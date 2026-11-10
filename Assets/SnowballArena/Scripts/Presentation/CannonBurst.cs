using UnityEngine;

namespace SnowballArena
{
    // Deterministic authored mesh fragments; this effect has no physics or damage.
    public sealed class CannonBurst : MonoBehaviour
    {
        public Transform[] fragments;
        public const float Lifetime = 0.30f;
        private float age;
        private Vector3[] scales;

        private void Awake()
        {
            scales = new Vector3[fragments.Length];
            for (int i = 0; i < fragments.Length; i++) scales[i] = fragments[i].localScale;
        }

        public bool Advance(float seconds)
        {
            age += seconds;
            float t = Mathf.Clamp01(age / Lifetime);
            for (int i = 0; i < fragments.Length; i++)
            {
                float angle = i * 2.399963f;
                var direction = new Vector3(Mathf.Cos(angle), 0.3f + i % 3 * 0.18f, Mathf.Sin(angle));
                fragments[i].localPosition = direction * (age * 2.8f) + Vector3.down * (age * age * 2f);
                fragments[i].localScale = scales[i] * (1f - t);
                fragments[i].localRotation = Quaternion.Euler(i * 31 + age * 280, i * 47, age * 160);
            }
            return age < Lifetime;
        }
    }
}
