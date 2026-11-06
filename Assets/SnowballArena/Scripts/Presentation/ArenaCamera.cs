using UnityEngine;

namespace SnowballArena
{
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public sealed class ArenaCamera : MonoBehaviour
    {
        public float minimumVerticalSize = 12.5f;
        public float arenaHalfWidth = 14f;
        private void LateUpdate()
        {
            var cameraComponent = GetComponent<Camera>();
            cameraComponent.orthographicSize = Mathf.Max(minimumVerticalSize, arenaHalfWidth / Mathf.Max(0.1f, cameraComponent.aspect));
        }
    }
}
