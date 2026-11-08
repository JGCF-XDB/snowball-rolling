using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnowballArena.Editor
{
    public static class MatchSceneBuilder
    {
        public const string MatchPath = PrototypeBuilder.Root + "/Scenes/Match.unity";
        public static void TuneAndBuild()
        {
            var contact = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PrototypeBuilder.Root + "/Materials/SnowContact.physicMaterial");
            contact.bounciness = 0.65f;
            EditorUtility.SetDirty(contact); AssetDatabase.SaveAssets();
            PrototypeValidation.Run();
            GrowthCollisionValidation.Run();
            BuildWindowsGame();
        }
        public static void ValidateArenaAndBuild()
        {
            ArenaExtensionValidation.Run();
            BuildWindowsGame();
        }
        public static void BuildGame()
        {
            if (!File.Exists(MatchPath)) CreateMatch();
            PrototypeValidation.Run();
            GrowthCollisionValidation.Run();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MatchPath, true), new EditorBuildSettingsScene(PrototypeBuilder.ScenePath, true) };
            BuildWindowsGame();
        }

        [MenuItem("Snowball Arena/Build Windows Game")]
        public static void BuildWindowsGame()
        {
            UnityEditor.PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { MatchPath, PrototypeBuilder.ScenePath }, locationPathName = "Builds/Windows/SnowballArena.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new System.Exception("Game build failed");
        }

        private static void CreateMatch()
        {
            var scene = EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            EditorSceneManager.SaveScene(scene, MatchPath);
            var practice = Object.FindFirstObjectByType<PrototypeSession>();
            Object.DestroyImmediate(practice.player.gameObject);
            foreach (var target in practice.practiceTargets) Object.DestroyImmediate(target.gameObject);
            Object.DestroyImmediate(practice.gameObject);
            var match = new GameObject("Match Controller").AddComponent<MatchController>();
            match.snowballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath);
            match.surface = Object.FindFirstObjectByType<ArenaSurface>();
            match.arenaCamera = Camera.main;
            CreateEnvironment();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        private static void CreateEnvironment()
        {
            var root = new GameObject("Snow Mountain Environment");
            var stone = Material("MountainStone", new Color(0.12f, 0.28f, 0.36f));
            var ice = Material("MountainSnow", new Color(0.51f, 0.72f, 0.80f));
            var tree = Material("Pine", new Color(0.045f, 0.22f, 0.25f));
            var cone = ConeMesh();
            AssetDatabase.CreateAsset(cone, PrototypeBuilder.Root + "/Art/EnvironmentCone.asset");
            for (int i = 0; i < 14; i++)
            {
                float angle = i * Mathf.PI * 2 / 14;
                Vector3 position = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (18.5f + (i % 3));
                position.y = -11;
                float height = 7 + (i * 7 % 6);
                Piece(root.transform, "Mountain", cone, stone, position, new Vector3(5.4f, height, 5.4f));
                Piece(root.transform, "Snow peak", cone, ice, position + Vector3.up * height * 0.56f, new Vector3(2.4f, height * 0.45f, 2.4f));
            }
            for (int i = 0; i < 30; i++)
            {
                float angle = i * Mathf.PI * 2 / 30;
                Vector3 position = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (14.1f + (i % 4) * 0.4f);
                position.y = -4.8f - (i % 3) * 0.5f;
                Piece(root.transform, "Pine island", cone, stone, position + Vector3.down * 2, new Vector3(1.6f, 2.4f, 1.6f));
                Piece(root.transform, "Pine lower", cone, tree, position, new Vector3(0.85f, 2.4f, 0.85f));
                Piece(root.transform, "Pine snow", cone, ice, position + Vector3.up * 0.85f, new Vector3(0.66f, 1.7f, 0.66f));
            }
            var rimObject = new GameObject("Ice rim"); rimObject.transform.SetParent(root.transform);
            var rim = rimObject.AddComponent<LineRenderer>();
            rim.useWorldSpace = true; rim.loop = true; rim.positionCount = 96; rim.widthMultiplier = 0.055f;
            rim.sharedMaterial = Material("IceRim", new Color(0.48f, 0.88f, 0.94f));
            for (int i = 0; i < 96; i++)
            {
                float a = i * Mathf.PI * 2 / 96;
                rim.SetPosition(i, new Vector3(Mathf.Cos(a) * 10.98f, 0.025f, Mathf.Sin(a) * 10.98f));
            }
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 28; RenderSettings.fogEndDistance = 70;
            RenderSettings.fogColor = new Color(0.045f, 0.13f, 0.19f);
            Camera.main.backgroundColor = RenderSettings.fogColor;
        }

        private static Material Material(string name, Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", 0.12f);
            AssetDatabase.CreateAsset(material, PrototypeBuilder.Root + "/Materials/" + name + ".mat");
            return material;
        }
        private static void Piece(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var item = new GameObject(name) { layer = 2 }; item.transform.SetParent(parent); item.transform.position = position; item.transform.localScale = scale;
            item.AddComponent<MeshFilter>().sharedMesh = mesh; item.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        private static Mesh ConeMesh()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < 7; i++)
            {
                float a = i * Mathf.PI * 2 / 7, b = (i + 1) * Mathf.PI * 2 / 7;
                int start = vertices.Count;
                vertices.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))); vertices.Add(Vector3.up);
                vertices.Add(new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)));
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }
            var mesh = new Mesh { name = "Low poly cone" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
