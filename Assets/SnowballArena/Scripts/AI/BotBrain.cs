using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnowballArena
{
    [RequireComponent(typeof(SnowballMotor))]
    public sealed class BotBrain : MonoBehaviour
    {
        public MatchController match;
        public MatchParticipant self;
        public float personality;
        private float nextDecision, retargetAt, escapeUntil, stuckSince = -1;
        private Vector3 escapeDirection;
        private MatchParticipant target;
        private bool wasRoaming;

        private void Update()
        {
            if (match == null || self == null || !self.alive || match.Phase != MatchPhase.Running || Time.time < nextDecision) return;
            nextDecision = Time.time + 0.2f + personality % 0.08f;
            var ball = self.body; var body = ball.RigidBody;
            Vector3 velocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            // A target is kept for several seconds; changing targets every frame creates deadlocks.
            if (target == null || !target.alive || Time.time >= retargetAt)
            {
                retargetAt = Time.time + 3.1f;
                float best = float.PositiveInfinity; target = null;
                for (int i = 0; i < match.Participants.Count; i++)
                {
                    var candidate = match.Participants[i];
                    if (candidate == self || !candidate.alive) continue;
                    float distance = (candidate.body.RigidBody.position - body.position).sqrMagnitude;
                    float variation = 0.45f + Mathf.Abs(Mathf.Sin(personality * 1.7f + i * 2.2f + Mathf.Floor(Time.time / 5f)));
                    float score = distance * variation * Mathf.Clamp(candidate.body.RigidBody.mass / body.mass, 0.6f, 1.5f);
                    if (score < best) { best = score; target = candidate; }
                }
            }
            bool closeAttack = target != null && Vector3.Distance(target.body.RigidBody.position, body.position) < ball.Radius + target.body.Radius + 2f;
            // Collision attempts carry some risk. Far from an opponent, brake near the edge.
            float anticipation = closeAttack ? 0.22f : Mathf.Clamp(0.3f + velocity.magnitude * body.mass / (ball.settings.driveForce * 6f), 0.35f, 1.1f);
            if (!Supported(body.position + velocity * anticipation))
            {
                Vector3 safe = match.surface.AimPoint; safe.y = body.position.y;
                Vector3 direction = (safe - body.position).normalized - velocity.normalized * 0.65f;
                if (!Supported(body.position + direction.normalized * 2f)) direction = OpenDirection();
                self.motor.SetMoveDirection(direction); return;
            }
            if (velocity.magnitude < 0.3f)
            {
                if (stuckSince < 0) stuckSince = Time.time;
                if (Time.time - stuckSince > 1.2f)
                {
                    escapeDirection = OpenDirection(); escapeUntil = Time.time + 3.5f; stuckSince = -1;
                }
            }
            else stuckSince = -1;
            bool roaming = Mathf.Repeat(Time.time + personality * 2.7f, 9f) < 2.5f;
            if (roaming && !wasRoaming) { escapeDirection = OpenDirection(); escapeUntil = Time.time + 2.5f; }
            wasRoaming = roaming;
            if (Time.time < escapeUntil)
            {
                if (!Supported(body.position + escapeDirection * (ball.Radius + 1))) escapeDirection = OpenDirection();
                self.motor.SetMoveDirection(escapeDirection); return;
            }
            if (target == null) { self.motor.SetMoveDirection(OpenDirection()); return; }
            Vector3 aim = target.body.RigidBody.position + target.body.RigidBody.linearVelocity * 0.15f;
            Vector3 attack = Vector3.ProjectOnPlane(aim - body.position, Vector3.up).normalized;
            attack = Quaternion.AngleAxis(Mathf.Sin(Time.time * 0.45f + personality) * 9f, Vector3.up) * attack;
            self.motor.SetMoveDirection(attack);
        }

        private Vector3 OpenDirection()
        {
            var ball = self.body;
            Vector3 bestDirection = (match.surface.AimPoint - ball.RigidBody.position).normalized;
            float best = float.NegativeInfinity;
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8 + personality;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 point = ball.RigidBody.position + direction * (ball.Radius + 2f);
                if (!Supported(point)) continue;
                float clearance = 1000;
                foreach (var other in match.Participants)
                    if (other != self && other.alive) clearance = Mathf.Min(clearance, Vector3.Distance(point, other.body.RigidBody.position) - other.body.Radius);
                if (clearance > best) { best = clearance; bestDirection = direction; }
            }
            return bestDirection;
        }

        private bool Supported(Vector3 position)
        {
            var ball = self.body;
            return ArenaQuery.TrySupport(gameObject.scene.GetPhysicsScene(), position, ball.Radius,
                self.motor.supportLayers, ball.settings.supportProbeGap + 0.05f, ball.settings.minimumSupportNormal, out _);
        }
    }
}
