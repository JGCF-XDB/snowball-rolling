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
    // An isolated art review stage. It never writes the Match scene or gameplay prefabs.
    public static class ConceptAssetStudio
    {
        private const string Output = "TestResults/ConceptMatch/Studies";

        public static void Render()
        {
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f,0.67f,0.74f);
            RenderSettings.fog = false;
            var key = new GameObject("Studio key").AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 2.0f; key.color = Color.white;
            key.shadows = LightShadows.Soft; key.shadowStrength = 0.65f;
            key.shadowBias = 0.025f; key.shadowNormalBias = 0.10f;
            key.transform.rotation = Quaternion.Euler(42,-35,0);
            var fill = new GameObject("Studio fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.25f; fill.color = new Color(0.68f,0.81f,1);
            fill.transform.rotation = Quaternion.Euler(25,145,0); fill.shadows = LightShadows.None;
            var camera = new GameObject("Studio camera").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.farClipPlane = 200;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.78f,0.82f,0.88f);
            camera.allowHDR = true;
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.antialiasingQuality = AntialiasingQuality.High;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var volume = new GameObject("Studio exposure").AddComponent<Volume>(); volume.isGlobal = true; volume.sharedProfile = profile;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.localScale = Vector3.one * 10;
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.SetColor("_BaseColor",new Color(0.66f,0.71f,0.78f)); floorMaterial.SetFloat("_Smoothness",0.05f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;

            var snow = ModelingReferenceBuilder.Surface("StudySnow",0.10f,0.5f);
            var packed = ModelingReferenceBuilder.Surface("StudyPackedSnow",0.18f,0.4f);
            var ice = ModelingReferenceBuilder.Surface("StudyIce",0.7f,0,0,true);
            var rock = ModelingReferenceBuilder.Surface("StudyRock",0.08f);
            var core = ModelingReferenceBuilder.Surface("StudyIceCore",0.45f);
            var foliage = ModelingReferenceBuilder.Surface("StudyFoliage",0.07f);
            var bark = ModelingReferenceBuilder.Surface("StudyBark",0.08f);
            var platform = Item(ReferenceMeshes.Import("ConceptPlatform","StudyPlatform",4),new[] {snow,ice,rock,core});
            floor.transform.position = Vector3.down * 7;
            Aim(camera,new Vector3(26,15,-33),new Vector3(0,-2,0),10.5f);
            ModelingReferenceBuilder.Capture(camera,Output+"/platform-perspective.png");
            Aim(camera,new Vector3(0,-2,-45),new Vector3(0,-2,0),7.2f);
            ModelingReferenceBuilder.Capture(camera,Output+"/platform-section.png");
            platform.SetActive(false);
            floor.transform.position = Vector3.zero;
            var ballMesh = ReferenceMeshes.Import("ConceptSnowball","StudySnowball",2);
            var ball = Item(ballMesh,new[] {snow,packed});
            ball.transform.SetPositionAndRotation(Vector3.up*0.5f,Quaternion.Euler(17,30,0));
            Aim(camera,new Vector3(2.4f,1.9f,-3.7f),Vector3.up*0.5f,0.85f);
            ModelingReferenceBuilder.Capture(camera,Output+"/snowball.png");
            ball.SetActive(false);
            var cedar = Item(ReferenceMeshes.Import("ConceptCedar0","StudyCedar",3),new[] {snow,foliage,bark});
            Aim(camera,new Vector3(6,4.2f,-8),Vector3.up*1.8f,2.55f);
            ModelingReferenceBuilder.Capture(camera,Output+"/cedar.png"); cedar.SetActive(false);
            var mountain = Item(ReferenceMeshes.Import("ConceptMountain0","StudyMountain",2),new[] {snow,rock});
            mountain.transform.localScale = new Vector3(7,7,7);
            Aim(camera,new Vector3(14,9,-18),new Vector3(0,2.4f,0),5.8f);
            ModelingReferenceBuilder.Capture(camera,Output+"/mountain.png");
            mountain.SetActive(false);
            var mountainRock = ModelingReferenceBuilder.Surface("StudyMountainRock",0.08f);
            var mountainSnow = ModelingReferenceBuilder.Surface("StudyPeakSnow",0.10f,0.25f);
            mountain.GetComponent<MeshRenderer>().sharedMaterials = new[] {mountainSnow,mountainRock};
            floor.SetActive(false); fill.enabled = false;
            key.gameObject.name = "Sun";
            var cedars = new[] {cedar, Item(ReferenceMeshes.Import("ConceptCedar1","StudyCedar1",3),new[] {snow,foliage,bark})};
            var islands = new GameObject[3];
            var peaks = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                islands[i] = Item(ReferenceMeshes.Import("ConceptIslet"+i,"StudyIslet"+i,4),new[] {snow,ice,rock,core});
                islands[i].SetActive(false);
                peaks[i] = i == 0 ? mountain : Item(ReferenceMeshes.Import("ConceptMountain"+i,"StudyMountain"+i,2),new[] {mountainSnow,mountainRock});
                peaks[i].SetActive(false);
            }
            cedars[1].SetActive(false);
            // The mountain was enlarged for its single-asset portrait; templates use unit scale.
            mountain.transform.localScale = Vector3.one;
            var cliff = Item(ReferenceMeshes.Import("ConceptCliff","StudyCliff",4),new[] {snow,ice,rock,core}); cliff.SetActive(false);
            var environment = new GameObject("Composition workbench");
            ModelingReferenceBuilder.Environment(environment.transform,cedars,islands,peaks,cliff);
            ModelingReferenceBuilder.LightingAndCamera(environment.transform);
            var framing = camera.GetComponent<ArenaCamera>(); framing.enabled = false;
            platform.SetActive(true);
            var arenaSnow = ModelingReferenceBuilder.Surface("StudyArenaSnow",0.10f,1,1);
            platform.GetComponent<MeshRenderer>().sharedMaterials = new[] {arenaSnow,ice,rock,core};
            ModelingReferenceBuilder.Capture(camera,Output+"/composition-workbench.png");
            var identityOutline = new Material(Shader.Find("SnowArena/IdentityOutline"));
            identityOutline.SetFloat("_OutlineWidth",0.02f);
            var shell = Object.Instantiate(ballMesh); var allTriangles = ballMesh.triangles;
            shell.subMeshCount = 1; shell.SetTriangles(allTriangles,0);
            var actorPositions = new[] {new Vector3(0,0.85f,-4),new Vector3(-4,0.85f,0),new Vector3(4,0.85f,1),
                new Vector3(0,0.85f,5),new Vector3(5,0.85f,-4),new Vector3(-5,0.85f,4)};
            for (int i = 0; i < actorPositions.Length; i++)
            {
                var actor = Item(ballMesh,new[] {snow,packed});
                actor.transform.SetPositionAndRotation(actorPositions[i],Quaternion.Euler(i*23,i*47,0));
                actor.transform.localScale = Vector3.one*1.7f;
                var outline = Item(shell,new[] {identityOutline});
                outline.transform.SetParent(actor.transform,false);
                outline.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                var appearance = actor.AddComponent<SnowballAppearance>();
                appearance.snowRenderer = actor.GetComponent<MeshRenderer>();
                appearance.outlineRenderer = outline.GetComponent<MeshRenderer>();
                appearance.SetIdentity(i);
            }
            ModelingReferenceBuilder.Capture(camera,Output+"/composition-identities.png");
            AssetDatabase.SaveAssets();
            Debug.Log("CONCEPT STUDIO: Isolated platform, ball, cedar and mountain studies rendered.");
        }

        private static GameObject Item(Mesh mesh,Material[] materials)
        {
            var item = new GameObject(mesh.name);
            item.AddComponent<MeshFilter>().sharedMesh=mesh;
            item.AddComponent<MeshRenderer>().sharedMaterials=materials;
            return item;
        }

        private static void Aim(Camera camera,Vector3 position,Vector3 target,float size)
        {
            camera.transform.position=position; camera.transform.LookAt(target); camera.orthographicSize=size;
        }
    }
}
