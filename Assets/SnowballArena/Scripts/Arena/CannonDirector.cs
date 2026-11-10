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
        private Material metal, orange, dark, warningMaterial;
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
                foreach (var c in cannons)
                {
                    c.stageTime += step;
                    if (c.Stage != CannonStage.Flying) continue;
                    c.travelled = Mathf.Min(Vector3.Distance(c.LaneStart, c.LaneEnd), c.travelled + Mathf.Max(0.1f, projectileSpeed) * step);
                    c.ShotPosition = c.LaneStart + (c.LaneEnd - c.LaneStart).normalized * c.travelled;
                    c.shot.transform.position = c.ShotPosition;
                }
                Elapsed += step; remaining -= step;
                if (hitCannon != null)
                {
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
            var cannon = new GameObject("Cannon"); cannon.transform.SetParent(c.root.transform);
            cannon.transform.SetPositionAndRotation(start - Vector3.up * 0.8f, Quaternion.LookRotation(direction));
            Piece(cannon.transform, PrimitiveType.Cube, "Base", new Vector3(0, 0.22f, -0.25f), new Vector3(1.5f, 0.44f, 1.5f), metal);
            Piece(cannon.transform, PrimitiveType.Cylinder, "Barrel", new Vector3(0, 0.8f, -0.55f), new Vector3(0.8f, 0.65f, 0.8f), metal, Quaternion.Euler(90, 0, 0));
            Piece(cannon.transform, PrimitiveType.Cylinder, "Muzzle", new Vector3(0, 0.8f, 0.11f), new Vector3(0.61f, 0.025f, 0.61f), dark, Quaternion.Euler(90, 0, 0));
            Piece(cannon.transform, PrimitiveType.Cube, "Warning lamp", new Vector3(0, 1.25f, -0.7f), new Vector3(0.35f, 0.2f, 0.35f), orange);
            for (int i = -1; i <= 1; i += 2)
                Piece(cannon.transform, PrimitiveType.Cylinder, "Wheel", new Vector3(i * 0.83f, 0.4f, -0.45f), new Vector3(0.8f, 0.14f, 0.8f), dark, Quaternion.Euler(0, 0, 90));
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
            c.shot = Piece(c.root.transform, PrimitiveType.Sphere, "Lethal cannonball", Vector3.zero, Vector3.one * projectileRadius * 2, orange);
            c.shot.transform.position = c.ShotPosition;
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
            if (metal != null) return;
            metal = MakeMaterial("Cannon steel", new Color(0.12f, 0.18f, 0.23f));
            dark = MakeMaterial("Cannon bore", new Color(0.015f, 0.02f, 0.025f));
            orange = MakeMaterial("Cannonball glow", new Color(1f, 0.32f, 0.025f), true);
            warningMaterial = MakeMaterial("Cannon warning", new Color(1f, 0.055f, 0.025f), true);
        }
        private static Material MakeMaterial(string name, Color color, bool signal = false)
        {
            // Lit is already referenced by the shipped arena materials, so it survives
            // player shader stripping (unreferenced runtime-only shaders do not).
            var template = signal ? Resources.Load<Material>("CannonSignal") : null;
            var material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = name;
            material.SetColor("_BaseColor", color); return material;
        }
        private static GameObject Piece(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
        {
            var item = GameObject.CreatePrimitive(type); item.name = name; item.layer = 2;
            var collider = item.GetComponent<Collider>(); collider.enabled = false; Object.Destroy(collider);
            item.transform.SetParent(parent, false); item.transform.localPosition = position;
            item.transform.localScale = scale; item.transform.localRotation = rotation ?? Quaternion.identity;
            item.GetComponent<Renderer>().sharedMaterial = material; return item;
        }
        private static void ClearVisuals(CannonState c)
        {
            if (c.root != null) { c.root.SetActive(false); Destroy(c.root); }
            c.root = c.shot = null; c.warning = null; c.Stage = CannonStage.Inactive;
        }
        private void OnDestroy()
        {
            Stop();
            foreach (var material in new[] { metal, dark, orange, warningMaterial }) if (material != null) Destroy(material);
        }
    }
}
