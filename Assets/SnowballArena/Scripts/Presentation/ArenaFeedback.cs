using UnityEngine;

namespace SnowballArena
{
    // One place for everything that sells a collision: sound, a snow puff, a camera kick and
    // a very short hit stop. Created at runtime, so no scene or prefab has to be re-authored.
    public sealed class ArenaFeedback : MonoBehaviour
    {
        public static ArenaFeedback Instance { get; private set; }

        [Header("Mix")]
        [Range(0f, 1f)] public float masterVolume = 0.85f;
        [Min(1)] public int voiceCount = 10;
        [Tooltip("Hit stop is skipped in batch runs so automated matches keep real-time pacing.")]
        public float hitstopTimeScale = 0.15f;

        [Header("Impact feel")]
        [Tooltip("Impact strength needed before the camera moves at all. Below this only sound, snow and squash react.")]
        [Range(0f, 1f)] public float shakeThreshold = 0.48f;
        [Min(0f)] public float shakeMinAmplitude = 0.07f;
        [Min(0f)] public float shakeMaxAmplitude = 0.3f;
        [Tooltip("Impact strength that bursts the extra snow ring.")]
        [Range(0f, 1f)] public float burstThreshold = 0.62f;
        [Tooltip("Impact strength that earns a hit stop. Deliberately rare, so it reads as a reward.")]
        [Range(0f, 1f)] public float hitstopThreshold = 0.82f;
        [Min(0f)] public float hitstopMinSeconds = 0.02f;
        [Min(0f)] public float hitstopMaxSeconds = 0.035f;

        [Header("Music")]
        [Range(0f, 1f)] public float musicVolume = 0.15f;

        private AudioLibrary library;
        private AudioSource[] voices;
        private int nextVoice;
        private ParticleSystem burst;
        private CameraShake shake;
        private AudioSource music;
        private AudioClip musicClip;
        private MatchController match;
        private float hitstopUntil;
        private float resumeTimeScale = 1f;
        private bool audioAvailable;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Instance != null) return;
            var host = new GameObject("Arena Feedback");
            DontDestroyOnLoad(host);
            host.AddComponent<ArenaFeedback>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (library == null) library = Resources.Load<AudioLibrary>("SnowballAudioLibrary");
            // Unity pauses the audio listener while the window is unfocused. Keep the game
            // (and therefore its sound) running so alt-tabbing to the editor does not mute it.
            Application.runInBackground = true;
            // A batch run has no audio device, and its scripted waits must not be slowed down.
            audioAvailable = !Application.isBatchMode && Application.isPlaying;
            BuildVoices();
            BuildBurst();
            BuildMusic();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreTime();
        }

        private void Update()
        {
            if (hitstopUntil > 0f && Time.unscaledTime >= hitstopUntil) RestoreTime();
            UpdateMusic();
        }

        public AudioLibrary Clips => library;

        /// <summary>Ball-to-ball or ball-to-ground impact. strength is 0..1.</summary>
        public void Impact(Vector3 point, float strength, bool ground)
        {
            strength = Mathf.Clamp01(strength);
            AudioClip clip = library == null ? null : ground ? library.GroundClip() : library.ImpactClip(strength);
            if (clip != null)
                Play(clip, point, Mathf.Lerp(0.25f, 0.95f, strength) * masterVolume,
                    Random.Range(0.9f, 1.12f), true);
            if (!ground) Burst(point, strength);
            // Camera motion and hit stop are reserved for real collisions; ordinary contact
            // stays readable so the big hits still feel like a reward.
            if (!ground && strength >= shakeThreshold)
            {
                var shaker = Shake();
                if (shaker != null)
                {
                    float t = Mathf.InverseLerp(shakeThreshold, 1f, strength);
                    shaker.Shake(Mathf.Lerp(shakeMinAmplitude, shakeMaxAmplitude, t), 0.1f + 0.07f * t);
                }
            }
            if (!ground && strength >= hitstopThreshold)
                Hitstop(Mathf.Lerp(hitstopMinSeconds, hitstopMaxSeconds, Mathf.InverseLerp(hitstopThreshold, 1f, strength)));
        }

        /// <summary>
        /// Ball-on-ball contact the player is not part of. Audible so the arena has life, but kept
        /// well under the player's own channel: no shake, no puff, no hit stop.
        /// </summary>
        public void BallChatter(Vector3 point, float strength)
        {
            strength = Mathf.Clamp01(strength);
            AudioClip clip = library == null ? null : library.ImpactClip(strength);
            // Three quarters of the player's level: clearly present, still below a hit you caused.
            if (clip != null)
                Play(clip, point, Mathf.Lerp(0.25f, 0.95f, strength) * 0.75f * masterVolume, Random.Range(0.95f, 1.15f), true);
            // Snow still puffs off a bot collision; camera shake and hit stop stay reserved for
            // the player's own contacts.
            Burst(point, strength * 0.7f);
        }

        public void UiClick() => Play(library == null ? null : library.click, Vector3.zero, 0.7f * masterVolume, 1f, false);
        public void UiHover() => Play(library == null ? null : library.hover, Vector3.zero, 0.4f * masterVolume, 1f, false);
        public void UiConfirm() => Play(library == null ? null : library.confirm, Vector3.zero, 0.85f * masterVolume, 1f, false);
        public void UiTick() => Play(library == null ? null : library.countdownTick, Vector3.zero, 0.6f * masterVolume, 1f, false);

        public void Result(bool victory)
        {
            AudioClip clip = library == null ? null : victory ? library.victory : library.defeat;
            Play(clip, Vector3.zero, 0.95f * masterVolume, 1f, false);
        }

        // Cannon cues are 3D so the player can hear which side of the platform a shot comes from.
        public void CannonDeploy(Vector3 at) =>
            Play(library == null ? null : library.cannonDeploy, at, masterVolume, Random.Range(0.94f, 1.06f), true);

        // Raising the pitch per flash turns the two audible blips into a countdown.
        public void CannonWarning(Vector3 at, int flash) =>
            Play(library == null ? null : library.cannonWarning, at,
                Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(flash / 3f)) * masterVolume, 1f + 0.12f * flash, true);

        public void CannonFire(Vector3 at) =>
            Play(library == null ? null : library.cannonFire, at, masterVolume, Random.Range(0.92f, 1f), true);

        public void CannonFlight(Vector3 at) =>
            Play(library == null ? null : library.cannonFlight, at, 0.8f * masterVolume, Random.Range(0.95f, 1.08f), true);

        public void CannonExplosion(Vector3 at) =>
            Play(library == null ? null : library.cannonExplosion, at, masterVolume, Random.Range(0.9f, 1.05f), true);

        /// <summary>
        /// Extra kick for the cannonball killing a snowball. The boom itself is already on
        /// CannonExplosion; this only adds the impact camera move and, for the heaviest style,
        /// a very short hit stop so the explosion reads as the cause of the elimination.
        /// </summary>
        public void SnowballDestroyed(SnowballExplosionStyle style)
        {
            var shaker = Shake();
            if (shaker != null)
            {
                float amplitude = style == SnowballExplosionStyle.AvalancheBlast ? 0.22f
                    : style == SnowballExplosionStyle.IceShatter ? 0.15f : 0.11f;
                shaker.Shake(amplitude, style == SnowballExplosionStyle.AvalancheBlast ? 0.24f : 0.18f);
            }
            if (style == SnowballExplosionStyle.AvalancheBlast) Hitstop(0.028f);
        }

        /// <summary>Player knocked a rival off the platform. Played flat (2D) so it reads as a reward.</summary>
        public void Knockdown() =>
            Play(library == null ? null : library.knockdown, Vector3.zero, 0.9f * masterVolume, 1f, false);

        public void Hitstop(float seconds)
        {
            if (Application.isBatchMode) return;
            float until = Time.unscaledTime + Mathf.Max(0.01f, seconds);
            if (hitstopUntil > 0f && Time.unscaledTime < hitstopUntil)
            {
                // Already stretched: only extend the window. Re-sampling resumeTimeScale here
                // would capture the reduced scale and leave it stuck after the window ends.
                hitstopUntil = Mathf.Max(hitstopUntil, until);
                return;
            }
            resumeTimeScale = Time.timeScale > 0.01f ? Time.timeScale : 1f;
            Time.timeScale = Mathf.Clamp(hitstopTimeScale, 0.01f, 1f);
            hitstopUntil = until;
        }

        private void RestoreTime()
        {
            if (hitstopUntil <= 0f) return;
            hitstopUntil = 0f;
            // Something else (a smoke probe, a pause screen) may own timeScale now; only
            // undo our own stretch.
            if (Mathf.Approximately(Time.timeScale, Mathf.Clamp(hitstopTimeScale, 0.01f, 1f)))
                Time.timeScale = resumeTimeScale <= 0.01f ? 1f : resumeTimeScale;
        }

        private CameraShake Shake()
        {
            if (shake != null) return shake;
            var target = Camera.main;
            if (target == null) return null;
            shake = target.GetComponent<CameraShake>();
            if (shake == null) shake = target.gameObject.AddComponent<CameraShake>();
            return shake;
        }

        private void BuildVoices()
        {
            voices = new AudioSource[Mathf.Max(1, voiceCount)];
            for (int i = 0; i < voices.Length; i++)
            {
                // One host object per voice: 3D clips need their own transform to sit at
                // the impact point, otherwise every overlapping hit plays from one place.
                var voiceObject = new GameObject("Voice " + i);
                voiceObject.transform.SetParent(transform, false);
                var source = voiceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 10f;
                source.maxDistance = 70f;
                source.dopplerLevel = 0f;
                voices[i] = source;
            }
        }

        private void BuildMusic()
        {
            var host = new GameObject("Music");
            host.transform.SetParent(transform, false);
            music = host.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            music.spatialBlend = 0f;
            music.volume = 0f;
            music.priority = 0;
            music.dopplerLevel = 0f;
        }

        // Menu, match and results each pick their own track; the results screen is left
        // silent so the win/loss jingle is not competing with a loop.
        private void UpdateMusic()
        {
            if (!audioAvailable || library == null || music == null) return;
            if (match == null) match = FindAnyObjectByType<MatchController>();
            AudioClip wanted;
            if (match == null) wanted = library.musicMatch;              // practice arena scene
            else if (match.Phase == MatchPhase.Results) wanted = null;
            else if (match.Phase == MatchPhase.Menu) wanted = library.musicMenu;
            else wanted = library.musicMatch;
            ApplyMusic(wanted);
            float target = musicClip != null ? musicVolume : 0f;
            music.volume = Mathf.MoveTowards(music.volume, target, Time.unscaledDeltaTime * 1.2f);
        }

        private void ApplyMusic(AudioClip clip)
        {
            if (musicClip == clip) return;
            musicClip = clip;
            music.Stop();
            music.clip = clip;
            music.volume = 0f;
            if (clip != null) music.Play();
        }

        private void Play(AudioClip clip, Vector3 position, float volume, float pitch, bool spatial)
        {
            if (clip == null || !audioAvailable || voices == null || voices.Length == 0) return;
            AudioSource source = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            source.transform.position = position;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            source.spatialBlend = spatial ? 1f : 0f;
            source.Play();
        }

        private void BuildBurst()
        {
            var host = new GameObject("Snow Burst");
            host.transform.SetParent(transform, false);
            burst = host.AddComponent<ParticleSystem>();
            // Kept playing with emission disabled so Emit() lands in a simulated system.
            // A stopped system ignores emitters until something plays it again.
            burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = burst.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.45f;
            main.startSpeed = 0f;            // every particle supplies its own velocity
            main.startSize = 0.3f;
            main.startColor = new Color(0.95f, 0.98f, 1f, 0.9f);
            main.gravityModifier = 1.15f;
            main.maxParticles = 800;

            var emission = burst.emission;
            emission.enabled = false;
            var shape = burst.shape;
            shape.enabled = false;
            var colorOverLifetime = burst.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.86f, 0.94f, 1f), 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            var material = ParticleMaterial();
            if (material != null) renderer.material = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            burst.Play();
        }

        private static Material ParticleMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            // A stripped build may not include any of them; skip the burst material instead
            // of letting new Material(null) throw during Awake.
            if (shader == null) return null;
            var material = new Material(shader) { name = "Snow Burst Material" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", new Color(0.96f, 0.99f, 1f, 0.9f));
            return material;
        }

        private void Burst(Vector3 point, float strength)
        {
            if (burst == null) return;
            if (!burst.isPlaying) burst.Play();
            // Density was hiding the contact dent: a dozen large opaque puffs sat right on top
            // of the point we want the player to see.
            int count = Mathf.RoundToInt(Mathf.Lerp(5f, 16f, strength));
            for (int i = 0; i < count; i++) EmitOne(point, strength, 1f);
            // A near-maximum hit gets a wider, faster ring so the reward reads as a burst
            // rather than just a slightly bigger puff.
            if (strength >= burstThreshold)
                for (int i = 0; i < 20; i++) EmitOne(point, strength, 2.0f);
        }

        private void EmitOne(Vector3 point, float strength, float speedScale)
        {
            // Thrown mostly upward and outward, never straight through the ball, so the burst
            // reads as snow flying off rather than a cloud sitting over the impact.
            Vector3 flare = new Vector3(Random.Range(-1f, 1f), Random.Range(0.45f, 1.6f), Random.Range(-1f, 1f)).normalized;
            var emit = new ParticleSystem.EmitParams
            {
                position = point + Random.insideUnitSphere * 0.1f,
                velocity = (flare * Random.Range(1.6f, 3.6f) + Vector3.up * 1.1f) * speedScale,
                startSize = Random.Range(0.12f, 0.32f) * Mathf.Lerp(0.9f, 1.3f, strength),
                startLifetime = Random.Range(0.16f, 0.34f),
                startColor = new Color(0.95f, 0.98f, 1f, 0.55f),
                applyShapeToPosition = false,
            };
            burst.Emit(emit, 1);
        }
    }
}
