using UnityEngine;

namespace SnowballArena
{
    public sealed class CannonVisuals : MonoBehaviour
    {
        public Transform muzzle;
        public Transform barrel;
        public GameObject muzzleFlash;
        public Renderer[] signals;
        public Material signalOn, signalOff;
        public float muzzleInnerDiameter = 0.6f;
        public int muzzleSides = 48;
        private float shotAge = 1f;
        private Vector3 restPosition;

        private void Awake() { restPosition = barrel.localPosition; }

        public void SetWarning(bool visible)
        {
            foreach (var signal in signals) signal.sharedMaterial = visible ? signalOn : signalOff;
        }

        public void Fire()
        {
            shotAge = 0;
            muzzleFlash.SetActive(true);
            SetWarning(true);
        }

        public void Advance(float seconds)
        {
            shotAge += seconds;
            float recoil = shotAge < 0.045f ? shotAge / 0.045f
                : Mathf.Clamp01((0.22f - shotAge) / 0.175f);
            barrel.localPosition = restPosition - Vector3.forward * (0.12f * recoil);
            muzzleFlash.SetActive(shotAge < 0.07f);
            if (shotAge >= 0.07f && shotAge < 1f) SetWarning(false);
        }
    }
}
