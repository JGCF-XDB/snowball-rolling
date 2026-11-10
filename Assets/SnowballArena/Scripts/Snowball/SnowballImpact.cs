using UnityEngine;

namespace SnowballArena
{
    // Ball-to-ball contact is still solved by the rigidbody solver. What this removes is
    // the one artifact high-speed snowball impacts keep producing: horizontal momentum
    // turning into upward motion (deep penetration at 50 m/s, a contact normal tilted by
    // a height difference, or a ball wedged against the rim). A snowball hitting another
    // snowball should shove it across the platform, not throw it over the edge.
    // Only upward speed that the contact itself created is clipped; horizontal velocity,
    // and therefore horizontal momentum, is never touched.
    [RequireComponent(typeof(SnowballBody))]
    [DefaultExecutionOrder(-20)]
    public sealed class SnowballImpact : MonoBehaviour
    {
        private SnowballBody ball;
        private Vector3 stepStartVelocity;
        private bool primed;

        private void Awake() => ball = GetComponent<SnowballBody>();

        private void FixedUpdate()
        {
            if (ball == null) ball = GetComponent<SnowballBody>();
            var body = ball != null ? ball.RigidBody : null;
            // Runs before the physics step, so contact callbacks later in the same step
            // can compare against the velocity the solver started from.
            if (body == null) { primed = false; return; }
            stepStartVelocity = body.linearVelocity;
            primed = true;
        }

        private void OnCollisionEnter(Collision collision) => SuppressLaunch(collision, true);
        private void OnCollisionStay(Collision collision) => SuppressLaunch(collision, false);

        private void SuppressLaunch(Collision collision, bool entering)
        {
            if (!primed || ball == null) return;
            var body = ball.RigidBody;
            if (body == null || body.isKinematic) return;
            // Ground contacts keep their own behaviour; only snowball-on-snowball impacts
            // are corrected, so rolling up a slope still works normally.
            var other = collision.collider.GetComponentInParent<SnowballBody>();
            if (other == null || other == ball) return;
            var settings = ball.settings;
            if (settings == null) return;
            // A resting contact has no approach speed; only a real impact is worth judging.
            if (entering && collision.relativeVelocity.magnitude < settings.impactSpeedThreshold) return;
            Vector3 velocity = body.linearVelocity;
            if (!TryClamp(velocity.y, stepStartVelocity.y, settings, entering, out float clamped)) return;
            velocity.y = clamped;
            body.linearVelocity = velocity;
        }

        // Clips upward speed that a snowball contact added. The allowance only applies to
        // the first frame of a contact and the clip always falls back to the speed the step
        // started from, so a ball crushed between two others cannot ratchet upwards one
        // allowance per physics step.
        public static bool TryClamp(float upward, float stepStartUpward, SnowballSettings settings, bool entering, out float clamped)
        {
            float baseline = Mathf.Max(stepStartUpward, 0f);
            float limit = entering ? baseline + Mathf.Max(0f, settings.impactVerticalAllowance) : baseline;
            clamped = Mathf.Min(upward, baseline);
            return upward > limit;
        }
    }
}
