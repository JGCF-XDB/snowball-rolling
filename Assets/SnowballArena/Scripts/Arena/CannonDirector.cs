using System.Collections.Generic;
using UnityEngine;

namespace SnowballArena
{
    public enum CannonStage { Waiting, Warning, Flying, Inactive }

    public sealed class CannonDirector : MonoBehaviour
    {
        public MatchController match;
        public float startAfterSeconds = 60f;
        public float flashHalfPeriod = 0.5f;
        public float projectileSpeed = 18f;
        public float projectileRadius = 0.3f;
        public CannonStage Stage { get; private set; } = CannonStage.Waiting;
        public float Elapsed { get; private set; }
        public int FlashCount { get; private set; }
        public int ShotsFired { get; private set; }
        public int CycleCount { get; private set; }
        public Vector3 LaneStart { get; private set; }
        public Vector3 LaneEnd { get; private set; }
        public Vector3 ShotPosition { get; private set; }
        public bool WarningVisible => warning != null && warning.enabled;
        public bool HasVisuals => root != null;
        private float stageTime, travelled, lastAngle = float.NaN;
        private GameObject root, shot;
        private LineRenderer warning;
        private Material metal, orange, dark, warningMaterial;
        private readonly Dictionary<MatchParticipant, Vector3> previousPositions = new Dictionary<MatchParticipant, Vector3>();

        public void ResetRound()
        {
            Stop(); Stage = CannonStage.Waiting; Elapsed = 0; stageTime = 0;
            FlashCount = ShotsFired = CycleCount = 0; lastAngle = float.NaN;
            previousPositions.Clear();
        }
        public void Stop()
        {
            ClearVisuals(); Stage = CannonStage.Inactive;
        }

        public void Advance(float deltaTime)
        {
            if (match == null || match.Phase != MatchPhase.Running || Stage == CannonStage.Inactive || deltaTime <= 0) return;
            float remaining = deltaTime;
            while (remaining > 0.00001f)
            {
                float duration = Stage == CannonStage.Waiting ? Mathf.Max(0, startAfterSeconds) :
                    Stage == CannonStage.Warning ? Mathf.Max(0.05f, flashHalfPeriod) * 6f :
                    Vector3.Distance(LaneStart, LaneEnd) / Mathf.Max(0.1f, projectileSpeed);
                float step = Mathf.Min(remaining, Mathf.Max(0, duration - stageTime));
                if (Stage == CannonStage.Flying) MoveShot(step, (deltaTime - remaining) / deltaTime, (deltaTime - remaining + step) / deltaTime);
                stageTime += step; Elapsed += step; remaining -= step;
                if (Stage == CannonStage.Warning)
                {
                    int half = Mathf.FloorToInt(stageTime / Mathf.Max(0.05f, flashHalfPeriod));
                    FlashCount = Mathf.Min(3, half / 2 + 1);
                    warning.enabled = half < 6 && half % 2 == 0;
                }
                if (stageTime + 0.00001f < duration) break;
                stageTime = 0;
                if (Stage == CannonStage.Warning) Fire();
                else if (!SpawnCannon()) { Stop(); break; }
            }
            foreach (var participant in match.Participants)
                if (participant.alive) previousPositions[participant] = participant.body.RigidBody.position;
        }

        private bool SpawnCannon()
        {
            ClearVisuals();
            float angle = float.IsNaN(lastAngle) ? Random.Range(0f, 360f) : lastAngle + Random.Range(65f, 295f);
            Vector3 outward = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.right;
            if (!match.surface.TryGetCannonLane(outward, out var start, out var end)) return false;
            lastAngle = Mathf.Repeat(angle, 360f);
            LaneStart = start; LaneEnd = end; travelled = 0;
            Stage = CannonStage.Warning; FlashCount = 1; CycleCount++;
            EnsureMaterials();
            root = new GameObject("Cannon hazard");
            Vector3 direction = (end - start).normalized;
            var cannon = new GameObject("Cannon"); cannon.transform.SetParent(root.transform);
            cannon.transform.SetPositionAndRotation(start - Vector3.up * 0.8f, Quaternion.LookRotation(direction));
            Piece(cannon.transform, PrimitiveType.Cube, "Base", new Vector3(0, 0.22f, -0.25f), new Vector3(1.5f, 0.44f, 1.5f), metal);
            Piece(cannon.transform, PrimitiveType.Cylinder, "Barrel", new Vector3(0, 0.8f, -0.55f), new Vector3(0.8f, 0.65f, 0.8f), metal, Quaternion.Euler(90, 0, 0));
            Piece(cannon.transform, PrimitiveType.Cylinder, "Muzzle", new Vector3(0, 0.8f, 0.11f), new Vector3(0.61f, 0.025f, 0.61f), dark, Quaternion.Euler(90, 0, 0));
            Piece(cannon.transform, PrimitiveType.Cube, "Warning lamp", new Vector3(0, 1.25f, -0.7f), new Vector3(0.35f, 0.2f, 0.35f), orange);
            for (int i = -1; i <= 1; i += 2)
                Piece(cannon.transform, PrimitiveType.Cylinder, "Wheel", new Vector3(i * 0.83f, 0.4f, -0.45f), new Vector3(0.8f, 0.14f, 0.8f), dark, Quaternion.Euler(0, 0, 90));
            warning = new GameObject("Three-flash diameter warning").AddComponent<LineRenderer>();
            warning.transform.SetParent(root.transform); warning.sharedMaterial = warningMaterial;
            warning.useWorldSpace = true; warning.positionCount = 2; warning.widthMultiplier = projectileRadius * 2;
            warning.SetPosition(0, start - Vector3.up * 0.75f); warning.SetPosition(1, end - Vector3.up * 0.75f);
            warning.enabled = true;
            return true;
        }

        private void Fire()
        {
            warning.enabled = false; FlashCount = 3; ShotsFired++;
            Stage = CannonStage.Flying; travelled = 0; ShotPosition = LaneStart;
            shot = Piece(root.transform, PrimitiveType.Sphere, "Lethal cannonball", Vector3.zero, Vector3.one * projectileRadius * 2, orange);
            shot.transform.position = ShotPosition;
        }

        private void MoveShot(float step, float fromFraction, float toFraction)
        {
            Vector3 from = ShotPosition;
            travelled = Mathf.Min(Vector3.Distance(LaneStart, LaneEnd), travelled + Mathf.Max(0.1f, projectileSpeed) * step);
            ShotPosition = LaneStart + (LaneEnd - LaneStart).normalized * travelled;
            // Sweep relative motion so a fast snowball and a fast projectile cannot cross
            // between physics steps without a hit. The shot penetrates until the far edge.
            foreach (var participant in match.Participants)
            {
                if (!participant.alive) continue;
                Vector3 current = participant.body.RigidBody.position;
                Vector3 previous = previousPositions.TryGetValue(participant, out var p) ? p : current;
                Vector3 a = from - Vector3.Lerp(previous, current, fromFraction), b = ShotPosition - Vector3.Lerp(previous, current, toFraction);
                Vector3 segment = b - a;
                float t = segment.sqrMagnitude > 0.000001f ? Mathf.Clamp01(-Vector3.Dot(a, segment) / segment.sqrMagnitude) : 0;
                float radius = participant.body.Radius + projectileRadius;
                if ((a + segment * t).sqrMagnitude <= radius * radius) match.Eliminate(participant, EliminationCause.Cannon);
            }
            shot.transform.position = ShotPosition;
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
        private void ClearVisuals()
        {
            if (root != null) { root.SetActive(false); Destroy(root); }
            root = shot = null; warning = null;
        }
        private void OnDestroy()
        {
            ClearVisuals();
            foreach (var material in new[] { metal, dark, orange, warningMaterial }) if (material != null) Destroy(material);
        }
    }
}
