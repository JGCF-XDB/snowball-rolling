using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnowballArena
{
    [RequireComponent(typeof(SnowballBody))]
    public sealed class SnowballMotor : MonoBehaviour
    {
        public LayerMask supportLayers = 1 << 8;
        public bool controlEnabled = true;
        public bool HasSupport { get; private set; }
        public Vector3 DesiredDirection { get; private set; }
        private SnowballBody ball;

        public void SetMoveDirection(Vector3 direction)
        {
            DesiredDirection = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(direction, Vector3.up), 1f);
        }

        private void FixedUpdate() => SimulateDrive();

        public void SimulateDrive()
        {
            if (ball == null) ball = GetComponent<SnowballBody>();
            if (ball.RigidBody == null) ball.Initialize();
            var body = ball.RigidBody;
            var settings = ball.settings;
            HasSupport = ArenaQuery.TrySupport(gameObject.scene.GetPhysicsScene(), body.position,
                ball.Radius, supportLayers, settings.supportProbeGap, settings.minimumSupportNormal, out var hit);
            if (!HasSupport) return;

            var surface = hit.collider.GetComponentInParent<ArenaSurface>();
            Vector3 surfaceVelocity = surface != null ? surface.VelocityAt(hit.point) : Vector3.zero;
            Vector3 relativeVelocity = Vector3.ProjectOnPlane(body.linearVelocity - surfaceVelocity, hit.normal);
            // ForceMode.Force preserves mass-dependent acceleration. Never overwrite contact velocity.
            body.AddForce(-relativeVelocity * settings.groundDrag, ForceMode.Force);
            if (controlEnabled)
                body.AddForce(Vector3.ProjectOnPlane(DesiredDirection, hit.normal) * settings.driveForce, ForceMode.Force);
        }
    }
}
