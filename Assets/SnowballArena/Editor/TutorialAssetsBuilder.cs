using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SnowballArena.Editor
{
    public static class TutorialAssetsBuilder
    {
        private const string Folder = "Assets/Resources/Tutorial";

        // Batch-only entry point keeps the user's open scene and editor untouched.
        public static void Build()
        {
            if (!Application.isBatchMode) throw new System.InvalidOperationException("Run in an isolated batch project.");
            Directory.CreateDirectory(Folder);
            EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            var camera = Camera.main;
            ModelingReferenceBuilder.Capture(camera, Folder + "/Background.png");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypeBuilder.PrefabPath);
            var winner = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            winner.transform.position = new Vector3(0, 1.3f, 0);
            winner.transform.localScale = Vector3.one * 1.5f;
            winner.GetComponent<SnowballAppearance>().SetIdentity(0);
            ModelingReferenceBuilder.Capture(camera, Folder + "/Victory.png");
            Object.DestroyImmediate(winner);

            // Bake a blurred photograph of the actual empty arena for the glass backdrop.
            var source = new Texture2D(2, 2);
            source.LoadImage(File.ReadAllBytes(Folder + "/Background.png"));
            const int width = 320, height = 180, radius = 5;
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Color sum = Color.clear;
                for (int offset = -radius; offset <= radius; offset++)
                    sum += source.GetPixelBilinear(Mathf.Clamp01((x + offset + .5f) / width), (y + .5f) / height);
                pixels[y * width + x] = sum / (radius * 2 + 1);
            }
            var blurred = new Texture2D(width, height, TextureFormat.RGB24, false);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Color sum = Color.clear;
                for (int offset = -radius; offset <= radius; offset++)
                    sum += pixels[Mathf.Clamp(y + offset, 0, height - 1) * width + x];
                blurred.SetPixel(x, y, sum / (radius * 2 + 1));
            }
            blurred.Apply();
            File.WriteAllBytes(Folder + "/Glass.png", blurred.EncodeToPNG());
            Object.DestroyImmediate(source); Object.DestroyImmediate(blurred);
            File.Delete(Folder + "/Background.png");
            AssetDatabase.Refresh();
            foreach (string name in new[] { "Movement", "Cannon", "Victory", "Glass" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Folder + "/" + name + ".png");
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            Debug.Log("TUTORIAL ASSETS: generated and imported all four images.");
            MatchSceneBuilder.BuildWindowsGame();
        }
    }
}
