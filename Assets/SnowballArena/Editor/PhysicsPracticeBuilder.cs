using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SnowballArena.Editor
{
    public static class PhysicsPracticeBuilder
    {
        [MenuItem("Snowball Arena/Upgrade Growth and Collision Practice")]
        public static void Upgrade()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var session = UnityEngine.Object.FindFirstObjectByType<PrototypeSession>();
            if (session.player.GetComponent<SnowballGrowth>() == null) session.player.gameObject.AddComponent<SnowballGrowth>();
            var prefab = PrefabUtility.LoadPrefabContents(PrototypeBuilder.PrefabPath);
            try
            {
                if (prefab.GetComponent<SnowballGrowth>() == null) prefab.AddComponent<SnowballGrowth>();
                PrefabUtility.SaveAsPrefabAsset(prefab, PrototypeBuilder.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            if (session.practiceTargets.Length == 0)
            {
                var targets = new SnowballBody[2];
                var spawns = new[] { new Vector3(-3, 0.8f, 2), new Vector3(3, 1.15f, 3) };
                for (int i = 0; i < 2; i++)
                {
                    var target = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath)) as GameObject;
                    target.name = i == 0 ? "Practice Target - Equal Mass" : "Practice Target - Heavy";
                    UnityEngine.Object.DestroyImmediate(target.GetComponent<PlayerInputSource>());
                    var settings = UnityEngine.Object.Instantiate(session.player.settings);
                    settings.name = i == 0 ? "EqualTarget" : "HeavyTarget";
                    settings.initialRadius = i == 0 ? 0.65f : 1f;
                    AssetDatabase.CreateAsset(settings, PrototypeBuilder.Root + "/Configs/" + settings.name + ".asset");
                    targets[i] = target.GetComponent<SnowballBody>();
                    targets[i].settings = settings;
                    targets[i].Initialize();
                    target.transform.position = spawns[i];
                    // Targets are passive dynamic bodies, not bots. They can be pushed and roll-grow.
                    target.GetComponent<SnowballMotor>().controlEnabled = false;
                    var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(PrototypeBuilder.Root + "/Materials/Snowball.mat"));
                    material.name = "PracticeTarget" + i;
                    material.SetColor("_BaseColor", i == 0 ? new Color(0.25f, 0.82f, 0.72f) : new Color(1f, 0.64f, 0.27f));
                    AssetDatabase.CreateAsset(material, PrototypeBuilder.Root + "/Materials/" + material.name + ".mat");
                    target.GetComponentInChildren<MeshRenderer>().sharedMaterials = new[] { material, material };
                }
                session.practiceTargets = targets;
                session.targetSpawns = spawns;
            }
            // Custom MonoBehaviour fields need explicit prefab override recording;
            // otherwise Awake can restore the prefab's original small-ball settings.
            for (int i = 0; i < session.practiceTargets.Length; i++)
            {
                var body = session.practiceTargets[i];
                body.settings = AssetDatabase.LoadAssetAtPath<SnowballSettings>(PrototypeBuilder.Root + "/Configs/" + (i == 0 ? "EqualTarget" : "HeavyTarget") + ".asset");
                body.Initialize();
                var motor = body.GetComponent<SnowballMotor>();
                motor.controlEnabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(body);
                PrefabUtility.RecordPrefabInstancePropertyModifications(motor);
                EditorUtility.SetDirty(body);
                EditorUtility.SetDirty(motor);
            }
            EditorUtility.SetDirty(session);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static void VerifyAndBuild()
        {
            Upgrade();
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var practice = UnityEngine.Object.FindFirstObjectByType<PrototypeSession>();
            if (practice.practiceTargets.Length != 2 || practice.practiceTargets[1].settings.initialRadius <= practice.player.settings.initialRadius * 1.4f)
                throw new Exception("Heavy target settings did not survive scene reload");
            foreach (var target in practice.practiceTargets)
                if (target.GetComponent<PlayerInputSource>() != null || target.GetComponent<SnowballMotor>().controlEnabled)
                    throw new Exception("Practice target must remain passive after scene reload");
            PrototypeValidation.Run();
            GrowthCollisionValidation.Run();
            PrototypeBuilder.RenderPreview();
            PrototypeBuilder.BuildWindows();
        }
    }
}
