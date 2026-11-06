using UnityEngine;

namespace SnowballArena
{
    public static class ArenaQuery
    {
        public static bool TrySupport(PhysicsScene physicsScene, Vector3 center, float radius,
            LayerMask layers, float gap, float minNormal, out RaycastHit hit)
        {
            // A slightly smaller sweep avoids starting inside the ground collider.
            // Query actual geometry: works with polygons, holes and platform edges.
            bool found = physicsScene.SphereCast(center + Vector3.up * gap, radius * 0.9f,
                Vector3.down, out hit, radius * 0.1f + 2f * gap, layers, QueryTriggerInteraction.Ignore);
            return found && Vector3.Dot(hit.normal, Vector3.up) >= minNormal;
        }
    }
}
