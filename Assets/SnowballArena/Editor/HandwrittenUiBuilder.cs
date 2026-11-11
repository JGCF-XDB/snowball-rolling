using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SnowballArena.Editor
{
    public static class HandwrittenUiBuilder
    {
        [MenuItem("Snowball Arena/Prepare Handwritten UI and Build")]
        public static void Build()
        {
            Prepare();
            MatchSceneBuilder.BuildWindowsGame();
            string notices = "Builds/Windows/ThirdPartyNotices";
            Directory.CreateDirectory(notices);
            File.Copy("Assets/Resources/UI/OFL.txt", notices + "/LXGWMarkerGothic-OFL.txt", true);
            File.Copy("Assets/Resources/UI/AUTHORS.txt", notices + "/LXGWMarkerGothic-AUTHORS.txt", true);
        }

        public static void Prepare()
        {
            AssetDatabase.Refresh();
            PlayerSettings.productName = MatchController.GameTitle;
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/UI/LXGWMarkerGothic-Regular.ttf");
            if (font == null) throw new InvalidOperationException("Bundled handwritten font is missing.");
            foreach (char character in MatchController.GameTitle)
                if (!font.HasCharacter(character)) throw new InvalidOperationException("Font is missing " + character);
            ConfigureTexture("Assets/Resources/UI/GameTitle.png", true);
            foreach (string sheet in new[] { "Headers", "Buttons", "Lessons", "Copy", "Numbers", "OutlinedNumbers" })
                ConfigureTexture("Assets/Resources/UI/Handwriting/" + sheet + ".png", true);
            if (new HandwrittenTextAtlas().GlyphCount < 51)
                throw new InvalidOperationException("Handwritten UI lettering is incomplete.");
            foreach (string image in new[] { "Movement", "Cannon", "Victory" })
                ConfigureTexture("Assets/Resources/Tutorial/" + image + ".png", false);
            AssetDatabase.SaveAssets();
        }

        private static void ConfigureTexture(string path, bool alpha)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("UI image is missing: " + path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = alpha;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = alpha ? 4096 : 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
