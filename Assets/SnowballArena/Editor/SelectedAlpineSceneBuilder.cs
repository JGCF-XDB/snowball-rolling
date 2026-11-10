using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SnowballArena.Editor
{
    public static class SelectedAlpineSceneBuilder
    {
        public const string SourceScene = "Assets/SnowballArena/Scenes/SelectedAlpine01.unity";
        const string Art = "Assets/SnowballArena/Art/Selected01";
        const string Results = "TestResults/Selected01";

        [MenuItem("Snowball Arena/Apply Selected Alpine 01")]
        public static void Apply()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var selectedLit = Shader.Find("SnowArena/Selected01Lit");
            var selectedSky = Shader.Find("SnowArena/Selected01Sky");
            if (selectedLit == null || selectedSky == null) throw new InvalidOperationException("Selected 01 shaders are missing.");
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Art }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material.shader.name == "SnowArena/FacetedSnowLit") material.shader = selectedLit;
                if (material.shader.name == "SnowArena/AlpineSky") material.shader = selectedSky;
                EditorUtility.SetDirty(material);
            }
            var target = EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            var surface = Object.FindFirstObjectByType<ArenaSurface>();
            var collision = surface.GetComponent<MeshCollider>();
            var collisionMesh = collision.sharedMesh;
            var collisionTransform = surface.transform.localToWorldMatrix;
            var match = Object.FindFirstObjectByType<MatchController>();
            foreach (string name in new[] { "Snow Mountain Environment", "Main Camera", "Sun", "Alpine layout 01-diagonal", "Gameplay perspective cliffs" })
            {
                var old = GameObject.Find(name);
                if (old != null) Object.DestroyImmediate(old);
            }

            var source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(source);
            var sky = RenderSettings.skybox;
            var fogColor = RenderSettings.fogColor;
            float fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance;
            var ambientSky = RenderSettings.ambientSkyColor;
            var ambientEquator = RenderSettings.ambientEquatorColor;
            var ambientGround = RenderSettings.ambientGroundColor;
            var ambientProbe = RenderSettings.ambientProbe;
            var sun = RenderSettings.sun;
            var sourceRoots = source.GetRootGameObjects();
            Camera camera = null;
            foreach (var item in sourceRoots)
            {
                if (item.TryGetComponent<ArenaSurface>(out var authoredSurface))
                {
                    var authoredVisual = authoredSurface.transform.Find("Selected 01 snow, glacier and rock");
                    var previous = surface.transform.Find("Selected 01 snow, glacier and rock");
                    if (previous != null) Object.DestroyImmediate(previous.gameObject);
                    authoredVisual.SetParent(null);
                    SceneManager.MoveGameObjectToScene(authoredVisual.gameObject, target);
                    authoredVisual.SetParent(surface.transform, false);
                    authoredVisual.localPosition = Vector3.zero;
                    authoredVisual.localRotation = Quaternion.identity;
                    authoredVisual.localScale = new Vector3(1, 1.3f, 1);
                    surface.GetComponent<MeshRenderer>().enabled = false;
                    continue;
                }
                if (item.name == "Gameplay perspective cliffs")
                {
                    string prefabPath = Art + "/SelectedAlpineEnvironment.prefab";
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab == null) prefab = PrefabUtility.SaveAsPrefabAsset(item, prefabPath);
                    var environment = (GameObject)PrefabUtility.InstantiatePrefab(prefab, target);
                    environment.name = item.name;
                    continue;
                }
                if (item.GetComponent<MatchController>() != null) continue;
                if (item.TryGetComponent<Camera>(out var found)) camera = found;
                SceneManager.MoveGameObjectToScene(item, target);
            }
            EditorSceneManager.CloseScene(source, true);
            SceneManager.SetActiveScene(target);
            if (camera == null) throw new InvalidOperationException("The authored main camera is missing.");
            match.arenaCamera = camera;
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientProbe = ambientProbe;
            RenderSettings.ambientIntensity = 1;
            var pipeline = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            pipeline.shadowDistance = 120;
            EditorUtility.SetDirty(pipeline);
            if (collision.sharedMesh != collisionMesh || surface.transform.localToWorldMatrix != collisionTransform)
                throw new InvalidOperationException("The platform collision geometry or transform changed.");
            Validate(camera, surface);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(target);
            EditorSceneManager.SaveScene(target);

            // Reopen the saved scene so captures exercise the persisted assets.
            EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            Capture();
            Debug.Log("SELECTED 01: modeled environment saved, verified and captured.");
        }

        static void Validate(Camera camera, ArenaSurface surface)
        {
            if (camera.orthographic || Mathf.Abs(camera.transform.eulerAngles.x - 60) > .01f)
                throw new InvalidOperationException("The gameplay camera must be 60 degrees.");
            var backdrop = camera.GetComponent<ArenaCamera>().backgroundCamera;
            if (backdrop == null || !backdrop.GetUniversalAdditionalCameraData().cameraStack.Contains(camera))
                throw new InvalidOperationException("The scenery camera stack is disconnected.");
            var near = GameObject.Find("Gameplay perspective cliffs");
            if (!PrefabUtility.IsPartOfPrefabInstance(near))
                throw new InvalidOperationException("The selected environment is not connected to its editable prefab.");
            if (near.GetComponentsInChildren<Collider>().Length != 0)
                throw new InvalidOperationException("The decorative cliffs contain gameplay colliders.");
            int triangles = 0;
            foreach (var renderer in near.GetComponentsInChildren<MeshRenderer>())
            {
                int layer = 1 << renderer.gameObject.layer;
                if ((camera.cullingMask & layer) == 0 || (backdrop.cullingMask & layer) != 0)
                    throw new InvalidOperationException("A surrounding platform uses the scenery camera.");
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (!EditorUtility.IsPersistent(mesh)) throw new InvalidOperationException("A model was not saved.");
                triangles += mesh.triangles.Length / 3;
            }
            Physics.SyncTransforms();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath);
            if (!SpawnService.TryGetPositions(surface, 16, prefab.GetComponent<SnowballBody>().settings.initialRadius, out _))
                throw new InvalidOperationException("The scene cannot provide sixteen safe spawns.");
            Directory.CreateDirectory(Results);
            File.WriteAllLines(Results + "/validation.txt", new[] {
                "PASS 60 degree main and surrounding platforms use the same camera",
                "PASS Near environment meshes are persisted assets",
                "PASS Decorative cliffs contain no gameplay colliders",
                "PASS Sixteen safe spawns",
                "Near environment triangles="+triangles
            });
        }

        [MenuItem("Snowball Arena/Capture Selected Alpine 01")]
        public static void Capture()
        {
            var camera = Camera.main;
            Validate(camera, Object.FindFirstObjectByType<ArenaSurface>());
            Directory.CreateDirectory("docs/art/selected01");
            ModelingReferenceBuilder.Capture(camera, "docs/art/selected01/arena.png");
            var samples = new List<GameObject>();
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath);
                var positions = new[] { new Vector2(-2.4f, -3.1f), new Vector2(-3.85f, 2.4f), new Vector2(4.5f, -.4f), new Vector2(1.9f, 5.3f) };
                var sizes = new[] { 1.22f, 1.38f, 1.20f, 1.05f };
                for (int i = 0; i < positions.Length; i++)
                {
                    var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    samples.Add(actor);
                    actor.transform.localScale = Vector3.one * sizes[i];
                    actor.transform.SetPositionAndRotation(new Vector3(positions[i].x, .85f * sizes[i], positions[i].y),
                        Quaternion.Euler(23 + i * 27, 50 + i * 34, 30));
                    var look = actor.GetComponent<SnowballAppearance>();
                    look.SetIdentity(i);
                    look.snowRenderer.sharedMaterials[0].SetColor("_BaseColor", new Color(.92f, .96f, 1));
                    look.snowRenderer.sharedMaterials[1].SetColor("_BaseColor", new Color(.81f, .89f, .98f));
                }
                ModelingReferenceBuilder.Capture(camera, "docs/art/selected01/gameplay.png");
            }
            finally
            {
                foreach (var sample in samples) Object.DestroyImmediate(sample);
            }
        }

        public static void ValidatePhysics()
        {
            PrototypeValidation.Run();
            GrowthCollisionValidation.Run();
            ArenaExtensionValidation.Run();
            EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            Validate(Camera.main, Object.FindFirstObjectByType<ArenaSurface>());
            Debug.Log("SELECTED 01 PHYSICS: all existing validation suites passed.");
        }
    }
}
