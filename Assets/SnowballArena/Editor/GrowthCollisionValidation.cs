using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnowballArena.Editor
{
    public static class GrowthCollisionValidation
    {
        [Serializable] private class Result { public string name; public bool passed; public string details; }
        [Serializable] private class Report { public int passed; public int failed; public List<Result> cases = new List<Result>(); }

        private sealed class World : IDisposable
        {
            public readonly Scene scene = EditorSceneManager.NewPreviewScene();
            private readonly List<UnityEngine.Object> transientAssets = new List<UnityEngine.Object>();
            public SnowballBody Ball(Vector3 position, float radius = 0.65f, bool gravity = false)
            {
                var gameObject = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath));
                SceneManager.MoveGameObjectToScene(gameObject, scene);
                var ball = gameObject.GetComponent<SnowballBody>();
                var settings = UnityEngine.Object.Instantiate(ball.settings);
                settings.initialRadius = radius;
                transientAssets.Add(settings);
                ball.settings = settings;
                ball.Initialize();
                ball.RigidBody.position = position;
                ball.RigidBody.interpolation = RigidbodyInterpolation.None;
                ball.RigidBody.useGravity = gravity;
                ball.RigidBody.angularDamping = 0f;
                var material = new PhysicsMaterial("Isolated collision")
                {
                    dynamicFriction = 0, staticFriction = 0, bounciness = 0.6f,
                    bounceCombine = PhysicsMaterialCombine.Average, frictionCombine = PhysicsMaterialCombine.Minimum
                };
                transientAssets.Add(material);
                gameObject.GetComponent<SphereCollider>().sharedMaterial = material;
                Physics.SyncTransforms();
                return ball;
            }
            public void Platform()
            {
                var ground = new GameObject("Ground") { layer = 8 };
                SceneManager.MoveGameObjectToScene(ground, scene);
                var collider = ground.AddComponent<BoxCollider>();
                // Keep the long high-speed rolling test away from fixture edges.
                // Falling off the gameplay platform has its own validation case.
                collider.size = new Vector3(1000, 2, 1000); collider.center = Vector3.down;
                var material = new PhysicsMaterial("Rolling contact") { staticFriction = 0.5f, dynamicFriction = 0.5f };
                transientAssets.Add(material); collider.sharedMaterial = material;
                Physics.SyncTransforms();
            }
            public void Simulate(float dt) => scene.GetPhysicsScene().Simulate(dt);
            public void Dispose()
            {
                EditorSceneManager.ClosePreviewScene(scene);
                foreach (var asset in transientAssets) UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [MenuItem("Snowball Arena/Validate Growth and Collisions")]
        public static void Run()
        {
            var report = new Report();
            void Check(string name, Func<string> test)
            {
                var result = new Result { name = name };
                try { result.details = test(); result.passed = true; report.passed++; }
                catch (Exception ex) { result.details = ex.Message; report.failed++; }
                report.cases.Add(result);
                Debug.Log("SNOWBALL PHYSICS: " + name + " : " + result.details);
            }
            Check("Equal masses exchange impulse", () => Collision(0.65f, 0f, false));
            Check("Unequal masses conserve momentum", () => Collision(1f, 0f, false));
            Check("Oblique collision conserves vector momentum", () => Collision(0.85f, 0.7f, false));
            Check("Fast sphere collision is detected", () => Collision(0.65f, 0f, true));
            Check("Elastic gameplay contact transfers speed between equal masses", () => Collision(0.65f, 0f, true, true));
            Check("Fast attacker rebounds from heavier opponent with conserved momentum", () => Collision(1f, 0f, true, true));
            Check("Lighter opponent receives greater speed without extra energy", () => Collision(0.45f, 0f, true, true));
            Check("Gameplay ground settles a dropped ball without repeated bouncing", () =>
            {
                using var world = new World(); world.Platform();
                foreach (var root in world.scene.GetRootGameObjects())
                    if (root.TryGetComponent<BoxCollider>(out var floor))
                        floor.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(ElasticCollisionBuilder.GroundMaterialPath);
                var ball = world.Ball(new Vector3(0, 3, 0), gravity: true);
                ball.GetComponent<SphereCollider>().sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(ElasticCollisionBuilder.BallMaterialPath);
                float lateHeight = 0;
                for (int i = 0; i < 240; i++)
                {
                    world.Simulate(1f / 120);
                    if (i >= 120) lateHeight = Mathf.Max(lateHeight, ball.RigidBody.position.y - ball.Radius);
                }
                Require(lateHeight < 0.1f && Mathf.Abs(ball.RigidBody.linearVelocity.y) < 0.1f, "Ground inherited high ball-to-ball restitution");
                return $"Height above ground after first second <= {lateHeight:F4} m";
            });
            Check("Accretion conserves incoming linear and angular momentum", () =>
            {
                using var world = new World(); var ball = world.Ball(Vector3.up * 5);
                ball.RigidBody.linearVelocity = new Vector3(4, 0, 2);
                ball.RigidBody.angularVelocity = new Vector3(1, 2, 3);
                Vector3 incoming = new Vector3(0.5f, 0, 0);
                float mass = ball.RigidBody.mass;
                var momentum = mass * ball.RigidBody.linearVelocity;
                var angular = ball.RigidBody.inertiaTensor.x * ball.RigidBody.angularVelocity;
                ball.Accrete(0.4d, incoming, false);
                var expected = momentum + (ball.RigidBody.mass - mass) * incoming;
                Require((ball.RigidBody.mass * ball.RigidBody.linearVelocity - expected).magnitude < 0.001f, "Accretion invented linear momentum");
                Require((ball.RigidBody.inertiaTensor.x * ball.RigidBody.angularVelocity - angular).magnitude < 0.001f, "Accretion invented angular momentum");
                return "Linear and angular momentum errors < 0.001";
            });
            Check("Distance sampling is independent of frame partition", () =>
            {
                double at30 = FeedDistance(30), at60 = FeedDistance(60), at120 = FeedDistance(120);
                Require(Math.Abs(at30 - at60) < 0.0001 && Math.Abs(at120 - at60) < 0.0001, "Frame partition changed growth");
                return $"Volume for 10 m: 30 Hz={at30:F6}, 60 Hz={at60:F6}, 120 Hz={at120:F6}";
            });
            Check("Idle, airborne, sliding and stationary spin do not grow", () =>
            {
                using var world = new World(); world.Platform(); var ball = world.Ball(new Vector3(0, 0.65f, 0));
                var growth = ball.GetComponent<SnowballGrowth>(); double before = ball.Volume;
                growth.SampleStep(1f / 60);
                for (int i = 0; i < 30; i++) growth.SampleStep(1f / 60);
                ball.RigidBody.angularVelocity = Vector3.right * 5;
                for (int i = 0; i < 30; i++) growth.SampleStep(1f / 60);
                ball.RigidBody.angularVelocity = Vector3.zero;
                ball.RigidBody.position += Vector3.right;
                Physics.SyncTransforms(); growth.SampleStep(1f / 60);
                ball.RigidBody.position = new Vector3(0, 5, 0); ball.RigidBody.angularVelocity = Vector3.right * 5;
                Physics.SyncTransforms(); growth.SampleStep(1f / 60);
                Require(Math.Abs(ball.Volume - before) < 0.000001, "Non-rolling state grew");
                return "All four non-rolling states preserve volume";
            });
            Check("Real driven rolling grows without ground penetration", () =>
            {
                using var world = new World(); world.Platform(); var ball = world.Ball(new Vector3(0, 0.67f, 0), gravity: true);
                var motor = ball.GetComponent<SnowballMotor>(); var growth = ball.GetComponent<SnowballGrowth>();
                // Both sides need nonzero friction for actual rolling.
                ball.GetComponent<SphereCollider>().sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PrototypeBuilder.Root + "/Materials/SnowContact.physicMaterial");
                motor.SetMoveDirection(Vector3.right);
                for (int i = 0; i < 480; i++) { growth.SampleStep(1f / 60); motor.SimulateDrive(); world.Simulate(1f / 60); }
                Require(ball.Radius > 0.8f && growth.RolledDistance > 3, "Real rolling did not grow");
                Require(Mathf.Abs(ball.RigidBody.position.y - ball.Radius) < 0.1f, $"Growth left ground: position={ball.RigidBody.position}, radius={ball.Radius:F3}");
                return $"Radius={ball.Radius:F3}, rolled={growth.RolledDistance:F3} m, ground error={ball.RigidBody.position.y - ball.Radius:F4}";
            });
            Check("Larger mass responds less to the same force", () =>
            {
                using var world = new World();
                var small = world.Ball(Vector3.left * 5); var large = world.Ball(Vector3.right * 5, 1.3f);
                small.RigidBody.AddForce(Vector3.forward * 100); large.RigidBody.AddForce(Vector3.forward * 100);
                world.Simulate(1f / 60);
                float ratio = small.RigidBody.linearVelocity.z / large.RigidBody.linearVelocity.z;
                Require(Mathf.Abs(ratio - 8f) < 0.01f, "Acceleration not inverse to mass");
                return $"Velocity-change ratio={ratio:F4} for 8:1 mass ratio";
            });
            Check("Control remains responsive as mass grows, with lighter balls more agile", () =>
            {
                float previousSpeed = float.PositiveInfinity, previousTurn = float.PositiveInfinity;
                var details = new List<string>();
                foreach (float ratio in new[] { 1f, 4f, 16f })
                {
                    var current = MeasureControl(ratio, false);
                    var old = MeasureControl(ratio, true);
                    Require(current.speed > old.speed * 1.2f && current.turn > old.turn * 1.2f, "Control did not improve over previous fixed-force motor");
                    Require(current.speed < previousSpeed && current.turn < previousTurn, "Heavier balls became more agile than lighter balls");
                    Require(current.speed > 1.5f && current.turn > 0.8f, "Heavy ball loses practical control");
                    details.Add($"{ratio:F0}x mass: 1s speed {old.speed:F2}->{current.speed:F2} m/s; 0.6s sideways response {old.turn:F2}->{current.turn:F2} m/s");
                    previousSpeed = current.speed; previousTurn = current.turn;
                }
                return string.Join("; ", details);
            });
            Check("Grown mass bonus strengthens impact with conserved momentum and no collision energy gain", () =>
            {
                var baseline = MeasureGrownImpact(1f);
                var boosted = MeasureGrownImpact(1.1f);
                Require(boosted.mass > baseline.mass * 1.14f && boosted.mass < baseline.mass * 1.16f, "Unexpected four-volume mass bonus");
                Require(boosted.targetSpeed > baseline.targetSpeed && boosted.attackerSpeed > baseline.attackerSpeed,
                    "Heavy attacker did not transfer more speed and retain more forward velocity");
                return $"Same size and 10 m/s approach: mass {baseline.mass:F2}->{boosted.mass:F2} kg, target {baseline.targetSpeed:F2}->{boosted.targetSpeed:F2} m/s, attacker {baseline.attackerSpeed:F2}->{boosted.attackerSpeed:F2} m/s";
            });
            Check("Growth has no gameplay size cap and reset clears history", () =>
            {
                using var world = new World(); var ball = world.Ball(Vector3.zero);
                ball.Accrete(10000, Vector3.zero, false); float first = ball.Radius;
                ball.Accrete(10000, Vector3.zero, false);
                Require(ball.Radius > first && first > 10, "Unexpected size cap");
                ball.ResetAt(new Vector3(0, 0.8f, 0));
                Require(Mathf.Abs(ball.Radius - 0.65f) < 0.001 && ball.GetComponent<SnowballGrowth>().RolledDistance == 0, "Reset retained growth");
                return "Radius grows beyond platform radius; reset restores initial state";
            });
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/growth-collision-validation.json", JsonUtility.ToJson(report, true));
            if (report.failed > 0) throw new Exception(report.failed + " growth/collision checks failed");
        }

        private static string Collision(float otherRadius, float offset, bool fast, bool gameplay = false)
        {
            using var world = new World();
            var a = world.Ball(new Vector3(-3, 5, 0)); var b = world.Ball(new Vector3(0, 5, offset), otherRadius);
            float restitution = 0.6f;
            if (gameplay)
            {
                var contact = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(ElasticCollisionBuilder.BallMaterialPath);
                a.GetComponent<SphereCollider>().sharedMaterial = contact;
                b.GetComponent<SphereCollider>().sharedMaterial = contact;
                restitution = contact.bounciness;
                Require(Mathf.Abs(restitution - 0.95f) < 0.001f, "Wrong gameplay restitution");
            }
            var initialVelocity = Vector3.right * (fast ? 80 : 6);
            a.RigidBody.linearVelocity = initialVelocity;
            var initialMomentum = a.RigidBody.mass * initialVelocity;
            var firstVolume = a.Volume; var secondVolume = b.Volume;
            for (int i = 0; i < (fast ? 30 : 120); i++) world.Simulate(1f / 120);
            var finalMomentum = a.RigidBody.mass * a.RigidBody.linearVelocity + b.RigidBody.mass * b.RigidBody.linearVelocity;
            float relativeError = (finalMomentum - initialMomentum).magnitude / initialMomentum.magnitude;
            Require(b.RigidBody.linearVelocity.magnitude > 0.1f, "Bodies never collided");
            Require(relativeError < 0.001f, "Relative momentum error exceeded 0.1%: " + relativeError);
            Require(a.Volume == firstVolume && b.Volume == secondVolume, "Collision reduced or altered volume");
            if (offset == 0)
            {
                float expected = ((a.RigidBody.mass - restitution * b.RigidBody.mass) / (a.RigidBody.mass + b.RigidBody.mass)) * initialVelocity.x;
                Require(Mathf.Abs(a.RigidBody.linearVelocity.x - expected) < 0.08f, "Head-on result differs from impulse solution");
            }
            if (gameplay)
            {
                float before = a.RigidBody.mass * initialVelocity.sqrMagnitude;
                float after = a.RigidBody.mass * a.RigidBody.linearVelocity.sqrMagnitude + b.RigidBody.mass * b.RigidBody.linearVelocity.sqrMagnitude;
                Require(after <= before * 1.001f, "Collision created kinetic energy");
                if (otherRadius > 0.65f) Require(a.RigidBody.linearVelocity.x < -1f, "Fast attacker did not rebound from heavier target");
            }
            return $"Relative momentum error={relativeError:E3}, v1={a.RigidBody.linearVelocity}, v2={b.RigidBody.linearVelocity}";
        }

        private static (float speed, float turn) MeasureControl(float massRatio, bool legacy)
        {
            using var world = new World(); world.Platform();
            var ball = world.Ball(new Vector3(0, 0.85f, 0), 0.85f, true);
            if (legacy)
            {
                // These parameters reproduce the previous constant 520 N / 40 drag model.
                ball.settings.driveForce = 520; ball.settings.groundDrag = 40;
                ball.settings.minimumDriveAcceleration = 0; ball.settings.massControlExponent = 1;
                ball.settings.massSpeedExponent = 0;
                ball.settings.massGrowthExponent = 1f;
            }
            ball.GetComponent<SphereCollider>().sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PrototypeBuilder.Root + "/Materials/SnowContact.physicMaterial");
            ball.Accrete(ball.Volume * (Math.Pow(massRatio, 1d / ball.settings.massGrowthExponent) - 1d), Vector3.zero, true);
            Physics.SyncTransforms();
            var motor = ball.GetComponent<SnowballMotor>();
            for (int i = 0; i < 30; i++) { motor.SimulateDrive(); world.Simulate(1f / 60); }
            motor.SetMoveDirection(Vector3.right);
            for (int i = 0; i < 60; i++) { motor.SimulateDrive(); world.Simulate(1f / 60); }
            float speed = ball.RigidBody.linearVelocity.x;
            motor.SetMoveDirection(Vector3.forward);
            for (int i = 0; i < 36; i++) { motor.SimulateDrive(); world.Simulate(1f / 60); }
            return (speed, ball.RigidBody.linearVelocity.z);
        }

        private static (float mass, float targetSpeed, float attackerSpeed) MeasureGrownImpact(float exponent)
        {
            using var world = new World();
            var a = world.Ball(new Vector3(-4, 5, 0)); var b = world.Ball(new Vector3(0, 5, 0));
            a.settings.massGrowthExponent = exponent;
            a.Accrete(a.Volume * 3, Vector3.zero, false);
            var contact = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(ElasticCollisionBuilder.BallMaterialPath);
            a.GetComponent<SphereCollider>().sharedMaterial = contact; b.GetComponent<SphereCollider>().sharedMaterial = contact;
            a.RigidBody.linearVelocity = Vector3.right * 10;
            float mass = a.RigidBody.mass, initialMomentum = mass * 10, initialEnergy = mass * 100;
            for (int i = 0; i < 120; i++) world.Simulate(1f / 120);
            Vector3 momentum = mass * a.RigidBody.linearVelocity + b.RigidBody.mass * b.RigidBody.linearVelocity;
            float energy = mass * a.RigidBody.linearVelocity.sqrMagnitude + b.RigidBody.mass * b.RigidBody.linearVelocity.sqrMagnitude;
            Require((momentum - Vector3.right * initialMomentum).magnitude < initialMomentum * 0.001f, "Mass bonus broke collision momentum conservation");
            Require(energy <= initialEnergy * 1.001f, "Mass bonus collision created energy");
            return (mass, b.RigidBody.linearVelocity.x, a.RigidBody.linearVelocity.x);
        }

        private static double FeedDistance(int steps)
        {
            using var world = new World(); world.Platform(); var ball = world.Ball(new Vector3(0, 0.65f, 0));
            var growth = ball.GetComponent<SnowballGrowth>(); growth.SampleStep(1f / steps);
            float dx = 10f / steps;
            for (int i = 0; i < steps; i++)
            {
                ball.RigidBody.position += Vector3.right * dx;
                ball.RigidBody.angularVelocity = Vector3.back * (10f / ball.Radius);
                Physics.SyncTransforms(); growth.SampleStep(1f / steps);
            }
            Require(Math.Abs(growth.RolledDistance - 10d) < 0.0001, "Wrong measured rolling distance");
            return ball.Volume;
        }

        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
