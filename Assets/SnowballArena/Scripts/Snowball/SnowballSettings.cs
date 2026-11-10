using UnityEngine;

namespace SnowballArena
{
    [CreateAssetMenu(menuName = "Snowball Arena/Snowball Settings")]
    public sealed class SnowballSettings : ScriptableObject
    {
        [Min(0.1f)] public float initialRadius = 0.85f;
        [Min(1f)] public float density = 25f;
        [Min(0f)] public float driveForce = 2517.12f;
        [Tooltip("Ground-only force per unit of horizontal speed. No hard speed clamp.")]
        [Min(0f)] public float groundDrag = 50f;
        [Tooltip("Lower limit of commanded force per kg; rolling inertia still affects actual acceleration.")]
        [Min(0f)] public float minimumDriveAcceleration = 11.52f;
        [Range(0.05f, 1f)] public float massControlExponent = 0.55f;
        [Range(0f, 0.5f)] public float massSpeedExponent = 0.08f;
        [Tooltip("Cubic metres of collected snow per metre of actual surface rolling. No size cap.")]
        [Min(0f)] public float volumePerMetre = 0.16f;
        [Min(0.01f)] public float supportProbeGap = 0.06f;
        [Range(0f, 1f)] public float minimumSupportNormal = 0.6f;
        public float InitialMass => density * (4f / 3f) * Mathf.PI * Mathf.Pow(initialRadius, 3);

        public float DriveForceForMass(float mass)
        {
            float initialMass = Mathf.Max(0.001f, InitialMass);
            float ratio = Mathf.Max(1f, mass / initialMass);
            float initialAcceleration = driveForce / initialMass;
            float floor = Mathf.Min(minimumDriveAcceleration, initialAcceleration);
            float acceleration = floor + (initialAcceleration - floor) * Mathf.Pow(ratio, -massControlExponent);
            return initialMass * ratio * acceleration;
        }

        public float GroundDragForMass(float mass)
        {
            if (driveForce <= 0f) return groundDrag;
            float ratio = Mathf.Max(1f, mass / Mathf.Max(0.001f, InitialMass));
            // Keep stronger heavy-ball control from creating unlimited cruising speed.
            // This remains a physical opposing force, never a velocity clamp.
            return groundDrag * DriveForceForMass(mass) / driveForce
                * Mathf.Min(1.6f, Mathf.Pow(ratio, massSpeedExponent));
        }
    }
}
