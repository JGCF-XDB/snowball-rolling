using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace SnowballArena.Editor
{
    public static class PrototypeBuilder
    {
        public const string Root = "Assets/SnowballArena";
        public const string ScenePath = Root + "/Scenes/Arena.unity";
        public const string PrefabPath = Root + "/Prefabs/Snowball.prefab";

        // Explicit menu/batch operation only: importing scripts never replaces an open scene.
        [MenuItem("Snowball Arena/Create Prototype Scene")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Arena already exists. Open it instead; generation never overwrites authored scenes.");
            foreach (string folder in new[] { "Scenes", "Prefabs", "Configs", "Materials", "Art", "Tests" })
                Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh();
            ConfigureProject();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.58f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.49f, 0.62f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.22f, 0.32f);
            RenderSettings.fog = false;
            var snow = MakeMaterial("Snow", new Color(0.36f, 0.62f, 0.74f));
            var ice = MakeMaterial("IceEdge", new Color(0.25f, 0.54f, 0.68f));
            var rock = MakeMaterial("Rock", new Color(0.085f, 0.20f, 0.29f));
            var ballWhite = MakeMaterial("Snowball", new Color(0.97f, 0.99f, 1f));
            var ballBlue = MakeMaterial("SnowballIce", new Color(0.34f, 0.76f, 0.85f));
            var surfacePhysics = new PhysicsMaterial("Snow Contact")
            {
                dynamicFriction = 0.45f, staticFriction = 0.5f, bounciness = 0.18f,
                frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Average
            };
            AssetDatabase.CreateAsset(surfacePhysics, Root + "/Materials/SnowContact.physicMaterial");
            var settings = ScriptableObject.CreateInstance<SnowballSettings>();
            AssetDatabase.CreateAsset(settings, Root + "/Configs/DefaultSnowball.asset");
            var arena = ScriptableObject.CreateInstance<ArenaDefinition>();
            AssetDatabase.CreateAsset(arena, Root + "/Configs/CircularArena.asset");

            var platform = new GameObject("Arena Surface");
            platform.layer = 8;
            var mesh = MakePlatform(arena.previewRadius, arena.thickness, 64);
            AssetDatabase.CreateAsset(mesh, Root + "/Art/CircularPlatform.asset");
            platform.AddComponent<MeshFilter>().sharedMesh = mesh;
            platform.AddComponent<MeshRenderer>().sharedMaterials = new[] { snow, ice, rock };
            var collider = platform.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.sharedMaterial = surfacePhysics;
            platform.AddComponent<ArenaSurface>().definition = arena;
            PrefabUtility.SaveAsPrefabAsset(platform, Root + "/Prefabs/CircularArena.prefab");

            var controls = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = controls.AddActionMap("Gameplay");
            var move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            var controlsPath = Root + "/Configs/SnowballControls.inputactions";
            File.WriteAllText(controlsPath, controls.ToJson());
            UnityEngine.Object.DestroyImmediate(controls);
            AssetDatabase.ImportAsset(controlsPath);
            controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(controlsPath);

            var player = new GameObject("Player Snowball");
            player.layer = 9;
            player.transform.position = arena.spawnPosition;
            player.AddComponent<SphereCollider>().sharedMaterial = surfacePhysics;
            player.AddComponent<Rigidbody>();
            var body = player.AddComponent<SnowballBody>();
            body.settings = settings;
            var visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform, false);
            var ballMesh = MakeBall();
            AssetDatabase.CreateAsset(ballMesh, Root + "/Art/FacetedSnowball.asset");
            visual.AddComponent<MeshFilter>().sharedMesh = ballMesh;
            visual.AddComponent<MeshRenderer>().sharedMaterials = new[] { ballWhite, ballBlue };
            body.visual = visual.transform;
            body.Initialize();
            player.AddComponent<SnowballMotor>();
            var input = player.AddComponent<PlayerInputSource>();
            input.controls = controls;
            PrefabUtility.SaveAsPrefabAsset(player, PrefabPath);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 23, -20);
            cameraObject.transform.LookAt(new Vector3(0, -0.6f, 0));
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 12.5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 150f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.085f, 0.13f);
            camera.allowHDR = true;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<ArenaCamera>();
            input.cameraFrame = cameraObject.transform;

            var lightObject = new GameObject("Sun");
            lightObject.transform.rotation = Quaternion.Euler(48, -35, 0);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.95f, 0.88f);
            light.shadows = LightShadows.Soft;
            light.shadowBias = 0.02f;
            light.shadowNormalBias = 0.2f;
            var session = new GameObject("Practice Session").AddComponent<PrototypeSession>();
            session.player = body;
            session.arena = arena;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("SNOWBALL: Prototype scene generated successfully.");
        }

        private static void ConfigureProject()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            foreach (var entry in new[] { (8, "Arena"), (9, "Snowball") })
            {
                var slot = layers.GetArrayElementAtIndex(entry.Item1);
                if (!string.IsNullOrEmpty(slot.stringValue) && slot.stringValue != entry.Item2)
                    throw new InvalidOperationException("Required layer slot is occupied: " + entry.Item1);
                slot.stringValue = entry.Item2;
            }
            tags.ApplyModifiedProperties();
            Time.fixedDeltaTime = 1f / 60f;
            Time.maximumDeltaTime = 0.1f;
            var timeSettings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            timeSettings.FindProperty("Fixed Timestep").floatValue = 1f / 60f;
            timeSettings.FindProperty("Maximum Allowed Timestep").floatValue = 0.1f;
            timeSettings.ApplyModifiedProperties();
            PlayerSettings.companyName = "Snowball Arena";
            PlayerSettings.productName = "Snowball Arena Prototype";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = false;
        }

        private static Material MakeMaterial(string name, Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.18f);
            AssetDatabase.CreateAsset(material, Root + "/Materials/" + name + ".mat");
            return material;
        }

        public static Mesh MakePlatform(float radius, float depth, int segments)
        {
            var vertices = new List<Vector3>();
            var indices = new[] { new List<int>(), new List<int>(), new List<int>() };
            for (int i = 0; i < segments; i++)
            {
                float a = 2 * Mathf.PI * i / segments, b = 2 * Mathf.PI * (i + 1) / segments;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                Vector3 q = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * radius;
                Vector3 p1 = p + Vector3.down * 0.35f, q1 = q + Vector3.down * 0.35f;
                Vector3 p2 = p * (0.88f + 0.035f * Mathf.Sin(i * 2.1f)) + Vector3.down * depth;
                Vector3 q2 = q * (0.88f + 0.035f * Mathf.Sin((i + 1) % segments * 2.1f)) + Vector3.down * depth;
                AddTriangle(vertices, indices[0], Vector3.zero, q, p);
                AddTriangle(vertices, indices[1], p, q, q1); AddTriangle(vertices, indices[1], p, q1, p1);
                AddTriangle(vertices, indices[2], p1, q1, q2); AddTriangle(vertices, indices[2], p1, q2, p2);
                AddTriangle(vertices, indices[2], Vector3.down * depth, p2, q2);
            }
            return MeshFrom(vertices, indices, "Circular Platform");
        }

        private static Mesh MakeBall()
        {
            var vertices = new List<Vector3>();
            var indices = new[] { new List<int>(), new List<int>() };
            const int longitude = 20, latitude = 12;
            Vector3 Point(int row, int col)
            {
                float phi = Mathf.PI * row / latitude, theta = 2f * Mathf.PI * col / longitude;
                return new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)) * 0.5f;
            }
            for (int row = 0; row < latitude; row++)
                for (int col = 0; col < longitude; col++)
                {
                    var list = indices[(col >= 3 && col <= 5 && row >= 3 && row <= 8) ? 1 : 0];
                    if (row > 0) AddTriangle(vertices, list, Point(row, col), Point(row, col + 1), Point(row + 1, col));
                    if (row < latitude - 1) AddTriangle(vertices, list, Point(row, col + 1), Point(row + 1, col + 1), Point(row + 1, col));
                }
            return MeshFrom(vertices, indices, "Faceted Snowball");
        }

        private static void AddTriangle(List<Vector3> vertices, List<int> indices, Vector3 a, Vector3 b, Vector3 c)
        {
            indices.Add(vertices.Count); vertices.Add(a);
            indices.Add(vertices.Count); vertices.Add(b);
            indices.Add(vertices.Count); vertices.Add(c);
        }

        private static Mesh MeshFrom(List<Vector3> vertices, List<int>[] indices, string name)
        {
            var mesh = new Mesh { name = name, subMeshCount = indices.Length };
            mesh.SetVertices(vertices);
            for (int i = 0; i < indices.Length; i++) mesh.SetTriangles(indices[i], i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        public static void BuildAndValidate()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            PrototypeValidation.Run();
            RenderPreview();
        }

        public static void AdjustPrototypeContrast()
        {
            ConfigureProject();
            EditorSceneManager.OpenScene(ScenePath);
            var snow = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Snow.mat");
            snow.SetColor("_BaseColor", new Color(0.36f, 0.62f, 0.74f));
            EditorUtility.SetDirty(snow);
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.58f, 0.70f);
            GameObject.Find("Sun").GetComponent<Light>().intensity = 1.15f;
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            RenderPreview();
            BuildWindows();
        }

        [MenuItem("Snowball Arena/Build Windows Prototype")]
        public static void BuildWindows()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = "Builds/Windows/SnowballArena.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Windows build failed: " + report.summary.result);
            Debug.Log("SNOWBALL: Windows build succeeded.");
        }

        public static void RenderPreview()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var camera = Camera.main;
            var texture = new RenderTexture(1280, 720, 24);
            var previous = RenderTexture.active;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            Directory.CreateDirectory("TestResults");
            File.WriteAllBytes("TestResults/ArenaPreview.png", image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
