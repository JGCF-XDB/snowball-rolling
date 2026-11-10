using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SnowballArena.Editor
{
    public static class ModelingReferenceBuilder
    {
        private const string Root = PrototypeBuilder.Root;
        private const string Results = "TestResults/ConceptMatch";

        [MenuItem("Snowball Arena/Apply Modeling Reference")]
        public static void Apply()
        {
            if (File.Exists(SelectedAlpineSceneBuilder.SourceScene))
            {
                SelectedAlpineSceneBuilder.Apply();
                return;
            }
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var snow = Surface("ReferenceSnow", 0.10f, 1, 1);
            var canopy = Surface("ReferenceTreeSnow", 0.10f, 0.65f);
            var peakSnow = Surface("ReferencePeakSnow", 0.10f, 0.25f);
            var white = Surface("ReferenceSnowball", 0.18f, 0.55f);
            var packed = Surface("ReferencePackedSnow", 0.24f, 0.5f);
            var ice = Surface("ReferenceGlacier", 0.72f, 0, 0, true);
            var core = Surface("ReferenceIceCore", 0.48f);
            var rock = Surface("ReferenceRock", 0.08f);
            var pine = Surface("ReferenceCedar", 0.06f);
            var bark = Surface("ReferenceBark", 0.07f);
            var platformMaterials = new[] { snow, ice, rock, core };
            var isletMaterials = new[] { canopy, ice, rock, core };
            var platform = ReferenceMeshes.Import("ConceptPlatform", "ReferencePlatform", 4);
            var ball = ReferenceMeshes.Import("ConceptSnowball", "ReferenceSnowball", 2);
            var cedarPrefabs = new GameObject[2];
            var isletPrefabs = new GameObject[3];
            var mountainPrefabs = new GameObject[3];
            for (int i = 0; i < cedarPrefabs.Length; i++)
                cedarPrefabs[i] = Module(i == 0 ? "SnowCedar" : "SnowCedar1",
                    ReferenceMeshes.Import("ConceptCedar" + i, i == 0 ? "SnowCedar" : "SnowCedar1", 3),
                    new[] { canopy, pine, bark });
            for (int i = 0; i < isletPrefabs.Length; i++)
                isletPrefabs[i] = Module(i == 0 ? "FloatingIslet" : "FloatingIslet" + i,
                    ReferenceMeshes.Import("ConceptIslet" + i, i == 0 ? "FloatingIslet" : "FloatingIslet" + i, 4), isletMaterials);
            for (int i = 0; i < mountainPrefabs.Length; i++)
                mountainPrefabs[i] = Module("SnowMountain" + i,
                    ReferenceMeshes.Import("ConceptMountain" + i, "SnowMountain" + i, 2),
                    new[] { peakSnow, Surface("ReferenceMountain" + i, 0.07f) });
            var cliff = Module("RockCliff", ReferenceMeshes.Import("ConceptCliff", "RockCliff", 4), isletMaterials);
            UpdateBall(ball, white, packed);
            UpdateArenaPrefab(platform, platformMaterials);

            var scene = EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            var surface = Object.FindFirstObjectByType<ArenaSurface>();
            var collisionMesh = surface.GetComponent<MeshCollider>().sharedMesh;
            surface.GetComponent<MeshFilter>().sharedMesh = platform;
            surface.GetComponent<MeshRenderer>().sharedMaterials = platformMaterials;
            for (int i = surface.transform.childCount - 1; i >= 0; i--)
                if (surface.transform.GetChild(i).name == "Shallow rolling tracks")
                    Object.DestroyImmediate(surface.transform.GetChild(i).gameObject);
            var oldEnvironment = GameObject.Find("Snow Mountain Environment");
            if (oldEnvironment != null) Object.DestroyImmediate(oldEnvironment);
            var environment = new GameObject("Snow Mountain Environment");
            Environment(environment.transform, cedarPrefabs, isletPrefabs, mountainPrefabs, cliff);
            LightingAndCamera(environment.transform);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (surface.GetComponent<MeshCollider>().sharedMesh != collisionMesh) throw new Exception("The art update changed the gameplay collider.");
            if (environment.GetComponentsInChildren<Collider>().Length != 0) throw new Exception("The environment contains gameplay obstacles.");
            Physics.SyncTransforms();
            var body = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath).GetComponent<SnowballBody>();
            if (!SpawnService.TryGetPositions(surface, 16, body.settings.initialRadius, out _)) throw new Exception("Sixteen safe spawns are no longer possible.");
            foreach (var p in ball.vertices)
                if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z) || p.magnitude > 0.54f)
                    throw new Exception("Invalid snowball silhouette: " + p);
            if (ball.triangles.Length / 3 > 8000) throw new Exception("Snowball triangle budget exceeded.");
            var arenaCamera = Camera.main;
            if (arenaCamera.orthographic || (arenaCamera.cullingMask & (1 << 2)) == 0)
                throw new Exception("The match camera must see the entire alpine landscape in perspective.");
            Directory.CreateDirectory(Results);
            File.WriteAllText(Results + "/asset-validation.txt",
                "PASS Original flat gameplay collider retained\nPASS Decorative environment has no colliders\nPASS Sixteen safe spawns\n" +
                "PASS FBX axes, authored face normals and vertex colors imported\nPASS Snowball stays within its visual radius tolerance\n" +
                "Snowball triangles=" + ball.triangles.Length / 3 + "\nPlatform triangles=" + platform.triangles.Length / 3 +
                "\nPlatform bounds=" + platform.bounds + "\n");
            CaptureSamples();
            var shader = Shader.Find("SnowArena/FacetedSnowLit");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity.ToString() == "Error") throw new Exception("Concept shader: " + message.message);
            EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            Debug.Log("SNOWBALL CONCEPT: Blender models and alpine atmosphere applied; comparison captures saved.");
        }

        public static void ApplyAndBuild()
        {
            Apply();
            ValidatePhysics();
            MatchSceneBuilder.BuildWindowsGame();
        }

        public static void BuildConceptPreview()
        {
            ValidatePhysics();
            BuildPreview(BuildOptions.Development);
        }

        public static void BuildPlayablePreview()
        {
            BuildPreview(BuildOptions.None);
        }

        private static void BuildPreview(BuildOptions options)
        {
            Directory.CreateDirectory("Builds/ConceptPreview");
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { MatchSceneBuilder.MatchPath, PrototypeBuilder.ScenePath },
                locationPathName = "Builds/ConceptPreview/SnowballArena.exe",
                target = BuildTarget.StandaloneWindows64,
                options = options
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Concept preview build failed.");
            Debug.Log("SNOWBALL CONCEPT: New playable preview completed.");
        }

        private static void ValidatePhysics()
        {
            PrototypeValidation.Run();
            GrowthCollisionValidation.Run();
            ArenaExtensionValidation.Run();
        }

        internal static Material Surface(string name, float smoothness, float detail = 0, float tracks = 0, bool glacier = false)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + name + ".mat");
            var shader = Shader.Find("SnowArena/FacetedSnowLit");
            if (shader == null) throw new Exception("Concept surface shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                if (!name.StartsWith("Study", StringComparison.Ordinal))
                    AssetDatabase.CreateAsset(material, Root + "/Materials/" + name + ".mat");
            }
            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            material.enableInstancing = true;
            material.SetColor("_BaseColor", new Color(1, 1, 1, glacier ? 0.92f : 1));
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", glacier ? 0.08f : 0);
            material.SetFloat("_SnowDetail", detail);
            material.SetFloat("_GroundTracks", tracks);
            material.SetFloat("_IsIce", glacier ? 1 : 0);
            bool mountain = name.Contains("Mountain") || name.Contains("Peak");
            material.SetFloat("_ValleyFogStart", mountain ? -60 : -5);
            material.SetFloat("_ValleyFogDensity", mountain ? 0.10f : 0.023f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_ZWrite", glacier ? 0 : 1);
            material.SetFloat("_SrcBlend", (float)(glacier ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat("_DstBlend", (float)(glacier ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetOverrideTag("RenderType", glacier ? "Transparent" : "Opaque");
            material.renderQueue = glacier ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            material.SetShaderPassEnabled("ShadowCaster", !glacier);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material SpecialMaterial(string name, string shaderName)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null) throw new Exception("Missing shader: " + shaderName);
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            EditorUtility.SetDirty(material);
            return material;
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

        private static void UpdateBall(Mesh ball, Material snow, Material packed)
        {
            var shell = Object.Instantiate(ball);
            shell.name = "ReferenceSnowballOutline";
            var indices = ball.triangles;
            shell.subMeshCount = 1;
            shell.SetTriangles(indices, 0);
            string shellPath = Root + "/Art/ReferenceSnowballOutline.asset";
            var oldShell = AssetDatabase.LoadAssetAtPath<Mesh>(shellPath);
            if (oldShell != null)
            {
                EditorUtility.CopySerialized(shell, oldShell); EditorUtility.SetDirty(oldShell);
                Object.DestroyImmediate(shell); shell = oldShell;
            }
            else AssetDatabase.CreateAsset(shell, shellPath);
            var outline = SpecialMaterial("SnowballIdentityOutline", "SnowArena/IdentityOutline");
            outline.SetFloat("_OutlineWidth", 0.02f);
            outline.SetColor("_OutlineColor", new Color(1, 0.12f, 0.19f));
            var prefab = PrefabUtility.LoadPrefabContents(PrototypeBuilder.PrefabPath);
            try
            {
                var body = prefab.GetComponent<SnowballBody>();
                var renderer = body.visual.GetComponent<MeshRenderer>();
                body.visual.GetComponent<MeshFilter>().sharedMesh = ball;
                renderer.sharedMaterials = new[] { snow, packed };
                var shellTransform = body.visual.Find("Identity outline");
                if (shellTransform == null)
                {
                    shellTransform = new GameObject("Identity outline").transform;
                    shellTransform.SetParent(body.visual, false);
                    shellTransform.gameObject.AddComponent<MeshFilter>();
                    shellTransform.gameObject.AddComponent<MeshRenderer>();
                }
                shellTransform.GetComponent<MeshFilter>().sharedMesh = shell;
                var shellRenderer = shellTransform.GetComponent<MeshRenderer>();
                shellRenderer.sharedMaterial = outline;
                shellRenderer.shadowCastingMode = ShadowCastingMode.Off;
                shellRenderer.receiveShadows = false;
                var appearance = prefab.GetComponent<SnowballAppearance>() ?? prefab.AddComponent<SnowballAppearance>();
                appearance.snowRenderer = renderer; appearance.outlineRenderer = shellRenderer;
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

        private static GameObject Place(GameObject prefab, Transform parent, Vector3 position, Vector3 scale, float angle)
        {
            var item = EditorUtility.IsPersistent(prefab) ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)
                : Object.Instantiate(prefab, parent);
            item.SetActive(true);
            item.layer = 2;
            item.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0, angle, 0));
            item.transform.localScale = scale;
            return item;
        }

        private static void PlacePeak(GameObject prefab, Transform parent, Vector3 summit, Vector3 scale, float yaw)
        {
            var vertices = prefab.GetComponent<MeshFilter>().sharedMesh.vertices;
            var peak = vertices[0];
            foreach (var vertex in vertices)
                if (vertex.y > peak.y) peak = vertex;
            var offset = Quaternion.Euler(0,yaw,0) * Vector3.Scale(peak,scale);
            Place(prefab,parent,summit-offset,scale,yaw);
        }

        internal static void Environment(Transform root, GameObject[] cedars, GameObject[] islets, GameObject[] mountains, GameObject cliff)
        {
            var foothills = new GameObject[3];
            var foothillMaterial = Surface(EditorUtility.IsPersistent(mountains[0]) ? "ReferenceFoothill" : "StudyFoothill",0.07f);
            foothillMaterial.SetFloat("_ValleyFogStart",0);
            foothillMaterial.SetFloat("_ValleyFogDensity",0.027f);
            for (int i = 0; i < 3; i++)
            {
                bool save = EditorUtility.IsPersistent(mountains[i]);
                var mesh = ReferenceMeshes.RockOnly(mountains[i].GetComponent<MeshFilter>().sharedMesh,"MountainFoothills"+i,save);
                var materials = new[] { foothillMaterial };
                foothills[i] = save ? Module("MountainFoothills"+i,mesh,materials) : new GameObject("Foothill template");
                if (!save)
                {
                    foothills[i].AddComponent<MeshFilter>().sharedMesh = mesh;
                    foothills[i].AddComponent<MeshRenderer>().sharedMaterials = materials;
                    foothills[i].SetActive(false);
                }
            }
            // Silhouettes descend into a central saddle, with the clearest snow faces on the near ridge.
            PlacePeak(mountains[0],root,new Vector3(-77.97f, -31.14f, 113.00f),new Vector3(32.00f, 18.00f, 24.64f),147);
            PlacePeak(mountains[1],root,new Vector3(-57.44f, -29.55f, 120.00f),new Vector3(27.00f, 19.00f, 20.79f),36);
            PlacePeak(mountains[2],root,new Vector3(-31.00f, -31.45f, 132.00f),new Vector3(35.00f, 18.00f, 26.95f),84);
            PlacePeak(mountains[0],root,new Vector3(-3.54f, -35.16f, 140.00f),new Vector3(30.00f, 20.00f, 23.10f),16);
            PlacePeak(mountains[1],root,new Vector3(23.33f, -29.44f, 126.00f),new Vector3(29.00f, 22.00f, 22.33f),113);
            PlacePeak(mountains[2],root,new Vector3(55.18f, -34.89f, 138.00f),new Vector3(34.00f, 17.00f, 26.18f),44);
            PlacePeak(mountains[0],root,new Vector3(73.72f, -26.35f, 114.00f),new Vector3(30.00f, 20.00f, 23.10f),270);
            PlacePeak(mountains[1],root,new Vector3(-53.95f, -15.35f, 62.00f),new Vector3(20.00f, 14.00f, 15.40f),62);
            PlacePeak(mountains[2],root,new Vector3(-35.20f, -15.66f, 71.00f),new Vector3(23.00f, 18.00f, 17.71f),135);
            PlacePeak(mountains[0],root,new Vector3(-16.27f, -21.75f, 78.00f),new Vector3(18.00f, 12.00f, 13.86f),72);
            PlacePeak(mountains[1],root,new Vector3(-3.71f, -22.78f, 84.00f),new Vector3(22.00f, 13.00f, 16.94f),176);
            PlacePeak(mountains[2],root,new Vector3(10.69f, -20.50f, 74.00f),new Vector3(16.00f, 12.00f, 12.32f),85);
            PlacePeak(mountains[0],root,new Vector3(23.79f, -11.95f, 63.00f),new Vector3(26.00f, 18.00f, 20.02f),31);
            PlacePeak(mountains[1],root,new Vector3(50.11f, -17.01f, 70.00f),new Vector3(22.00f, 18.00f, 16.94f),140);

            // Exposed valley walls recede behind the hanging shelves.
            PlacePeak(foothills[0],root,new Vector3(-33.15f, -14.02f, 29.00f),new Vector3(18.00f, 14.00f, 10.26f),19);
            PlacePeak(foothills[1],root,new Vector3(-20.97f, -21.44f, 35.00f),new Vector3(15.00f, 16.00f, 8.55f),52);
            PlacePeak(foothills[2],root,new Vector3(-8.37f, -21.46f, 23.00f),new Vector3(12.00f, 15.00f, 6.84f),127);
            PlacePeak(foothills[0],root,new Vector3(11.29f, -23.79f, 32.00f),new Vector3(17.00f, 18.00f, 9.69f),95);
            PlacePeak(foothills[1],root,new Vector3(24.83f, -18.34f, 38.00f),new Vector3(20.00f, 13.00f, 11.40f),46);
            PlacePeak(foothills[2],root,new Vector3(35.46f, -11.29f, 30.00f),new Vector3(17.00f, 15.00f, 9.69f),73);

            // Each shelf has its own footprint, drop and stand of cedars.
            Place(islets[0],root,new Vector3(-22.89f, -6.02f, 15.00f),new Vector3(5.60f, 3.20f, 2.90f),-15);
            Place(cedars[0],root,new Vector3(-24.69f, -5.78f, 15.10f),new Vector3(0.60f, 0.60f, 0.60f),17);
            Place(cedars[1],root,new Vector3(-21.19f, -5.78f, 15.00f),new Vector3(1.13f, 1.13f, 1.13f),80);
            Place(cedars[0],root,new Vector3(-20.19f, -5.78f, 15.25f),new Vector3(0.48f, 0.48f, 0.48f),143);
            Place(islets[1],root,new Vector3(-15.20f, -8.24f, 28.00f),new Vector3(2.60f, 2.70f, 1.80f),27);
            Place(cedars[1],root,new Vector3(-15.95f, -8.03f, 28.00f),new Vector3(0.90f, 0.90f, 0.90f),46);
            Place(cedars[0],root,new Vector3(-14.70f, -8.03f, 28.20f),new Vector3(0.54f, 0.54f, 0.54f),109);
            Place(cedars[1],root,new Vector3(-14.08f, -8.03f, 27.90f),new Vector3(0.70f, 0.70f, 0.70f),172);
            Place(islets[2],root,new Vector3(-14.96f, -5.81f, 9.00f),new Vector3(1.65f, 2.35f, 1.10f),-15);
            Place(cedars[0],root,new Vector3(-15.41f, -5.63f, 9.00f),new Vector3(0.61f, 0.61f, 0.61f),75);
            Place(cedars[1],root,new Vector3(-14.56f, -5.63f, 9.30f),new Vector3(0.39f, 0.39f, 0.39f),138);
            Place(islets[0],root,new Vector3(9.91f, -8.52f, 32.00f),new Vector3(2.30f, 2.00f, 1.65f),15);
            Place(cedars[1],root,new Vector3(9.51f, -8.37f, 32.00f),new Vector3(0.65f, 0.65f, 0.65f),104);
            Place(cedars[0],root,new Vector3(10.59f, -8.37f, 32.20f),new Vector3(0.94f, 0.94f, 0.94f),167);
            Place(islets[1],root,new Vector3(17.43f, -5.63f, 13.00f),new Vector3(3.05f, 3.65f, 2.05f),24);
            Place(cedars[0],root,new Vector3(16.43f, -5.35f, 13.20f),new Vector3(0.68f, 0.68f, 0.68f),133);
            Place(cedars[1],root,new Vector3(17.80f, -5.35f, 12.86f),new Vector3(1.28f, 1.28f, 1.28f),196);
            Place(cedars[0],root,new Vector3(18.91f, -5.35f, 13.20f),new Vector3(0.61f, 0.61f, 0.61f),259);
            Place(islets[2],root,new Vector3(21.11f, -5.55f, 4.00f),new Vector3(4.15f, 4.00f, 2.70f),37);
            Place(cedars[1],root,new Vector3(21.51f, -5.25f, 3.85f),new Vector3(0.71f, 0.71f, 0.71f),162);
            Place(cliff,root,new Vector3(-13.83f, -1.30f, -14.00f),new Vector3(3.65f, 4.30f, 2.50f),8);
            Place(cedars[0],root,new Vector3(-12.45f, -0.98f, -14.00f),new Vector3(1.28f, 1.28f, 1.28f),191);
            Place(cedars[1],root,new Vector3(-10.66f, -0.98f, -13.65f),new Vector3(0.72f, 0.72f, 0.72f),254);
            Place(cliff,root,new Vector3(13.19f, -3.39f, -12.00f),new Vector3(4.65f, 4.65f, 2.40f),-21);
            Place(cedars[1],root,new Vector3(11.44f, -3.04f, -11.60f),new Vector3(0.73f, 0.73f, 0.73f),220);
            Place(cedars[0],root,new Vector3(12.79f, -3.04f, -11.75f),new Vector3(1.27f, 1.27f, 1.27f),283);
            Place(cedars[1],root,new Vector3(14.99f, -3.04f, -11.70f),new Vector3(0.60f, 0.60f, 0.60f),346);
            for (int layer = 0; layer < 3; layer++)
            {
                var mist = GameObject.CreatePrimitive(PrimitiveType.Plane);
                mist.name = "Canyon mist " + layer; mist.layer = 2;
                Object.DestroyImmediate(mist.GetComponent<Collider>());
                mist.transform.SetParent(root, false);
                mist.transform.localPosition = new Vector3(0, -31 - layer * 7, 18 + layer * 13);
                mist.transform.localScale = new Vector3(12, 1, 15);
                var renderer = mist.GetComponent<MeshRenderer>();
                var material = SpecialMaterial("ValleyMist" + layer, "SnowArena/ValleyMist");
                material.SetColor("_Color", new Color(0.64f, 0.81f, 0.94f, layer == 0 ? 0.33f : 0.30f));
                material.SetFloat("_Seed", layer * 13.7f);
                renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            var cloudTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/Concept/ValleyCloud.png");
            var cloudImporter = AssetImporter.GetAtPath(Root + "/Art/Concept/ValleyCloud.png") as TextureImporter;
            if (cloudImporter != null && (!cloudImporter.alphaIsTransparency || cloudImporter.wrapMode != TextureWrapMode.Clamp))
            {
                cloudImporter.alphaIsTransparency = true;
                cloudImporter.wrapMode = TextureWrapMode.Clamp;
                cloudImporter.textureCompression = TextureImporterCompression.CompressedHQ;
                cloudImporter.SaveAndReimport();
            }
            for (int i = 0; i < 42; i++)
            {
                var wisp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                wisp.name = "Soft valley cloud " + i; wisp.layer = 2;
                Object.DestroyImmediate(wisp.GetComponent<Collider>());
                wisp.transform.SetParent(root,false);
                int row = i / 6;
                float cloudZ = row < 5 ? -3 + row * 19 : row == 5 ? 38 : 1;
                float cloudY = row < 5 ? -14 - row * 8.0f : row == 5 ? -18.5f : -13;
                wisp.transform.localPosition = new Vector3((i % 6 - 2.5f) * 12 + Mathf.Sin(i * 2.1f) * 3,
                    cloudY + Mathf.Sin(i*2.8f)*1.4f, cloudZ + Mathf.Cos(i * 1.7f) * 6);
                wisp.transform.localRotation = Quaternion.Euler(20,0,Mathf.Sin(i*1.7f)*9);
                wisp.transform.localScale = new Vector3(18 + i % 3 * 4,7.5f + i % 2 * 2,1);
                var renderer = wisp.GetComponent<MeshRenderer>();
                var material = SpecialMaterial("CloudWisp" + i,"SnowArena/ValleyMist");
                material.SetColor("_Color",new Color(0.82f,0.91f,1,0.67f+(i%4)*0.075f));
                material.SetTexture("_CloudTex",cloudTexture);
                material.SetFloat("_Seed",i * 7.31f); material.SetFloat("_Billboard",1);
                renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private static T VolumeSetting<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var setting)) return setting;
            setting = profile.Add<T>(true);
            AssetDatabase.AddObjectToAsset(setting, profile);
            return setting;
        }

        internal static void LightingAndCamera(Transform environment)
        {
            var sky = SpecialMaterial("AlpineSky", "SnowArena/AlpineSky");
            sky.SetColor("_Horizon",new Color(.62f,.85f,1f));
            sky.SetColor("_Top",new Color(.36f,.73f,.99f));
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.70f, 0.80f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.52f, 0.60f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.29f, 0.38f);
            RenderSettings.ambientIntensity = 1;
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(new Color(0.38f, 0.44f, 0.52f));
            probe.AddDirectionalLight(Vector3.up, new Color(0.64f, 0.71f, 0.80f), 0.55f);
            RenderSettings.ambientProbe = probe;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.42f, 0.70f, 0.92f);
            RenderSettings.fogStartDistance = 75; RenderSettings.fogEndDistance = 260;
            var camera = Camera.main;
            // Use the same forward-lit materials for the arena and landscape.
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            rendererData.renderingMode = RenderingMode.Forward;
            EditorUtility.SetDirty(rendererData);
            camera.transform.position = new Vector3(-0.65f, 17.65f, -46);
            camera.transform.rotation = Quaternion.Euler(23.5f,0,0);
            camera.orthographic = false; camera.fieldOfView = 24;
            camera.nearClipPlane = 0.15f; camera.farClipPlane = 260;
            camera.clearFlags = CameraClearFlags.Skybox; camera.allowHDR = true;
            var framing = camera.GetComponent<ArenaCamera>() ?? camera.gameObject.AddComponent<ArenaCamera>();
            framing.minimumVerticalSize = 10.5f; framing.arenaHalfWidth = 13.2f;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            camera.cullingMask = ~0;
            cameraData.renderType = CameraRenderType.Base;
            cameraData.renderPostProcessing = true;
            cameraData.requiresDepthOption = CameraOverrideOption.On;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            framing.backgroundCamera = null;
            var sun = GameObject.Find("Sun").GetComponent<Light>();
            sun.transform.rotation = Quaternion.Euler(39, -41, 0);
            sun.color = new Color(1, 0.99f, 0.97f); sun.intensity = 1.8f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.70f;
            sun.shadowBias = 0.025f; sun.shadowNormalBias = 0.12f;
            RenderSettings.sun = sun;
            var fill = new GameObject("Cold sky fill").AddComponent<Light>();
            fill.transform.SetParent(environment, false);
            fill.transform.rotation = Quaternion.Euler(30, 145, 0);
            fill.type = LightType.Directional; fill.color = new Color(0.58f, 0.77f, 1);
            fill.intensity = 0.64f; fill.shadows = LightShadows.None;

            string path = Root + "/Art/ConceptLook.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            VolumeSetting<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var adjustments = VolumeSetting<ColorAdjustments>(profile);
            adjustments.postExposure.Override(0.15f); adjustments.contrast.Override(3); adjustments.saturation.Override(8);
            VolumeSetting<WhiteBalance>(profile).temperature.Override(-2);
            var bloom = VolumeSetting<Bloom>(profile);
            bloom.intensity.Override(0.06f); bloom.threshold.Override(1.2f);
            EditorUtility.SetDirty(profile);
            foreach (var setting in profile.components) EditorUtility.SetDirty(setting);
            var volume = new GameObject("Alpine color and exposure").AddComponent<Volume>();
            volume.transform.SetParent(environment, false); volume.isGlobal = true; volume.priority = 20; volume.sharedProfile = profile;
        }

        private static void CaptureSamples()
        {
            var camera = Camera.main;
            var framing = camera.GetComponent<ArenaCamera>();
            bool wasFraming = framing.enabled; framing.enabled = false;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath);
            var positions = new[] { new Vector3(0,0.85f,-4), new Vector3(-4,0.85f,0), new Vector3(4,0.85f,1),
                new Vector3(0,0.85f,5), new Vector3(5,0.85f,-4), new Vector3(-5,0.85f,4) };
            var samples = new GameObject[positions.Length];
            var oldPosition = camera.transform.position;
            var oldRotation = camera.transform.rotation;
            bool oldOrthographic = camera.orthographic;
            float oldSize = camera.orthographicSize;
            try
            {
                Capture(camera, Results + "/arena-overview.png");
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    samples[i].transform.SetPositionAndRotation(positions[i], Quaternion.Euler(i * 23, i * 47, 0));
                    samples[i].GetComponent<SnowballAppearance>().SetIdentity(i);
                }
                Capture(camera, Results + "/gameplay-preview.png");
                for (int i = 1; i < samples.Length; i++) samples[i].SetActive(false);
                samples[0].transform.SetPositionAndRotation(new Vector3(0,0.85f,0), Quaternion.Euler(17,30,0));
                samples[0].GetComponent<SnowballAppearance>().outlineRenderer.enabled = false;
                camera.transform.position = new Vector3(2.5f,2.5f,-3.7f);
                camera.transform.LookAt(new Vector3(0,0.85f,0));
                camera.orthographic = true; camera.orthographicSize = 1.35f;
                Capture(camera, Results + "/snowball-closeup.png");
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i].SetActive(true);
                    samples[i].transform.position = new Vector3((i % 3 - 1) * 2.7f,0.85f,(i / 3) * 2.5f);
                    samples[i].GetComponent<SnowballAppearance>().outlineRenderer.enabled = true;
                }
                camera.transform.position = new Vector3(0,7,-10);
                camera.transform.LookAt(new Vector3(0,0.6f,1.1f)); camera.orthographicSize = 3.7f;
                Capture(camera, Results + "/identity-colors.png");
            }
            finally
            {
                foreach (var sample in samples) if (sample != null) Object.DestroyImmediate(sample);
                camera.transform.SetPositionAndRotation(oldPosition,oldRotation);
                camera.orthographic = oldOrthographic; camera.orthographicSize = oldSize; framing.enabled = wasFraming;
            }
        }

        internal static void Capture(Camera camera, string path)
        {
            var framing = camera.GetComponent<ArenaCamera>();
            if (framing != null) camera = framing.RenderRoot;
            var target = new RenderTexture(1920,1080,24,RenderTextureFormat.DefaultHDR,RenderTextureReadWrite.Linear);
            var readback = new Texture2D(1920,1080,TextureFormat.RGBAFloat,false,true);
            var image = new Texture2D(1920,1080,TextureFormat.RGB24,false);
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = target };
                RenderPipeline.SubmitRenderRequest(camera,request); RenderTexture.active = target;
                readback.ReadPixels(new Rect(0,0,1920,1080),0,0); readback.Apply();
                var pixels = readback.GetPixels();
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
                image.SetPixels(pixels); image.Apply();
                File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(readback); Object.DestroyImmediate(image);
            }
        }
    }
}
