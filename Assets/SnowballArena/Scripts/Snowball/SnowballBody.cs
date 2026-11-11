using UnityEngine;

namespace SnowballArena
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class SnowballBody : MonoBehaviour
    {
        public SnowballSettings settings;
        public Transform visual;
        [Tooltip("Set by the match controller. Only the player's ball drives the heavy-hit feedback.")]
        public bool isPlayer;
        public Rigidbody RigidBody { get; private set; }
        public float Radius { get; private set; }
        public double Volume { get; private set; }

        public SnowballBody LastKnockedBy { get; private set; }
        public float LastKnockTime { get; private set; } = float.NegativeInfinity;

        private void Awake() => Initialize();

        public void Initialize()
        {
            // Reset includes the knock bookkeeping: a reused ball must not inherit last round's shove.
            LastKnockedBy = null;
            LastKnockTime = float.NegativeInfinity;
            // Re-authored prefabs have lost this reference before; fall back to the conventional
            // child name so a new skin cannot silently disable squash and size updates.
            if (visual == null) visual = transform.Find("Visual");
            RigidBody = GetComponent<Rigidbody>();
            // Added at runtime so every existing prefab and scene keeps working without
            // re-authoring components; it only clips upward speed created by a snowball hit.
            if (!TryGetComponent(out SnowballImpact _)) gameObject.AddComponent<SnowballImpact>();
            if (!TryGetComponent(out SnowballFeedback _)) gameObject.AddComponent<SnowballFeedback>();
            Radius = settings.initialRadius;
            Volume = (4d / 3d) * System.Math.PI * Radius * Radius * Radius;
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
            UpdateInertia();
        }

        public void RecordKnock(SnowballBody attacker)
        {
            if (attacker == null || attacker == this) return;
            LastKnockedBy = attacker;
            LastKnockTime = Time.fixedTime;
        }

        private void UpdateInertia()
        {
            // Isotropic solid-sphere inertia: avoids inertia changing with mesh tessellation.
            RigidBody.inertiaTensorRotation = Quaternion.identity;
            RigidBody.inertiaTensor = Vector3.one * (0.4f * RigidBody.mass * Radius * Radius);
        }

        public void Accrete(double volume, Vector3 incomingSnowVelocity, bool maintainGroundClearance)
        {
            if (volume <= 0d) return;
            if (RigidBody == null) Initialize();
            float oldMass = RigidBody.mass, oldRadius = Radius;
            float oldInertia = 0.4f * oldMass * oldRadius * oldRadius;
            Vector3 oldVelocity = RigidBody.linearVelocity, oldSpin = RigidBody.angularVelocity;
            Volume += volume;
            Radius = (float)System.Math.Pow(Volume * 3d / (4d * System.Math.PI), 1d / 3d);
            float newMass = settings.MassForVolume(Volume);
            RigidBody.mass = newMass;
            GetComponent<SphereCollider>().radius = Radius;
            if (visual != null) visual.localScale = Vector3.one * (2f * Radius);
            UpdateInertia();
            // Accretion is an open system: incoming snow contributes linear momentum.
            // Symmetric accretion assumes no net incoming spin about the ball centre.
            RigidBody.linearVelocity = (oldMass * oldVelocity + (newMass - oldMass) * incomingSnowVelocity) / newMass;
            RigidBody.angularVelocity = oldSpin * (oldInertia / (0.4f * newMass * Radius * Radius));
            // Geometric ground-clearance correction only; no horizontal teleport or launch impulse.
            if (maintainGroundClearance) RigidBody.position += Vector3.up * (Radius - oldRadius);
        }

        public void ResetAt(Vector3 position)
        {
            Initialize();
            RigidBody.position = position;
            RigidBody.rotation = Quaternion.identity;
            RigidBody.linearVelocity = Vector3.zero;
            RigidBody.angularVelocity = Vector3.zero;
            RigidBody.WakeUp();
            GetComponent<SnowballGrowth>()?.ResetTracking();
        }
    }
}
