using UnityEngine;

namespace SnowballArena
{
    [CreateAssetMenu(menuName = "Snowball Arena/Snowball Settings")]
    public sealed class SnowballSettings : ScriptableObject
    {
        [Min(0.1f)] public float initialRadius = 0.65f;
        [Min(1f)] public float density = 25f;
        [Min(0f)] public float driveForce = 155f;
        [Tooltip("Ground-only force per unit of horizontal speed. No hard speed clamp.")]
        [Min(0f)] public float groundDrag = 17f;
        [Tooltip("Cubic metres of collected snow per metre of actual surface rolling. No size cap.")]
        [Min(0f)] public float volumePerMetre = 0.16f;
        [Min(0.01f)] public float supportProbeGap = 0.06f;
        [Range(0f, 1f)] public float minimumSupportNormal = 0.6f;
        public float InitialMass => density * (4f / 3f) * Mathf.PI * Mathf.Pow(initialRadius, 3);
    }
}
