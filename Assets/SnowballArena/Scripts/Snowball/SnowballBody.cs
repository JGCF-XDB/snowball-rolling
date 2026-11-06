using UnityEngine;

namespace SnowballArena
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class SnowballBody : MonoBehaviour
    {
        public SnowballSettings settings;
        public Transform visual;
        public Rigidbody RigidBody { get; private set; }
        public float Radius { get; private set; }

        private void Awake() => Initialize();

        public void Initialize()
        {
            RigidBody = GetComponent<Rigidbody>();
            Radius = settings.initialRadius;
            GetComponent<SphereCollider>().radius = Radius;
            if (visual != null) visual.localScale = Vector3.one * (Radius * 2f);
            RigidBody.mass = settings.InitialMass;
            RigidBody.useGravity = true;
            RigidBody.linearDamping = 0f;
            RigidBody.angularDamping = 0.12f;
            RigidBody.maxAngularVelocity = 80f;
            RigidBody.interpolation = RigidbodyInterpolation.Interpolate;
            RigidBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            RigidBody.solverIterations = 12;
            RigidBody.solverVelocityIterations = 6;
            RigidBody.ResetInertiaTensor();
        }

        public void ResetAt(Vector3 position)
        {
            Initialize();
            RigidBody.position = position;
            RigidBody.rotation = Quaternion.identity;
            RigidBody.linearVelocity = Vector3.zero;
            RigidBody.angularVelocity = Vector3.zero;
            RigidBody.WakeUp();
        }
    }
}
