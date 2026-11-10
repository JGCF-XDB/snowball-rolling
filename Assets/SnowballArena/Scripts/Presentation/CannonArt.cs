using UnityEngine;

namespace SnowballArena
{
    // Resources entry keeps the selected Blender meshes and their shaders in builds.
    public sealed class CannonArt : ScriptableObject
    {
        public GameObject cannonPrefab;
        public GameObject projectilePrefab;
        public GameObject impactPrefab;
        public Material trailMaterial;
        public float projectileRadius = 0.3f;
    }
}
