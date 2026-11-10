using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SnowballArena.Editor
{
    public static class ModelingReferenceBuilder
    {
        private const string Root = PrototypeBuilder.Root;
        private const string Results = "TestResults/ModelingReference";

        [MenuItem("Snowball Arena/Apply Modeling Reference")]
        public static void Apply()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var snow = Material("ReferenceSnow", new Color(0.72f, 0.82f, 0.89f), 0.08f);
            var ice = Material("ReferenceGlacier", new Color(0.29f, 0.70f, 0.86f, 0.90f), 0.67f, true);
            var rock = Material("ReferenceRock", new Color(0.13f, 0.23f, 0.32f), 0.12f);
            var white = Material("ReferenceSnowball", new Color(0.89f, 0.94f, 0.98f), 0.15f);
            var packed = Material("ReferencePackedSnow", new Color(0.64f, 0.81f, 0.91f), 0.24f);
            var pine = Material("ReferenceCedar", new Color(0.055f, 0.24f, 0.27f), 0.08f);
            var bark = Material("ReferenceBark", new Color(0.19f, 0.26f, 0.29f), 0.04f);
            var tracks = Material("ReferenceSnowTracks", new Color(0.65f, 0.76f, 0.84f), 0.04f);
            var peakSnow = Material("ReferencePeakSnow", new Color(0.86f, 0.95f, 1f), 0.10f);
            var platformMaterials = new[] { snow, ice, rock };
            var platform = Mesh("ReferencePlatform", ReferenceMeshes.Platform(11, 3.2f, 64));
            var ball = Mesh("ReferenceSnowball", ReferenceMeshes.Snowball());
            var cedar = Mesh("SnowCedar", ReferenceMeshes.Cedar());
            var islet = Mesh("FloatingIslet", ReferenceMeshes.Platform(1, 1.8f, 14));
            var rollingTracks = Mesh("SnowRollingTracks", ReferenceMeshes.RollingTracks());
            var cedarPrefab = Module("SnowCedar", cedar, new[] { pine, snow, bark });
            var isletPrefab = Module("FloatingIslet", islet, platformMaterials);
            var mountainPrefabs = new GameObject[3];
            for (int i = 0; i < mountainPrefabs.Length; i++)
            {
                var stone = Material("ReferenceMountain" + i, Color.Lerp(new Color(0.10f, 0.31f, 0.41f), new Color(0.32f, 0.51f, 0.62f), i / 2f), 0.04f);
                mountainPrefabs[i] = Module("SnowMountain" + i, Mesh("SnowMountain" + i, ReferenceMeshes.Mountain(i)), new[] { stone, peakSnow });
            }
            UpdateBall(ball, white, packed);
            UpdateArenaPrefab(platform, platformMaterials);

            var scene = EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            var surface = Object.FindFirstObjectByType<ArenaSurface>();
            var collisionMesh = surface.GetComponent<MeshCollider>().sharedMesh;
            surface.GetComponent<MeshFilter>().sharedMesh = platform;
            surface.GetComponent<MeshRenderer>().sharedMaterials = platformMaterials;
            var previousEnvironment = GameObject.Find("Snow Mountain Environment");
            if (previousEnvironment != null) Object.DestroyImmediate(previousEnvironment);
            var environment = new GameObject("Snow Mountain Environment");
            Environment(environment.transform, cedarPrefab, isletPrefab, mountainPrefabs);
            var marks = new GameObject("Shallow rolling tracks") { layer = 2 };
            marks.transform.SetParent(surface.transform, false);
            // Reapplying the operation replaces, rather than accumulates, surface marks.
            for (int i = surface.transform.childCount - 1; i >= 0; i--)
            {
                var child = surface.transform.GetChild(i);
                if (child != marks.transform && child.name == marks.name) Object.DestroyImmediate(child.gameObject);
            }
            marks.AddComponent<MeshFilter>().sharedMesh = rollingTracks;
            var trackRenderer = marks.AddComponent<MeshRenderer>();
            trackRenderer.sharedMaterial = tracks; trackRenderer.shadowCastingMode = ShadowCastingMode.Off;
            LightingAndCamera();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);

            if (surface.GetComponent<MeshCollider>().sharedMesh != collisionMesh) throw new Exception("Art replacement changed the gameplay collider.");
            if (environment.GetComponentsInChildren<Collider>().Length != 0) throw new Exception("Decorative environment acquired gameplay colliders.");
            Physics.SyncTransforms();
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath).GetComponent<SnowballBody>();
            if (!SpawnService.TryGetPositions(surface, 16, body.settings.initialRadius, out _)) throw new Exception("New art scene cannot safely spawn sixteen players.");
            if (ball.triangles.Length / 3 > 3500) throw new Exception("Snowball exceeded its triangle budget.");
            foreach (var vertex in ball.vertices)
                if (!float.IsFinite(vertex.x) || !float.IsFinite(vertex.y) || !float.IsFinite(vertex.z) || vertex.magnitude > 0.53f)
                    throw new Exception("Snowball has invalid geometry or protrudes too far beyond its unit collision sphere.");
            Directory.CreateDirectory(Results);
            File.WriteAllText(Results + "/asset-validation.txt", "PASS Original gameplay collision mesh retained\nPASS Environment has no colliders\nPASS Sixteen safe initial spawns\nPASS Unit snowball geometry and triangle budget\n" +
                "Snowball triangles=" + ball.triangles.Length / 3 + "\nPlatform triangles=" + platform.triangles.Length / 3 + "\nCedar triangles=" + cedar.triangles.Length / 3);
            CaptureSamples();
            EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            Debug.Log("SNOWBALL ART: Reference models applied and previews captured.");
        }

        public static void ApplyAndBuild()
        {
            Apply();
            PrototypeValidation.Run();
            GrowthCollisionValidation.Run();
            ArenaExtensionValidation.Run();
            Directory.CreateDirectory("Builds/Windows");
            MatchSceneBuilder.BuildWindowsGame();
            Debug.Log("SNOWBALL ART: Playable Windows build completed.");
        }

        private static Material Material(string name, Color color, float smoothness, bool transparent = false)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new Exception("URP Lit shader is unavailable.");
                result = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(result, path);
            }
            result.SetColor("_BaseColor", color); result.SetFloat("_Smoothness", smoothness);
            result.SetFloat("_Metallic", 0); result.enableInstancing = true;
            if (transparent)
            {
                result.SetFloat("_Surface", 1); result.SetFloat("_Blend", 0);
                result.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                result.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                result.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                result.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                result.SetFloat("_ZWrite", 0); result.SetFloat("_AlphaToMask", 0);
                result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                result.SetOverrideTag("RenderType", "Transparent"); result.renderQueue = (int)RenderQueue.Transparent;
                result.SetShaderPassEnabled("ShadowCaster", false);
            }
            EditorUtility.SetDirty(result);
            return result;
        }

        private static Mesh Mesh(string name, Mesh generated)
        {
            string path = Root + "/Art/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            generated.name = name;
            if (existing == null) { AssetDatabase.CreateAsset(generated, path); return generated; }
            EditorUtility.CopySerialized(generated, existing); EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(generated);
            return existing;
        }

        private static GameObject Module(string name, Mesh mesh, Material[] materials)
        {
            var instance = new GameObject(name) { layer = 2, isStatic = true };
            try
            {
                instance.AddComponent<MeshFilter>().sharedMesh = mesh;
                instance.AddComponent<MeshRenderer>().sharedMaterials = materials;
                return PrefabUtility.SaveAsPrefabAsset(instance, Root + "/Prefabs/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        private static void UpdateBall(Mesh mesh, Material snow, Material packed)
        {
            var prefab = PrefabUtility.LoadPrefabContents(PrototypeBuilder.PrefabPath);
            try
            {
                var visual = prefab.GetComponent<SnowballBody>().visual;
                visual.GetComponent<MeshFilter>().sharedMesh = mesh;
                visual.GetComponent<MeshRenderer>().sharedMaterials = new[] { snow, packed };
                PrefabUtility.SaveAsPrefabAsset(prefab, PrototypeBuilder.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        private static void UpdateArenaPrefab(Mesh mesh, Material[] materials)
        {
            string path = Root + "/Prefabs/CircularArena.prefab";
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                prefab.GetComponent<MeshFilter>().sharedMesh = mesh;
                prefab.GetComponent<MeshRenderer>().sharedMaterials = materials;
                PrefabUtility.SaveAsPrefabAsset(prefab, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        private static GameObject Place(GameObject prefab, Transform parent, Vector3 position, Vector3 scale, float rotation)
        {
            var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            item.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0, rotation, 0));
            item.transform.localScale = scale;
            return item;
        }

        private static void Environment(Transform root, GameObject cedar, GameObject islet, GameObject[] mountains)
        {
            for (int row = 0; row < 3; row++)
                for (int i = -5; i <= 5; i++)
                {
                    float z = 23 + row * 13 + Mathf.Sin(i * 2.7f) * 3;
                    float x = i * (11.5f + row * 2f) + Mathf.Sin(i * 1.9f) * 2;
                    float h = 12 + (i * i + row * 7) % 8;
                    Place(mountains[row], root, new Vector3(x, -z * 0.86f - 9, z), new Vector3(9 + (i * i % 5), h, 9), i * 37 + row * 11);
                }
            for (int i = 0; i < 19; i++)
            {
                float angle = i * Mathf.PI * 2 / 19 + 0.12f;
                float distance = 15.7f + (i % 3) * 1.6f;
                Vector3 position = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                position.y = -3.5f - (i % 3) * 0.65f;
                float size = 1.6f + (i % 4) * 0.3f;
                var island = Place(islet, root, position, new Vector3(size, 1.3f + (i % 3) * 0.35f, size * 0.85f), i * 27);
                island.name = "Snow cedar island " + (i + 1);
                int treeCount = i % 3 == 0 ? 3 : 1;
                for (int tree = 0; tree < treeCount; tree++)
                {
                    Vector3 offset = treeCount == 1 ? Vector3.zero : new Vector3((tree - 1) * 0.85f, 0, tree == 1 ? 0.45f : -0.35f);
                    float treeSize = 0.65f + ((i + tree) % 4) * 0.14f;
                    Place(cedar, root, position + offset + Vector3.up * 0.015f, Vector3.one * treeSize, i * 19 + tree * 47);
                }
            }
        }

        private static void LightingAndCamera()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.43f, 0.56f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.31f, 0.44f, 0.57f);
            RenderSettings.ambientGroundColor = new Color(0.15f, 0.24f, 0.35f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.55f, 0.72f, 0.83f);
            RenderSettings.fogStartDistance = 48; RenderSettings.fogEndDistance = 125;
            var camera = Camera.main;
            camera.transform.position = new Vector3(0, 23, -28);
            camera.transform.LookAt(new Vector3(0, -1.1f, 0));
            camera.orthographic = true; camera.orthographicSize = 11.6f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
            camera.farClipPlane = 200;
            var framing = camera.GetComponent<ArenaCamera>();
            framing.minimumVerticalSize = 11.6f; framing.arenaHalfWidth = 13.2f;
            var sun = GameObject.Find("Sun").GetComponent<Light>();
            sun.transform.rotation = Quaternion.Euler(48, -32, 0);
            sun.color = new Color(0.95f, 0.98f, 1); sun.intensity = 1.0f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.65f;
            sun.shadowBias = 0.025f; sun.shadowNormalBias = 0.12f;
        }

        private static void CaptureSamples()
        {
            var camera = Camera.main;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath);
            var positions = new[] { new Vector3(0, 0.85f, -4), new Vector3(-4, 0.85f, 0), new Vector3(4, 0.85f, 1),
                new Vector3(0, 0.85f, 5), new Vector3(5, 0.85f, -4), new Vector3(-5, 0.85f, 4) };
            var samples = new GameObject[positions.Length];
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float cameraSize = camera.orthographicSize;
            try
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    samples[i].transform.SetPositionAndRotation(positions[i], Quaternion.Euler(i * 23, i * 47, 0));
                }
                Capture(camera, Results + "/gameplay-preview.png");
                for (int i = 1; i < samples.Length; i++) samples[i].SetActive(false);
                samples[0].transform.SetPositionAndRotation(new Vector3(0, 0.85f, 0), Quaternion.Euler(17, 30, 0));
                camera.transform.position = new Vector3(2.5f, 2.5f, -3.7f);
                camera.transform.LookAt(new Vector3(0, 0.85f, 0)); camera.orthographicSize = 1.3f;
                Capture(camera, Results + "/snowball-closeup.png");
            }
            finally
            {
                foreach (var sample in samples) if (sample != null) Object.DestroyImmediate(sample);
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation); camera.orthographicSize = cameraSize;
            }
        }

        private static void Capture(Camera camera, string path)
        {
            var target = new RenderTexture(1920, 1080, 24);
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            }
        }
    }
}
