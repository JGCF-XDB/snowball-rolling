using UnityEngine;

namespace SnowballArena
{
    // Surface queries and future hazards depend on this component, not a circle equation.
    public sealed class ArenaSurface : MonoBehaviour
    {
        public ArenaDefinition definition;
        public Transform[] hazardMounts = new Transform[0];
        public Vector3 AimPoint => transform.TransformPoint(definition != null ? definition.localAimPoint : Vector3.zero);
        public Vector3 VelocityAt(Vector3 point)
        {
            var body = GetComponentInParent<Rigidbody>();
            return body != null ? body.GetPointVelocity(point) : Vector3.zero;
        }
    }
}
