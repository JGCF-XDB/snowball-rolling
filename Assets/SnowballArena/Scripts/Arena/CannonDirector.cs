using System.Collections.Generic;
using UnityEngine;

namespace SnowballArena
{
    public enum CannonStage { Waiting, Warning, Flying, Inactive }

    public sealed class CannonState
    {
        public CannonStage Stage { get; internal set; }
        public int FlashCount { get; internal set; }
        public Vector3 LaneStart { get; internal set; }
        public Vector3 LaneEnd { get; internal set; }
        public Vector3 ShotPosition { get; internal set; }
        public bool WarningVisible => warning != null && warning.enabled;
        public bool HasProjectile => shot != null;
        internal float stageTime, travelled, angle = float.NaN;
        internal GameObject root, shot;
        internal LineRenderer warning;
        internal LineRenderer trail;
        internal CannonVisuals visuals;
    }

    public sealed class CannonDirector : MonoBehaviour
    {
        public MatchController match;
        public float startAfterSeconds = 20f;
        public float escalationInterval = 20f;
        public float flashHalfPeriod = 0.5f;
        public float projectileSpeed = 18f;
        public float projectileRadius = 0.3f;
        public float Elapsed { get; private set; }
        public int ShotsFired { get; private set; }
        public int CycleCount { get; private set; }
        public int ActiveEffectCount => bursts.Count;
        public IReadOnlyList<CannonState> Cannons => cannons;
        public int ActiveCannonCount => cannons.Count;
        public int ProjectileCount
        {
            get { int count = 0; foreach (var c in cannons) if (c.HasProjectile) count++; return count; }
        }
        public int WarningCount
        {
            get { int count = 0; foreach (var c in cannons) if (c.Stage == CannonStage.Warning) count++; return count; }
        }
        public CannonStage Stage => stopped ? CannonStage.Inactive : HasProjectile ? CannonStage.Flying :
            cannons.Count > 0 ? CannonStage.Warning : CannonStage.Waiting;
        // First cannon diagnostics retained for the single-cannon smoke tests.
        public int FlashCount => cannons.Count > 0 ? cannons[0].FlashCount : 0;
        public Vector3 LaneStart => cannons.Count > 0 ? cannons[0].LaneStart : Vector3.zero;
        public Vector3 LaneEnd => cannons.Count > 0 ? cannons[0].LaneEnd : Vector3.zero;
        public Vector3 ShotPosition => cannons.Count > 0 ? cannons[0].ShotPosition : Vector3.zero;
        public bool WarningVisible
        {
            get { foreach (var c in cannons) if (c.WarningVisible) return true; return false; }
        }
        public bool HasVisuals => cannons.Count > 0;
        public bool HasProjectile => ProjectileCount > 0;
        private bool stopped;
        private readonly List<CannonState> cannons = new List<CannonState>();
        private Material warningMaterial;
        private CannonArt art;
        private readonly List<CannonBurst> bursts = new List<CannonBurst>();
        private readonly Dictionary<MatchParticipant, Vector3> previousPositions = new Dictionary<MatchParticipant, Vector3>();
        private float NextSpawnTime => cannons.Count < 3 ? Mathf.Max(0, startAfterSeconds) + cannons.Count * Mathf.Max(0.1f, escalationInterval) : float.PositiveInfinity;
        private float Duration(CannonState c) => c.Stage == CannonStage.Warning ? Mathf.Max(0.05f, flashHalfPeriod) * 6f :
            Vector3.Distance(c.LaneStart, c.LaneEnd) / Mathf.Max(0.1f, projectileSpeed);

        public void ResetRound()
        {
            Stop(); stopped = false; Elapsed = 0;
            ShotsFired = CycleCount = 0; previousPositions.Clear();
        }
        public void Stop()
        {
            foreach (var c in cannons) ClearVisuals(c);
            foreach (var burst in bursts) { burst.gameObject.SetActive(false); Destroy(burst.gameObject); }
            bursts.Clear();
            cannons.Clear(); stopped = true;
        }

        public void Advance(float deltaTime)
        {
            if (match == null || match.Phase != MatchPhase.Running || stopped || deltaTime <= 0) return;
            float remaining = deltaTime;
            while (remaining > 0.00001f && !stopped)
            {
                // All cannons share one clock. Split at upgrades, stage boundaries and
                // the earliest impact across ALL projectiles, independent of list order.
                float step = Mathf.Min(remaining, Mathf.Max(0, NextSpawnTime - Elapsed));
                foreach (var c in cannons) step = Mathf.Min(step, Mathf.Max(0, Duration(c) - c.stageTime));
                float fromFraction = (deltaTime - remaining) / deltaTime;
                float toFraction = (deltaTime - remaining + step) / deltaTime;
                CannonState hitCannon = null;
                MatchParticipant victim = null;
                float earliest = float.PositiveInfinity;
                foreach (var c in cannons)
                {
                    if (c.Stage != CannonStage.Flying) continue;
                    float fraction = FindHit(c, step, fromFraction, toFraction, out var target);
                    if (fraction < earliest) { earliest = fraction; hitCannon = c; victim = target; }
                }
                if (hitCannon != null) step *= earliest;
                AdvanceEffects(step);
                foreach (var c in cannons)
                {
                    c.stageTime += step;
                    if (c.Stage != CannonStage.Flying) continue;
                    c.travelled = Mathf.Min(Vector3.Distance(c.LaneStart, c.LaneEnd), c.travelled + Mathf.Max(0.1f, projectileSpeed) * step);
                    c.ShotPosition = c.LaneStart + (c.LaneEnd - c.LaneStart).normalized * c.travelled;
                    c.shot.transform.position = c.ShotPosition;
                    c.shot.transform.rotation = Quaternion.LookRotation((c.LaneEnd - c.LaneStart).normalized)
                        * Quaternion.AngleAxis(c.travelled / Mathf.Max(0.01f, projectileRadius) * Mathf.Rad2Deg, Vector3.forward);
                    c.visuals.Advance(step);
                    UpdateTrail(c);
                }
                Elapsed += step; remaining -= step;
                if (hitCannon != null)
                {
                    EmitImpact(hitCannon.ShotPosition, (hitCannon.LaneEnd - hitCannon.LaneStart).normalized);
                    match.Eliminate(victim, EliminationCause.Cannon);
                    if (!SpawnCannon(hitCannon)) { Stop(); break; }
                }
                foreach (var c in cannons)
                {
                    if (c.stageTime + 0.00001f >= Duration(c))
                    {
                        c.stageTime = 0;
                        if (c.Stage == CannonStage.Warning) Fire(c);
                        else if (!SpawnCannon(c)) { Stop(); break; }
                    }
                    if (c.Stage == CannonStage.Warning)
                    {
                        int half = Mathf.FloorToInt(c.stageTime / Mathf.Max(0.05f, flashHalfPeriod));
                        c.FlashCount = Mathf.Min(3, half / 2 + 1);
                        c.warning.enabled = half < 6 && half % 2 == 0;
                        c.visuals.SetWarning(c.warning.enabled);
                    }
                }
                if (stopped) break;
                if (Elapsed + 0.00001f >= NextSpawnTime)
                {
                    var extra = new CannonState();
                    if (!SpawnCannon(extra)) { Stop(); break; }
                    cannons.Add(extra);
                }
            }
            foreach (var participant in match.Participants)
                if (participant.alive) previousPositions[participant] = participant.body.RigidBody.position;
        }

        private bool AngleAvailable(CannonState c, float angle)
        {
            if (!float.IsNaN(c.angle) && Mathf.Abs(Mathf.DeltaAngle(c.angle, angle)) < 65f) return false;
            foreach (var other in cannons)
            {
                if (other == c) continue;
                float separation = Mathf.Abs(Mathf.DeltaAngle(other.angle, angle));
                // Opposite rim positions share the same diameter, so exclude those too.
                if (separation < 25f || separation > 155f) return false;
            }
            return true;
        }
        private bool SpawnCannon(CannonState c)
        {
            float angle = Random.Range(0f, 360f);
            bool found = false;
            for (int i = 0; i < 360; i++)
            {
                if (AngleAvailable(c, angle)) { found = true; break; }
                angle = Mathf.Repeat(angle + 1f, 360f);
            }
            if (!found) return false;
            Vector3 outward = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.right;
            if (!match.surface.TryGetCannonLane(outward, out var start, out var end)) return false;
            ClearVisuals(c);
            c.angle = angle; c.stageTime = c.travelled = 0;
            c.LaneStart = start; c.LaneEnd = end;
            c.Stage = CannonStage.Warning; c.FlashCount = 1; CycleCount++;
            EnsureMaterials();
            c.root = new GameObject("Cannon hazard");
            Vector3 direction = (end - start).normalized;
            var cannon = Instantiate(art.cannonPrefab, c.root.transform);
            c.visuals = cannon.GetComponent<CannonVisuals>();
            cannon.transform.localScale = Vector3.one * (projectileRadius / art.projectileRadius);
            cannon.transform.rotation = Quaternion.LookRotation(direction);
            cannon.transform.position = start - cannon.transform.TransformVector(c.visuals.muzzle.localPosition);
            c.visuals.SetWarning(true);
            c.warning = new GameObject("Three-flash diameter warning").AddComponent<LineRenderer>();
            c.warning.transform.SetParent(c.root.transform); c.warning.sharedMaterial = warningMaterial;
            c.warning.useWorldSpace = true; c.warning.positionCount = 2; c.warning.widthMultiplier = projectileRadius * 2;
            c.warning.SetPosition(0, start - Vector3.up * 0.75f); c.warning.SetPosition(1, end - Vector3.up * 0.75f);
            c.warning.enabled = true;
            return true;
        }

        private void Fire(CannonState c)
        {
            c.warning.enabled = false; c.FlashCount = 3; ShotsFired++;
            c.Stage = CannonStage.Flying; c.travelled = 0; c.ShotPosition = c.LaneStart;
            c.shot = Instantiate(art.projectilePrefab, c.ShotPosition, Quaternion.LookRotation((c.LaneEnd - c.LaneStart).normalized), c.root.transform);
            c.shot.transform.localScale = Vector3.one * (projectileRadius / art.projectileRadius);
            c.visuals.Fire();
            c.trail = new GameObject("Cannonball flight trail").AddComponent<LineRenderer>();
            c.trail.transform.SetParent(c.root.transform, false);
            c.trail.sharedMaterial = art.trailMaterial;
            c.trail.useWorldSpace = true; c.trail.positionCount = 2;
            c.trail.widthMultiplier = projectileRadius;
            c.trail.widthCurve = AnimationCurve.Linear(0, 0, 1, 1);
            c.trail.numCapVertices = 3;
            UpdateTrail(c);
        }

        private void UpdateTrail(CannonState c)
        {
            // Derive the short tail from actual travel, so it also works when a
            // diagnostic advances several physics steps without rendering frames.
            float length = Mathf.Min(c.travelled, Mathf.Max(0.1f, projectileSpeed) * 0.12f);
            Vector3 direction = (c.LaneEnd - c.LaneStart).normalized;
            c.trail.SetPosition(0, c.ShotPosition - direction * length);
            c.trail.SetPosition(1, c.ShotPosition);
            c.trail.enabled = length > 0.001f;
        }

        private void EmitImpact(Vector3 point, Vector3 direction)
        {
            var burst = Instantiate(art.impactPrefab, point, Quaternion.LookRotation(direction), transform).GetComponent<CannonBurst>();
            burst.transform.localScale = Vector3.one * (projectileRadius / art.projectileRadius);
            bursts.Add(burst);
        }

        private void AdvanceEffects(float seconds)
        {
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                if (bursts[i].Advance(seconds)) continue;
                bursts[i].gameObject.SetActive(false); Destroy(bursts[i].gameObject);
                bursts.RemoveAt(i);
            }
        }

        private float FindHit(CannonState c, float step, float fromFraction, float toFraction, out MatchParticipant firstHit)
        {
            Vector3 from = c.ShotPosition;
            float nextTravelled = Mathf.Min(Vector3.Distance(c.LaneStart, c.LaneEnd), c.travelled + Mathf.Max(0.1f, projectileSpeed) * step);
            Vector3 end = c.LaneStart + (c.LaneEnd - c.LaneStart).normalized * nextTravelled;
            firstHit = null;
            float earliest = float.PositiveInfinity;
            foreach (var participant in match.Participants)
            {
                if (!participant.alive) continue;
                Vector3 current = participant.body.RigidBody.position;
                Vector3 previous = previousPositions.TryGetValue(participant, out var p) ? p : current;
                Vector3 a = from - Vector3.Lerp(previous, current, fromFraction), b = end - Vector3.Lerp(previous, current, toFraction);
                Vector3 segment = b - a;
                float radius = participant.body.Radius + projectileRadius;
                float distanceSquared = a.sqrMagnitude - radius * radius;
                float t = 0;
                if (distanceSquared > 0)
                {
                    float lengthSquared = segment.sqrMagnitude;
                    float projection = Vector3.Dot(a, segment);
                    float discriminant = projection * projection - lengthSquared * distanceSquared;
                    if (lengthSquared < 0.000001f || projection >= 0 || discriminant < 0) continue;
                    t = (-projection - Mathf.Sqrt(discriminant)) / lengthSquared;
                    if (t < 0 || t > 1) continue;
                }
                if (t < earliest) { earliest = t; firstHit = participant; }
            }
            return earliest;
        }

        private void EnsureMaterials()
        {
            if (warningMaterial != null) return;
            art = Resources.Load<CannonArt>("CannonArt");
            if (art == null) throw new System.InvalidOperationException("Selected cannon assets are missing. Run Rebuild Selected Cannon B.");
            warningMaterial = new Material(Resources.Load<Material>("CannonSignal")) { name = "Cannon warning" };
            warningMaterial.SetColor("_BaseColor", new Color(1f, 0.055f, 0.025f));
        }
        private static void ClearVisuals(CannonState c)
        {
            if (c.root != null) { c.root.SetActive(false); Destroy(c.root); }
            c.root = c.shot = null; c.warning = c.trail = null; c.Stage = CannonStage.Inactive;
            c.visuals = null;
        }
        private void OnDestroy()
        {
            Stop();
            if (warningMaterial != null) Destroy(warningMaterial);
        }
    }
}
