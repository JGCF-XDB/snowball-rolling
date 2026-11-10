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
        // Closing speed (m/s) along the contact normal below which neither ball is treated as
        // the attacker. Kept small so a slow shove still counts, which is the usual kill.
        private const float KnockAttributionSpeed = 0.35f;

        private SnowballBody ball;
        private Vector3 stepStartVelocity;
        private bool primed;

        public Vector3 StepStartVelocity => primed ? stepStartVelocity : Vector3.zero;

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
            RecordKnockAttribution(collision, other);
            Vector3 velocity = body.linearVelocity;
            if (!TryClamp(velocity.y, stepStartVelocity.y, settings, entering, out float clamped)) return;
            velocity.y = clamped;
            body.linearVelocity = velocity;
        }

        // Attribution runs on the pre-step velocities projected onto the contact normal (which
        // points from the other ball towards this one). Only the ball actually closing along that
        // axis is recorded, so a ball glancing past, or one already falling, is never named as the
        // attacker, and a slow sustained shove still is.
        private void RecordKnockAttribution(Collision collision, SnowballBody other)
        {
            if (collision.contactCount == 0) return;
            var otherBody = other.RigidBody;
            if (otherBody == null) return;
            Vector3 normal = collision.GetContact(0).normal;
            Vector3 otherStart = other.TryGetComponent(out SnowballImpact otherImpact)
                ? otherImpact.StepStartVelocity
                : otherBody.linearVelocity;
            float closing = Vector3.Dot(stepStartVelocity - otherStart, normal);
            // Negative closing means this ball is the one driving into the other.
            if (closing < -KnockAttributionSpeed) other.RecordKnock(ball);
            else if (closing > KnockAttributionSpeed) ball.RecordKnock(other);
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
