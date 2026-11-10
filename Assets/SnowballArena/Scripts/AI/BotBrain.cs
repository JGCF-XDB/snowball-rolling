using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnowballArena
{
    [RequireComponent(typeof(SnowballMotor))]
    [DefaultExecutionOrder(-20)]
    public sealed class BotBrain : MonoBehaviour
    {
        public MatchController match;
        public MatchParticipant self;
        public float personality;
        [Tooltip("Weight given to an aligned, nearby opponent close to falling off. This is a rule score, not ML training.")]
        [Min(0f)] public float finishOpportunityWeight = 5f;
        [Header("Impact judgement")]
        [Tooltip("How much heavier an opponent must be before backing off and charging instead of trading hits.")]
        [Min(1f)] public float chargeMassRatio = 1.3f;
        [Tooltip("Speed below which a charge against a heavier opponent is worth preparing, in m/s.")]
        [Min(0f)] public float chargeTriggerSpeed = 12f;
        [Tooltip("Seconds spent pulling away before the next ram.")]
        [Min(0f)] public float chargeSeconds = 0.9f;
        [Tooltip("How much heavier an incoming ball must be to count as a threat.")]
        [Min(1f)] public float threatMassRatio = 1.2f;
        [Tooltip("Closing speed at which a heavier ball counts as dangerous, in m/s.")]
        [Min(0f)] public float threatApproachSpeed = 10f;
        [Tooltip("Closing speed that is dangerous regardless of mass, in m/s.")]
        [Min(0f)] public float evadeApproachSpeed = 25f;
        [Min(0f)] public float threatRadius = 8f;
        [Tooltip("Seconds spent stepping aside from a heavier charge.")]
        [Min(0f)] public float evadeSeconds = 0.6f;
        [Header("Crowd escape")]
        [Tooltip("Extra distance beyond the two radii that counts as crowding, in metres.")]
        [Min(0f)] public float crowdGap = 1.5f;
        [Tooltip("How many touching neighbours make a bot try to break free.")]
        [Min(1)] public int crowdMinimum = 2;
        [Tooltip("Bots slower than this inside a crowd try to break free, in m/s.")]
        [Min(0f)] public float crowdBreakoutSpeed = 3f;
        [Tooltip("Seconds spent leaving a crowd.")]
        [Min(0f)] public float crowdBreakoutSeconds = 0.45f;
        private enum BotIntent { Attack, Charge, Evade, Breakout }
        private BotIntent intent;
        private Vector3 intentDirection;
        private float intentUntil, nextChargeAt, nextEvadeAt, nextBreakoutAt, nextThreatScan;
        private MatchParticipant threat;
        private float nextDecision, retargetAt, blockedTime, manoeuvreUntil;
        private Vector3 manoeuvreDirection, attackOutward;
        private float targetEdgeDistance;
        private MatchParticipant target;
        private MatchParticipant pressureTarget;
        private Vector3 pressurePosition;
        private float pressureProgressAt;
        private PhysicsScene physics;

        private void FixedUpdate()
        {
            if (match == null || self == null || !self.alive || match.Phase != MatchPhase.Running) return;
            if (Time.fixedTime < nextDecision) return;
            nextDecision = Time.fixedTime + 0.08f;
            physics = gameObject.scene.GetPhysicsScene();
            var ball = self.body;
            var body = ball.RigidBody;
            Vector3 position = body.position;
            Vector3 velocity = Flat(body.linearVelocity - match.surface.VelocityAt(position));
            float speed = velocity.magnitude;
            // Rolling inertia reduces acceleration. Ignore helpful ground drag here so that
            // the braking estimate stays conservative as a growing snowball becomes heavy.
            float braking = Mathf.Max(0.01f, ball.settings.DriveForceForMass(body.mass) / (1.4f * body.mass) * 0.9f);
            float stoppingDistance = speed * speed / (2f * braking) + speed * 0.16f;
            if (speed > 0.25f && Clearance(velocity / speed, stoppingDistance + 0.6f) < stoppingDistance + 0.3f)
            {
                self.motor.SetMoveDirection(-velocity.normalized);
                return;
            }

            // A heavier ball already closing fast cannot be beaten by standing ground.
            // Step aside and let it spend its speed instead of feeding it a head-on hit.
            if (Time.fixedTime >= nextThreatScan)
            {
                nextThreatScan = Time.fixedTime + 0.2f;
                threat = FindThreat(velocity);
            }
            if (intent == BotIntent.Evade)
            {
                if (Time.fixedTime < intentUntil && ShouldEvade(threat, velocity, out _, out _))
                {
                    DriveSafely(intentDirection, braking, velocity, SpeedCap(ball));
                    return;
                }
                intent = BotIntent.Attack;
            }
            if (threat != null && Time.fixedTime >= nextEvadeAt && ShouldEvade(threat, velocity, out _, out _))
            {
                intent = BotIntent.Evade;
                intentUntil = Time.fixedTime + evadeSeconds;
                nextEvadeAt = Time.fixedTime + evadeSeconds + 1.5f;
                intentDirection = EvadeDirection(threat, velocity);
                DriveSafely(intentDirection, braking, velocity, SpeedCap(ball));
                return;
            }

            // Several slow balls pressed together cannot push each other anywhere. Break
            // the tie by leaving the pile instead of adding another body to it.
            if (intent == BotIntent.Breakout)
            {
                if (Time.fixedTime < intentUntil && Clearance(intentDirection, 2f) > 1f)
                {
                    DriveSafely(intentDirection, braking, velocity, SpeedCap(ball));
                    return;
                }
                intent = BotIntent.Attack;
            }
            if (intent == BotIntent.Attack && Time.fixedTime >= nextBreakoutAt && speed < crowdBreakoutSpeed
                && TryCrowdDirection(position, out Vector3 escape))
            {
                intent = BotIntent.Breakout;
                intentUntil = Time.fixedTime + crowdBreakoutSeconds;
                nextBreakoutAt = Time.fixedTime + crowdBreakoutSeconds + 2f;
                intentDirection = escape;
                DriveSafely(escape, braking, velocity, SpeedCap(ball));
                return;
            }

            bool validTarget = ValidTarget(target);
            bool pressingAdvantage = false;
            if (validTarget)
            {
                attackOutward = NearestEdge(target.body.RigidBody.position, out targetEdgeDistance);
                pressingAdvantage = CanFinish(target, attackOutward, targetEdgeDistance);
            }
            if (!validTarget || !pressingAdvantage && Time.fixedTime >= retargetAt)
            {
                retargetAt = Time.fixedTime + 2f;
                float best = float.PositiveInfinity;
                MatchParticipant choice = null;
                foreach (var candidate in match.Participants)
                {
                    if (!ValidTarget(candidate)) continue;
                    float distance = Flat(candidate.body.RigidBody.position - position).magnitude;
                    float massRatio = candidate.body.RigidBody.mass / body.mass;
                    Vector3 outward = NearestEdge(candidate.body.RigidBody.position, out float edgeDistance);
                    // Focus on opponents that can be pushed out, especially lighter balls
                    // close to an edge, instead of repeatedly duelling in the middle.
                    float score = distance * Mathf.Clamp(massRatio * massRatio, 0.5f, 2f)
                        * (0.4f + Mathf.Min(edgeDistance, 9f) * 0.15f);
                    float alignment = Mathf.Clamp01(Vector3.Dot(Flat(candidate.body.RigidBody.position - position).normalized, outward));
                    float gap = Mathf.Max(0f, distance - ball.Radius - candidate.body.Radius);
                    float opportunity = Mathf.Clamp01(1f - edgeDistance / 4f) * alignment * Mathf.Clamp01(1f - gap / 5f);
                    score /= 1f + finishOpportunityWeight * opportunity;
                    // Different opponents remain attractive to different bots; otherwise
                    // the whole field settles into identical nearest-neighbour pushing pairs.
                    score *= 0.85f + 0.3f * Mathf.Abs(Mathf.Sin(personality + match.Participants.IndexOf(candidate) * 2.3f));
                    if (candidate == target) score *= 0.75f;
                    if (score < best) { best = score; choice = candidate; }
                }
                target = choice;
                if (target != null)
                {
                    attackOutward = NearestEdge(target.body.RigidBody.position, out targetEdgeDistance);
                    pressingAdvantage = CanFinish(target, attackOutward, targetEdgeDistance);
                }
            }

            if (target == null) { DriveVelocity(Vector3.zero, velocity); return; }
            Vector3 offset = Flat(target.body.RigidBody.position - position);
            float contactDistance = ball.Radius + target.body.Radius;
            float targetMassRatio = target.body.RigidBody.mass / Mathf.Max(0.001f, body.mass);

            // A shove comes from momentum, so a standing start cannot move a much heavier
            // ball. Pull back first, then charge with real speed behind the hit.
            if (!pressingAdvantage && Time.fixedTime >= nextChargeAt && targetMassRatio >= chargeMassRatio
                && speed < chargeTriggerSpeed && offset.magnitude < contactDistance + 8f)
            {
                Vector3 retreat = ChooseHeading(-offset.normalized);
                if (Clearance(retreat, 3f) > 2f)
                {
                    intent = BotIntent.Charge;
                    intentUntil = Time.fixedTime + chargeSeconds;
                    nextChargeAt = Time.fixedTime + chargeSeconds + 3f;
                    intentDirection = retreat;
                }
            }
            if (intent == BotIntent.Charge)
            {
                // Stop backing off when the retreat runs out of platform; the normal
                // attack flow takes over with whatever speed was built up.
                if (Time.fixedTime < intentUntil && Clearance(intentDirection, 2f) > 1f)
                {
                    DriveSafely(intentDirection, braking, velocity, SpeedCap(ball));
                    return;
                }
                intent = BotIntent.Attack;
            }
            // Lead a moving opponent, but use its real position when an intercept would
            // lead off the surface. Never steer after an already falling opponent.
            float leadTime = Mathf.Clamp((offset.magnitude - contactDistance) / 8f, 0f, 0.55f);
            Vector3 aim = target.body.RigidBody.position + Flat(target.body.RigidBody.linearVelocity) * leadTime;
            if (!SafePoint(aim)) aim = target.body.RigidBody.position;
            Vector3 attack = Flat(aim - position).normalized;

            // Make room for another charge after a pushing stalemate. A grown ball
            // needs several seconds of real acceleration to change its approach angle.
            bool blocked = offset.magnitude < contactDistance + 0.8f && speed < 1.2f;
            // Slow movement is not a stalemate when we are pinning an opponent at
            // the edge. Commit until the opportunity disappears or self-safety requires braking.
            if (pressingAdvantage) { manoeuvreUntil = 0; blockedTime = 0; }
            blockedTime = blocked && !pressingAdvantage ? blockedTime + 0.08f : 0f;
            if (blockedTime > 1.2f && Time.fixedTime > manoeuvreUntil)
            {
                float side = Mathf.Sin(personality) >= 0 ? 1f : -1f;
                manoeuvreDirection = ChooseHeading(Quaternion.AngleAxis(side * 140f, Vector3.up) * attack);
                manoeuvreUntil = Time.fixedTime + 2.5f + Mathf.Abs(Mathf.Sin(personality));
                blockedTime = 0f;
            }
            Vector3 wanted = Time.fixedTime < manoeuvreUntil ? manoeuvreDirection : attack;
            if (!pressingAdvantage && Time.fixedTime >= manoeuvreUntil && targetEdgeDistance < 8f && Vector3.Dot(attack, attackOutward) < 0.45f)
            {
                Vector3 approach = target.body.RigidBody.position - attackOutward * (contactDistance + 1.2f);
                if (SafePoint(approach))
                {
                    wanted = Flat(approach - position).normalized;
                    // Go around the target when approaching from the wrong side; driving
                    // straight through it would only push it back towards safety.
                    float along = Vector3.Dot(offset, wanted);
                    float miss = (offset - wanted * Mathf.Max(0f, along)).magnitude;
                    if (along > 0 && miss < contactDistance + 0.4f)
                    {
                        Vector3 tangent = Vector3.Cross(Vector3.up, offset.normalized);
                        if (Vector3.Dot(tangent, wanted) < -0.05f ||
                            Mathf.Abs(Vector3.Dot(tangent, wanted)) <= 0.05f && Mathf.Sin(personality) < 0) tangent = -tangent;
                        wanted = (tangent - offset.normalized * 0.25f).normalized;
                    }
                }
            }
            // Do not divert merely because the space beyond the opponent is short.
            // The attacker still checks its own stopping distance and ground clearance.
            Vector3 pressureDirection = attack;
            if (pressingAdvantage)
            {
                if (pressureTarget != target || Vector3.Dot(Flat(target.body.RigidBody.position - pressurePosition), attackOutward) > 0.08f)
                {
                    pressureTarget = target; pressurePosition = target.body.RigidBody.position; pressureProgressAt = Time.fixedTime;
                }
                float stalled = Time.fixedTime - pressureProgressAt;
                if (stalled > 1f)
                {
                    // Two grounded spheres can lock through contact friction. Keep forward
                    // pressure while changing the contact angle slightly; never turn away.
                    float angle = Mathf.Sin((stalled - 1f) * 5f) * 28f;
                    Vector3 angled = Quaternion.AngleAxis(angle, Vector3.up) * attack;
                    if (Clearance(angled, 1f) > 0.6f) pressureDirection = angled;
                }
            }
            else pressureTarget = null;
            Vector3 heading = pressingAdvantage && Clearance(pressureDirection, 1f) > 0.6f ? pressureDirection : ChooseHeading(wanted);
            float room = Clearance(heading, 12f);
            float safeSpeed = Mathf.Sqrt(2f * braking * Mathf.Max(0f, room - 0.35f));
            // Keep driving through contact to push the opponent. Only terrain safety,
            // not distance to the opponent, reduces the commanded approach speed.
            DriveVelocity(heading * Mathf.Min(SpeedCap(ball), safeSpeed), velocity);
        }

        private static float SpeedCap(SnowballBody ball)
        {
            // Bots used to stop at a hard 18.9 m/s while the player could cruise far faster.
            // Derive the cap from the current drive tier instead of a stale constant.
            var settings = ball.settings;
            return Mathf.Max(5f, settings.driveForce / Mathf.Max(1f, settings.groundDrag) * 0.85f);
        }

        private void DriveSafely(Vector3 direction, float braking, Vector3 velocity, float maxSpeed)
        {
            float room = Clearance(direction, 12f);
            float safeSpeed = Mathf.Sqrt(2f * braking * Mathf.Max(0f, room - 0.35f));
            DriveVelocity(direction * Mathf.Min(maxSpeed, safeSpeed), velocity);
        }

        private MatchParticipant FindThreat(Vector3 velocity)
        {
            MatchParticipant worst = null;
            float worstScore = 0f;
            foreach (var candidate in match.Participants)
            {
                if (candidate == self || !ShouldEvade(candidate, velocity, out _, out float score)) continue;
                if (score > worstScore) { worstScore = score; worst = candidate; }
            }
            return worst;
        }

        private bool ShouldEvade(MatchParticipant candidate, Vector3 velocity, out Vector3 incoming, out float score)
        {
            incoming = Vector3.zero; score = 0f;
            if (candidate == null || !ValidTarget(candidate)) return false;
            Vector3 offset = Flat(candidate.body.RigidBody.position - self.body.RigidBody.position);
            float distance = offset.magnitude;
            float reach = threatRadius + self.body.Radius + candidate.body.Radius;
            if (distance < 0.0001f || distance > reach) return false;
            float massRatio = candidate.body.RigidBody.mass / Mathf.Max(0.001f, self.body.RigidBody.mass);
            incoming = Flat(candidate.body.RigidBody.linearVelocity
                - match.surface.VelocityAt(candidate.body.RigidBody.position)) - velocity;
            // Positive approach means the other ball is closing on us right now.
            float approach = -Vector3.Dot(incoming, offset / distance);
            if (approach < threatApproachSpeed) return false;
            // Heavy balls are dangerous once they close in; anything closing fast enough is
            // dangerous regardless of mass.
            if (massRatio < threatMassRatio && approach < evadeApproachSpeed) return false;
            score = approach * (0.5f + massRatio) / Mathf.Max(1f, distance);
            return true;
        }

        private bool TryCrowdDirection(Vector3 position, out Vector3 direction)
        {
            direction = Vector3.zero;
            Vector3 push = Vector3.zero;
            int neighbours = 0;
            foreach (var candidate in match.Participants)
            {
                if (candidate == self || !ValidTarget(candidate)) continue;
                Vector3 offset = Flat(candidate.body.RigidBody.position - position);
                float reach = self.body.Radius + candidate.body.Radius + crowdGap;
                if (offset.sqrMagnitude > reach * reach || offset.sqrMagnitude < 0.0001f) continue;
                // Closer neighbours weigh more, so the escape route points out of the pile.
                push -= offset / Mathf.Max(0.35f, offset.sqrMagnitude);
                neighbours++;
            }
            if (neighbours < crowdMinimum || push.sqrMagnitude < 0.01f) return false;
            direction = ChooseHeading(push.normalized);
            return Clearance(direction, 3f) > 1.5f;
        }

        private Vector3 EvadeDirection(MatchParticipant candidate, Vector3 velocity)
        {
            Vector3 incoming = Flat(candidate.body.RigidBody.linearVelocity
                - match.surface.VelocityAt(candidate.body.RigidBody.position)) - velocity;
            if (incoming.sqrMagnitude < 0.01f)
                incoming = Flat(self.body.RigidBody.position - candidate.body.RigidBody.position);
            Vector3 side = Vector3.Cross(Vector3.up, incoming.normalized);
            // Pick the side with more platform left, biased back towards the middle.
            if (Clearance(side, 6f) < Clearance(-side, 6f)) side = -side;
            Vector3 inward = -NearestEdge(self.body.RigidBody.position, out _);
            return (side * 0.8f + inward * 0.2f).normalized;
        }

        private void DriveVelocity(Vector3 desired, Vector3 velocity)
        {
            var ball = self.body;
            Vector3 force = (desired - velocity) * (ball.RigidBody.mass * 1.4f / 0.4f)
                + velocity * ball.settings.GroundDragForMass(ball.RigidBody.mass);
            self.motor.SetMoveDirection(force / Mathf.Max(0.01f, ball.settings.DriveForceForMass(ball.RigidBody.mass)));
        }

        private bool CanFinish(MatchParticipant opponent, Vector3 outward, float edgeDistance)
        {
            Vector3 offset = Flat(opponent.body.RigidBody.position - self.body.RigidBody.position);
            // A successful impact can briefly separate the balls. Keep following up
            // instead of treating that rebound as the end of a finishing opportunity.
            return edgeDistance <= 3.5f && offset.magnitude <= self.body.Radius + opponent.body.Radius + 3f
                && Vector3.Dot(offset.normalized, outward) > 0.55f;
        }

        private Vector3 NearestEdge(Vector3 point, out float distance)
        {
            point.y = self.body.RigidBody.position.y;
            distance = 20f;
            Vector3 outward = Vector3.right;
            for (int i = 0; i < 16; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(i * 22.5f, Vector3.up) * Vector3.right;
                for (float d = 0.5f; d < distance; d += 0.75f)
                {
                    if (GroundAt(point + direction * d)) continue;
                    distance = d; outward = direction; break;
                }
            }
            return outward;
        }

        private bool ValidTarget(MatchParticipant candidate)
        {
            if (candidate == null || candidate == self || !candidate.alive) return false;
            var ball = candidate.body;
            return ArenaQuery.TrySupport(physics, ball.RigidBody.position, ball.Radius,
                self.motor.supportLayers, ball.settings.supportProbeGap + 0.08f,
                ball.settings.minimumSupportNormal, out _);
        }

        private Vector3 ChooseHeading(Vector3 wanted)
        {
            if (Clearance(wanted, 3f) >= 2.8f) return wanted;
            Vector3 bestDirection = wanted;
            float best = float.NegativeInfinity;
            for (int i = 0; i < 16; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(i * 22.5f, Vector3.up) * wanted;
                float room = Clearance(direction, 4f);
                float score = Vector3.Dot(direction, wanted) * 2f + Mathf.Min(room, 3f);
                if (room < 0.8f) score -= 8f;
                if (score > best) { best = score; bestDirection = direction; }
            }
            return bestDirection;
        }

        private float Clearance(Vector3 direction, float distance)
        {
            if (direction.sqrMagnitude < 0.01f) return 0;
            // Sample the whole route to detect holes, not just the end point.
            // No circular arena radius or cannon aim point is assumed.
            float limit = Mathf.Min(distance, 24f);
            bool enteredSafeArea = SafePoint(self.body.RigidBody.position);
            for (float d = 0.25f; d <= limit; d += 0.35f)
            {
                Vector3 point = self.body.RigidBody.position + direction * d;
                if (SafePoint(point)) { enteredSafeArea = true; continue; }
                // A collision can push us inside the safety margin while still on the
                // platform. Permit an inward recovery, never a route crossing a hole.
                if (!enteredSafeArea && d < self.body.Radius + 0.75f && GroundAt(point)) continue;
                return Mathf.Max(0f, d - 0.35f);
            }
            if (!enteredSafeArea) return 0f;
            return limit;
        }

        private bool SafePoint(Vector3 point)
        {
            float margin = Mathf.Clamp(self.body.Radius * 0.45f, 0.4f, 1.5f);
            return GroundAt(point) && GroundAt(point + Vector3.right * margin)
                && GroundAt(point - Vector3.right * margin) && GroundAt(point + Vector3.forward * margin)
                && GroundAt(point - Vector3.forward * margin);
        }

        private bool GroundAt(Vector3 point)
        {
            return physics.Raycast(point + Vector3.up, Vector3.down, out var hit,
                self.body.Radius + 1.5f, self.motor.supportLayers, QueryTriggerInteraction.Ignore)
                && Vector3.Dot(hit.normal, Vector3.up) >= self.body.settings.minimumSupportNormal;
        }

        private static Vector3 Flat(Vector3 vector) => Vector3.ProjectOnPlane(vector, Vector3.up);
    }
}
