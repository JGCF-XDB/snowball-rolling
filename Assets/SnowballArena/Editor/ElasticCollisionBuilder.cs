using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SnowballArena.Editor
{
    public static class ElasticCollisionBuilder
    {
        public const string BallMaterialPath = PrototypeBuilder.Root + "/Materials/SnowContact.physicMaterial";
        public const string GroundMaterialPath = PrototypeBuilder.Root + "/Materials/ArenaContact.physicMaterial";

        public static void ConfigureMaterials()
        {
            Configure(BallMaterialPath, "SnowContact", 0.95f);
            Configure(GroundMaterialPath, "ArenaContact", 0.05f);
            AssetDatabase.SaveAssets();
        }

        private static void Configure(string path, string name, float restitution)
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null) { material = new PhysicsMaterial(name); AssetDatabase.CreateAsset(material, path); }
            material.dynamicFriction = 0.45f; material.staticFriction = 0.5f;
            material.frictionCombine = PhysicsMaterialCombine.Average;
            // Ball/ball uses 0.95; ball/ground uses 0.05 without extra impulses.
            material.bounciness = restitution; material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
        }

        [MenuItem("Snowball Arena/Configure Elastic Collisions and Build")]
        public static void ConfigureAndBuild()
        {
            ConfigureMaterials();
            var ground = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(GroundMaterialPath);
            string path = PrototypeBuilder.Root + "/Prefabs/CircularArena.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var collider in root.GetComponentsInChildren<Collider>()) collider.sharedMaterial = ground;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            foreach (string scenePath in new[] { PrototypeBuilder.ScenePath, MatchSceneBuilder.MatchPath })
            {
                var scene = EditorSceneManager.OpenScene(scenePath);
                foreach (var surface in Object.FindObjectsByType<ArenaSurface>(FindObjectsSortMode.None))
                    foreach (var collider in surface.GetComponentsInChildren<Collider>())
                    {
                        collider.sharedMaterial = ground;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                    }
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            ArenaExtensionValidation.Run();
            MatchSceneBuilder.BuildGame();
        }
    }
}
