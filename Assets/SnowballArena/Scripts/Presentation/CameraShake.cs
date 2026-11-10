using UnityEngine;

namespace SnowballArena
{
    // Short positional shake for a fixed overhead camera. Rotation is left alone so the
    // arena framing never tilts; only local position is offset for the shake window.
    public sealed class CameraShake : MonoBehaviour
    {
        private Vector3 basePosition;
        private float amplitude;
        private float duration;
        private float remaining;

        private void Awake() => basePosition = transform.localPosition;
        private void OnEnable() => basePosition = transform.localPosition;

        public void Shake(float strength, float seconds)
        {
            amplitude = Mathf.Max(amplitude, Mathf.Max(0f, strength));
            duration = Mathf.Max(0.01f, seconds);
            remaining = Mathf.Max(remaining, seconds);
        }

        public void Stop()
        {
            if (amplitude > 0f) transform.localPosition = basePosition;
            amplitude = 0f; duration = 0f; remaining = 0f;
        }

        private void LateUpdate()
        {
            if (remaining <= 0f) return;
            float step = Time.unscaledDeltaTime;
            remaining -= step;
            if (remaining <= 0f) { Stop(); return; }
            // Decay smoothly so a burst of hits does not look like a permanent jitter.
            float falloff = Mathf.Clamp01(remaining / duration);
            Vector3 offset = Random.insideUnitSphere * (amplitude * falloff);
            offset.y *= 0.35f;
            transform.localPosition = basePosition + offset;
        }
    }
}
