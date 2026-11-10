using UnityEngine;

namespace SnowballArena
{
    // Surface queries and future hazards depend on this component, not a circle equation.
    public class ArenaSurface : MonoBehaviour
    {
        public ArenaDefinition definition;
        public Transform[] hazardMounts = new Transform[0];
        public Vector3 AimPoint => transform.TransformPoint(definition != null ? definition.localAimPoint : Vector3.zero);
        // Shape extension point: derive a hazard lane from the actual boundary, not previewRadius.
        public virtual bool TryGetCannonLane(Vector3 outward, out Vector3 start, out Vector3 end)
        {
            start = end = Vector3.zero;
            var collider = GetComponent<Collider>();
            if (collider == null) return false;
            outward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
            var center = AimPoint; center.y = collider.bounds.max.y - 0.05f;
            float reach = collider.bounds.size.magnitude + Vector3.Distance(center, collider.bounds.center) + 2;
            if (!collider.Raycast(new Ray(center + outward * reach, -outward), out var a, reach * 2)
                || !collider.Raycast(new Ray(center - outward * reach, outward), out var b, reach * 2)) return false;
            start = a.point; end = b.point;
            start.y = end.y = collider.bounds.max.y + 0.8f;
            return Vector3.Distance(start, end) > 2;
        }
        public Vector3 VelocityAt(Vector3 point)
        {
            var body = GetComponentInParent<Rigidbody>();
            return body != null ? body.GetPointVelocity(point) : Vector3.zero;
        }
    }
}
