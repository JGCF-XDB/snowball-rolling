using UnityEngine;

namespace SnowballArena
{
    // Clip table loaded from Resources so the feedback layer needs no scene or prefab wiring.
    // Built by Snowball Arena > Build Audio Library.
    [CreateAssetMenu(menuName = "Snowball Arena/Audio Library")]
    public sealed class AudioLibrary : ScriptableObject
    {
        [Header("Snowball impacts")]
        public AudioClip[] softHits = new AudioClip[0];
        public AudioClip[] hardHits = new AudioClip[0];
        public AudioClip[] groundHits = new AudioClip[0];

        [Header("Rolling")]
        public AudioClip rollLoop;

        [Header("Interface")]
        public AudioClip click;
        public AudioClip hover;
        public AudioClip confirm;
        public AudioClip countdownTick;

        [Header("Results")]
        public AudioClip victory;
        public AudioClip defeat;

        [Header("Cannons")]
        public AudioClip cannonDeploy;
        public AudioClip cannonWarning;
        public AudioClip cannonFire;
        public AudioClip cannonFlight;
        public AudioClip cannonExplosion;

        [Header("Rewards")]
        public AudioClip knockdown;

        [Header("Music")]
        public AudioClip musicMenu;
        public AudioClip musicMatch;

        // Weak taps use the soft set; real collisions switch to the heavier recordings.
        public AudioClip ImpactClip(float strength) =>
            Pick(strength > 0.55f ? hardHits : softHits) ?? Pick(softHits) ?? Pick(hardHits);

        public AudioClip GroundClip() => Pick(groundHits) ?? Pick(softHits);

        public static AudioClip Pick(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;
            return clips[Random.Range(0, clips.Length)];
        }
    }
}
