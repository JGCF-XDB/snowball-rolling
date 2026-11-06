using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnowballArena
{
    [DefaultExecutionOrder(-100), RequireComponent(typeof(SnowballBody), typeof(SnowballMotor))]
    public sealed class SnowballGrowth : MonoBehaviour
    {
        public double RolledDistance { get; private set; }
        private SnowballBody ball;
        private SnowballMotor motor;
        private Transform previousSurface;
        private Vector3 previousLocalCenter;
        private bool tracking;

        private void FixedUpdate() => SampleStep(Time.fixedDeltaTime);
        private void OnDisable() => tracking = false;

        public void ResetTracking()
        {
            RolledDistance = 0;
            tracking = false;
            previousSurface = null;
        }

        public void SampleStep(float deltaTime)
        {
            if (ball == null) ball = GetComponent<SnowballBody>();
            if (motor == null) motor = GetComponent<SnowballMotor>();
            if (ball.RigidBody == null) ball.Initialize();
            var settings = ball.settings;
            if (!ArenaQuery.TrySupport(gameObject.scene.GetPhysicsScene(), ball.RigidBody.position,
                ball.Radius, motor.supportLayers, settings.supportProbeGap, settings.minimumSupportNormal, out var hit))
            {
                tracking = false;
                return;
            }
            Transform surface = hit.collider.transform;
            Vector3 localCenter = surface.InverseTransformPoint(ball.RigidBody.position);
            if (tracking && previousSurface == surface && deltaTime > 0f)
            {
                Vector3 traveled = Vector3.ProjectOnPlane(surface.TransformVector(localCenter - previousLocalCenter), hit.normal);
                var arenaSurface = hit.collider.GetComponentInParent<ArenaSurface>();
                Vector3 snowVelocity = arenaSurface != null ? arenaSurface.VelocityAt(hit.point) : Vector3.zero;
                Vector3 platformSpin = hit.rigidbody != null ? hit.rigidbody.angularVelocity : Vector3.zero;
                float rollingSpeed = Vector3.ProjectOnPlane(ball.RigidBody.angularVelocity - platformSpin, hit.normal).magnitude * ball.Radius;
                // Pure sliding and spinning in place do not collect distance.
                double distance = System.Math.Min(traveled.magnitude, rollingSpeed * deltaTime);
                RolledDistance += distance;
                ball.Accrete(distance * settings.volumePerMetre, snowVelocity, true);
            }
            previousSurface = surface;
            // Store AFTER radius clearance adjustment so expansion cannot feed its own growth.
            previousLocalCenter = surface.InverseTransformPoint(ball.RigidBody.position);
            tracking = true;
        }
    }
}
