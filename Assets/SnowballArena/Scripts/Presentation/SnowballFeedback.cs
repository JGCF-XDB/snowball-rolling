using UnityEngine;

namespace SnowballArena
{
    // Per-ball feel: reports impacts to the arena feedback layer, squashes the visual mesh
    // along the contact axis and keeps a looping snow-roll sound in step with real speed.
    [RequireComponent(typeof(SnowballBody), typeof(SnowballMotor))]
    [DefaultExecutionOrder(-15)]
    public sealed class SnowballFeedback : MonoBehaviour
    {
        [Header("Impact")]
        [Tooltip("Approach speed below which an impact is not worth a sound or a puff, in m/s.")]
        public float minimumImpactSpeed = 2.5f;
        [Tooltip("Approach speed treated as a maximum-strength impact, in m/s. Matches the speed a real collision reaches on an 11 m platform.")]
        public float maximumImpactSpeed = 28f;
        [Tooltip("How far the mesh is compressed at the hardest impact.")]
        [Range(0f, 0.6f)] public float maximumSquash = 0.34f;
        [Tooltip("Spring stiffness of the squash. Higher snaps back faster.")]
        [Min(1f)] public float squashStiffness = 210f;
        [Tooltip("Spring damping. Below critical damping leaves a visible rebound overshoot.")]
        [Min(0f)] public float squashDamping = 12f;

        [Header("Rolling")]
        [Range(0f, 1f)] public float rollVolume = 0.35f;
        [Min(0f)] public float rollMinSpeed = 1.5f;
        [Min(0.1f)] public float rollMaxSpeed = 30f;

        private SnowballBody ball;
        private SnowballMotor motor;
        private AudioSource rollSource;
        private Vector3 squashAxis = Vector3.up;
        private float squash;
        private float squashVelocity;

        private void Awake() => Initialize();

        public void Initialize()
        {
            ball = GetComponent<SnowballBody>();
            motor = GetComponent<SnowballMotor>();
            if (rollSource == null) BuildRollSource();
        }

        private void BuildRollSource()
        {
            rollSource = gameObject.AddComponent<AudioSource>();
            rollSource.playOnAwake = false;
            rollSource.loop = true;
            rollSource.spatialBlend = 1f;
            rollSource.rolloffMode = AudioRolloffMode.Linear;
            rollSource.minDistance = 9f;
            rollSource.maxDistance = 38f;
            rollSource.dopplerLevel = 0f;
            rollSource.volume = 0f;
            AttachRollClip();
        }

        private void AttachRollClip()
        {
            var clips = ArenaFeedback.Instance != null ? ArenaFeedback.Instance.Clips : null;
            if (clips == null || clips.rollLoop == null) return;
            rollSource.clip = clips.rollLoop;
            if (!Application.isBatchMode) rollSource.Play();
        }

        private void OnCollisionEnter(Collision collision)
        {
            float impact = collision.relativeVelocity.magnitude;
            if (impact < minimumImpactSpeed) return;
            var other = collision.collider.GetComponentInParent<SnowballBody>();
            bool ballToBall = other != null && other != ball;
            // Both balls receive the callback for the same contact; only one reports it.
            if (ballToBall && other.gameObject.GetInstanceID() < gameObject.GetInstanceID()) return;
            // Square root keeps medium-speed hits meaningful; a linear curve pushed the
            // big-hit rewards past speeds a 11 m arena never reaches.
            float strength = Mathf.Sqrt(Mathf.InverseLerp(minimumImpactSpeed, maximumImpactSpeed, impact));
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.Impact(point, strength, !ballToBall);
            if (ballToBall) Squash(point, strength);
        }

        private void Squash(Vector3 contactPoint, float strength)
        {
            Vector3 axis = transform.position - contactPoint;
            if (axis.sqrMagnitude < 0.0001f) return;
            squashAxis = axis.normalized;
            // Depth follows impact strength: a light nudge barely deforms the snowball and
            // maximumSquash is reserved for a full-speed hit.
            squash = Mathf.Max(squash, maximumSquash * Mathf.Pow(Mathf.Clamp01(strength), 1.5f));
            squashVelocity = Mathf.Min(squashVelocity, 0f);
        }

        private void LateUpdate()
        {
            if (ball == null || ball.visual == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            // Under-damped spring: compress, then overshoot slightly as it springs back, so
            // the hit reads as a bounce rather than a static dent.
            squashVelocity += (-squashStiffness * squash - squashDamping * squashVelocity) * dt;
            squash += squashVelocity * dt;
            if (Mathf.Abs(squash) < 0.0005f && Mathf.Abs(squashVelocity) < 0.03f)
            {
                squash = 0f;
                squashVelocity = 0f;
                return;
            }
            float size = ball.Radius * 2f;
            Vector3 d = squashAxis;
            float alongAxis = 1f - squash;        // squeezed along the contact axis
            float sideways = 1f + squash * 0.5f;  // bulges out, roughly keeping volume
            ball.visual.localScale = new Vector3(
                size * (alongAxis * d.x * d.x + sideways * (1f - d.x * d.x)),
                size * (alongAxis * d.y * d.y + sideways * (1f - d.y * d.y)),
                size * (alongAxis * d.z * d.z + sideways * (1f - d.z * d.z)));
        }

        private void Update()
        {
            if (rollSource == null) return;
            // The feedback singleton may not exist yet when a scene-placed ball initialises,
            // so keep trying until the loop clip is attached.
            if (rollSource.clip == null) AttachRollClip();
            float speed = ball == null || ball.RigidBody == null
                ? 0f
                : Vector3.ProjectOnPlane(ball.RigidBody.linearVelocity, Vector3.up).magnitude;
            bool rolling = motor != null && motor.HasSupport && speed > rollMinSpeed;
            float t = Mathf.InverseLerp(rollMinSpeed, rollMaxSpeed, speed);
            float target = rolling ? t * rollVolume : 0f;
            rollSource.volume = Mathf.MoveTowards(rollSource.volume, target, Time.deltaTime * 2.5f);
            // A bigger ball rolls with a lower voice, which also sells the growth.
            float size = ball != null && ball.settings != null
                ? Mathf.Sqrt(Mathf.Max(0.5f, ball.Radius / Mathf.Max(0.01f, ball.settings.initialRadius)))
                : 1f;
            rollSource.pitch = Mathf.Clamp(Mathf.Lerp(0.85f, 1.55f, t) / size, 0.5f, 2f);
        }
    }
}
