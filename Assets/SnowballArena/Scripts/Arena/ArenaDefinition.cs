using UnityEngine;

namespace SnowballArena
{
    [CreateAssetMenu(menuName = "Snowball Arena/Arena Definition")]
    public sealed class ArenaDefinition : ScriptableObject
    {
        [Min(2f)] public float previewRadius = 11f;
        [Min(0.1f)] public float thickness = 2.2f;
        public float fallHeight = -7f;
        public Vector3 spawnPosition = new Vector3(0f, 1f, -2f);
        public Vector3 localAimPoint = Vector3.zero;
    }
}
