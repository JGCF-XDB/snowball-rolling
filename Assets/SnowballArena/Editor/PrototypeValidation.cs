using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace SnowballArena.Editor
{
    public static class PrototypeValidation
    {
        [Serializable] private class Result { public string name; public bool passed; public string details; }
        [Serializable] private class Report { public string unity; public int passed; public int failed; public List<Result> cases = new List<Result>(); }

        private sealed class Fixture : IDisposable
        {
            public Scene Scene;
            public SnowballBody Ball;
            public SnowballMotor Motor;
            public Fixture(bool square = false, float platformSize = 22f)
            {
                Scene = EditorSceneManager.NewPreviewScene();
                Require(Scene.GetPhysicsScene() != Physics.defaultPhysicsScene, "Validation must use an isolated physics scene");
                var platform = new GameObject("Test platform") { layer = 8 };
                SceneManager.MoveGameObjectToScene(platform, Scene);
                if (square)
                {
                    var box = platform.AddComponent<BoxCollider>();
                    box.size = new Vector3(platformSize, 2, platformSize); box.center = Vector3.down;
                    box.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PrototypeBuilder.Root + "/Materials/SnowContact.physicMaterial");
                }
                else
                {
                    var mesh = platform.AddComponent<MeshCollider>();
                    mesh.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(PrototypeBuilder.Root + "/Art/CircularPlatform.asset");
                    mesh.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PrototypeBuilder.Root + "/Materials/SnowContact.physicMaterial");
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath);
                var player = UnityEngine.Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(player, Scene);
                Ball = player.GetComponent<SnowballBody>();
                Ball.Initialize();
                Ball.RigidBody.interpolation = RigidbodyInterpolation.None;
                Ball.RigidBody.position = new Vector3(0, Ball.Radius + 0.02f, 0);
                Motor = player.GetComponent<SnowballMotor>();
                Physics.SyncTransforms();
            }
            public void Step(int count, Vector3 direction)
            {
                Motor.SetMoveDirection(direction);
                for (int i = 0; i < count; i++)
                {
                    Motor.SimulateDrive();
                    Scene.GetPhysicsScene().Simulate(1f / 60f);
                }
            }
            public void Dispose() => EditorSceneManager.ClosePreviewScene(Scene);
        }

        [MenuItem("Snowball Arena/Validate Prototype Physics")]
        public static void Run()
        {
            var report = new Report { unity = Application.unityVersion };
            void Check(string name, Action test)
            {
                var result = new Result { name = name };
                try { test(); result.passed = true; result.details = "Passed"; report.passed++; }
                catch (Exception ex) { result.details = ex.Message; report.failed++; }
                report.cases.Add(result);
                Debug.Log("SNOWBALL TEST: " + name + " : " + result.details);
            }

            Check("Round platform supports stationary sphere", () =>
            {
                using var f = new Fixture(); f.Step(180, Vector3.zero);
                Require(Mathf.Abs(f.Ball.RigidBody.position.y - f.Ball.Radius) < 0.05f, "Sphere sank or bounced off ground");
                Require(f.Motor.HasSupport, "Ground probe failed");
            });
            Check("Applied force accelerates and physically rolls sphere", () =>
            {
                using var f = new Fixture(true, 200); f.Step(30, Vector3.zero); f.Step(60, Vector3.right);
                Require(f.Ball.RigidBody.position.x > 0.5f, "Drive did not move sphere");
                Require(f.Ball.RigidBody.angularVelocity.magnitude > 0.1f, "Sphere slid without rolling");
            });
            Check("Release retains inertia then dissipates motion", () =>
            {
                // High-speed runs need room to remain grounded during the coast-down.
                // Gameplay edge behavior is validated separately on the original arena.
                using var f = new Fixture(true, 200); f.Step(30, Vector3.zero); f.Step(60, Vector3.right);
                float speed = f.Ball.RigidBody.linearVelocity.x;
                f.Step(1, Vector3.zero);
                Require(f.Ball.RigidBody.linearVelocity.x > speed * 0.8f, "Release instantly killed momentum");
                f.Step(90, Vector3.zero);
                Require(f.Ball.RigidBody.linearVelocity.x < speed * 0.8f, "Ground resistance did not slow the ball");
            });
            Check("Reverse input brakes before reversing", () =>
            {
                using var f = new Fixture(true, 200); f.Step(30, Vector3.zero); f.Step(60, Vector3.right);
                float initial = f.Ball.RigidBody.linearVelocity.x;
                f.Step(1, Vector3.left);
                Require(f.Ball.RigidBody.linearVelocity.x > 0 && f.Ball.RigidBody.linearVelocity.x < initial, "Velocity snapped on reverse");
                f.Step(120, Vector3.left);
                Require(f.Ball.RigidBody.linearVelocity.x < -0.1f, "Reverse force failed");
            });
            Check("Airborne ball ignores steering and falls under gravity", () =>
            {
                using var f = new Fixture();
                f.Ball.RigidBody.position = new Vector3(20, 4, 0);
                f.Ball.RigidBody.linearVelocity = new Vector3(2, 0, 0);
                Physics.SyncTransforms(); f.Step(60, Vector3.left);
                Require(!f.Motor.HasSupport, "False support outside platform");
                Require(Mathf.Abs(f.Ball.RigidBody.linearVelocity.x - 2) < 0.02f, "Input altered airborne horizontal momentum");
                Require(f.Ball.RigidBody.linearVelocity.y < -9f, "Gravity did not act");
            });
            Check("Sphere rolls off real circular edge without invisible wall", () =>
            {
                using var f = new Fixture(); f.Step(360, Vector3.right);
                Require(f.Ball.RigidBody.position.x > 11, "Sphere was blocked at edge");
                Require(f.Ball.RigidBody.position.y < -7, "Sphere did not fall past reset height");
            });
            Check("Non-circular platform supports same motor", () =>
            {
                using var f = new Fixture(true); f.Step(30, Vector3.zero); f.Step(45, Vector3.forward);
                Require(f.Motor.HasSupport && f.Ball.RigidBody.position.z > 0.2f, "Motor depends on circular geometry");
            });
            Check("Reset restores radius, mass and zero momentum", () =>
            {
                using var f = new Fixture(); f.Step(90, Vector3.right);
                f.Ball.ResetAt(new Vector3(0, f.Ball.Radius + 0.15f, -2));
                Require(f.Ball.RigidBody.linearVelocity.sqrMagnitude < 0.0001f && f.Ball.RigidBody.angularVelocity.sqrMagnitude < 0.0001f, "Reset retained momentum");
                Require(Mathf.Abs(f.Ball.RigidBody.mass - f.Ball.settings.InitialMass) < 0.001f, "Reset mass mismatch");
                f.Step(60, Vector3.zero); Require(f.Motor.HasSupport, "Reset sphere not supported");
            });
            Check("Screen-relative input has no diagonal speed advantage", () =>
            {
                var frame = new GameObject("Test camera frame");
                try
                {
                    frame.transform.rotation = Quaternion.Euler(48, 35, 0);
                    var diagonal = PlayerInputSource.ToWorldDirection(Vector2.one, frame.transform);
                    var forward = PlayerInputSource.ToWorldDirection(Vector2.up, frame.transform);
                    Require(Mathf.Abs(diagonal.magnitude - 1) < 0.001f, "Diagonal input not normalized");
                    Require(Mathf.Abs(diagonal.y) < 0.0001f, "Movement includes vertical force");
                    Require(Vector3.Dot(forward, Vector3.ProjectOnPlane(frame.transform.forward, Vector3.up).normalized) > 0.999f, "Forward does not match camera");
                }
                finally { UnityEngine.Object.DestroyImmediate(frame); }
            });
            Directory.CreateDirectory("TestResults");
            File.WriteAllText("TestResults/physics-validation.json", JsonUtility.ToJson(report, true));
            if (report.failed > 0) throw new Exception(report.failed + " prototype validation checks failed.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
