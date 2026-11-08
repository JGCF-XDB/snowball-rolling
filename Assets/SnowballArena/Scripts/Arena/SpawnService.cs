using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnowballArena
{
    public static class SpawnService
    {
        public static bool TryGetPositions(ArenaSurface surface, int count, float radius, out List<Vector3> positions)
        {
            positions = new List<Vector3>();
            var collider = surface.GetComponent<Collider>();
            if (collider == null || count < 1) return false;
            Bounds bounds = collider.bounds;
            var physics = surface.gameObject.scene.GetPhysicsScene();
            float step = radius * 2f + 0.8f, clearance = radius + 0.6f;
            var candidates = new List<Vector3>();
            for (float x = bounds.min.x + clearance; x <= bounds.max.x - clearance; x += step)
                for (float z = bounds.min.z + clearance; z <= bounds.max.z - clearance; z += step)
                {
                    Vector3 point = new Vector3(x, bounds.max.y + 2, z);
                    if (!physics.Raycast(point, Vector3.down, out var hit, bounds.size.y + 4, 1 << surface.gameObject.layer, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.collider.GetComponentInParent<ArenaSurface>() != surface || hit.normal.y < 0.9f) continue;
                    bool safe = true;
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * Mathf.PI / 4;
                        Vector3 check = point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * clearance;
                        if (!physics.Raycast(check, Vector3.down, out var edge, bounds.size.y + 4, 1 << surface.gameObject.layer, QueryTriggerInteraction.Ignore)
                            || edge.collider.GetComponentInParent<ArenaSurface>() != surface || edge.normal.y < 0.9f) { safe = false; break; }
                    }
                    if (safe) candidates.Add(hit.point + Vector3.up * (radius + 0.05f));
                }
            Vector3 preferred = bounds.center + Vector3.back * bounds.extents.z * 0.5f;
            while (positions.Count < count && candidates.Count > 0)
            {
                int best = -1; float bestScore = float.NegativeInfinity;
                for (int i = 0; i < candidates.Count; i++)
                {
                    float score = positions.Count == 0 ? -(candidates[i] - preferred).sqrMagnitude : float.PositiveInfinity;
                    foreach (var occupied in positions) score = Mathf.Min(score, (candidates[i] - occupied).sqrMagnitude);
                    if (score > bestScore) { best = i; bestScore = score; }
                }
                positions.Add(candidates[best]); candidates.RemoveAt(best);
            }
            return positions.Count == count;
        }
    }
}
