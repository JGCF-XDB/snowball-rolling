using UnityEditor;
using UnityEngine;

namespace SnowballArena.Editor
{
    public static class CannonBuilder
    {
        [MenuItem("Snowball Arena/Build Cannon Game")]
        public static void Build()
        {
            const string folder = PrototypeBuilder.Root + "/Resources";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(PrototypeBuilder.Root, "Resources");
            const string path = folder + "/CannonSignal.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
            {
                // A referenced Resources asset keeps the unlit shader in player builds.
                var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "CannonSignal" };
                material.SetColor("_BaseColor", new Color(1, 0.055f, 0.025f));
                AssetDatabase.CreateAsset(material, path);
            }
            AssetDatabase.SaveAssets();
            MatchSceneBuilder.BuildGame();
        }
    }
}
