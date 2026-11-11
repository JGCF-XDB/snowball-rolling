using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnowballArena.Editor
{
    /// <summary>
    /// Authors the shared materials for the snowball explosion into Resources, so both the
    /// game build and the review renderer use the same assets and the custom shader survives
    /// player stripping. Safe to run repeatedly: existing assets are updated in place.
    /// </summary>
    public static class SnowExplosionAssets
    {
        private const string ResourceFolder = PrototypeBuilder.Root + "/Resources";
        private const string DustPath = ResourceFolder + "/SnowExplosionDust.mat";
        private const string GlowPath = ResourceFolder + "/SnowExplosionGlow.mat";
        private const string SnowPath = ResourceFolder + "/SnowExplosionSnow.mat";
        private const string IcePath = ResourceFolder + "/SnowExplosionIce.mat";

        [MenuItem("Snowball Arena/Build Snowball Explosion Assets")]
        public static void Ensure()
        {
            if (!AssetDatabase.IsValidFolder(ResourceFolder))
                AssetDatabase.CreateFolder(PrototypeBuilder.Root, "Resources");
            var shader = Shader.Find("SnowArena/SnowExplosion");
            if (shader == null) throw new System.InvalidOperationException("SnowArena/SnowExplosion shader is missing.");
            RequireNoShaderErrors(shader);

            // Powder puffs: classic alpha blend, soft circular sprite.
            var dust = Save(DustPath, shader);
            dust.SetColor("_Color", new Color(0.95f, 0.98f, 1f, 1f));
            dust.SetFloat("_Softness", 0.95f);
            dust.SetFloat("_Power", 1.15f);
            dust.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            dust.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            dust.SetFloat("_Cull", 0f);
            dust.renderQueue = 3000;

            // Flash, shell, shockwave and ice glints: additive light on top of the snow.
            var glow = Save(GlowPath, shader);
            glow.SetColor("_Color", new Color(0.78f, 0.91f, 1f, 1f));
            glow.SetFloat("_Softness", 0.92f);
            glow.SetFloat("_Power", 1.35f);
            glow.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glow.SetFloat("_DstBlend", (float)BlendMode.One);
            glow.SetFloat("_Cull", 0f);
            glow.renderQueue = 3050;

            // Lit debris keeps the low-poly snow look of the real snowballs.
            var snowShader = Shader.Find("Universal Render Pipeline/Lit");
            if (snowShader == null) throw new System.InvalidOperationException("URP Lit shader is missing.");
            var snow = Save(SnowPath, snowShader);
            snow.SetColor("_BaseColor", new Color(0.96f, 0.98f, 1f, 1f));
            snow.SetFloat("_Smoothness", 0.18f);
            snow.SetFloat("_Metallic", 0f);
            var ice = Save(IcePath, snowShader);
            ice.SetColor("_BaseColor", new Color(0.42f, 0.78f, 0.92f, 1f));
            ice.SetFloat("_Smoothness", 0.46f);
            ice.SetFloat("_Metallic", 0.04f);

            EditorUtility.SetDirty(dust); EditorUtility.SetDirty(glow);
            EditorUtility.SetDirty(snow); EditorUtility.SetDirty(ice);
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("SNOWBALL EXPLOSION ASSETS: dust, glow, snow and ice materials are ready in Resources.");
        }

        public static void Validate()
        {
            var shader = Shader.Find("SnowArena/SnowExplosion");
            if (shader == null) throw new System.InvalidOperationException("SnowArena/SnowExplosion shader is missing.");
            RequireNoShaderErrors(shader);
            foreach (var path in new[] { DustPath, GlowPath, SnowPath, IcePath })
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) throw new System.InvalidOperationException("Missing material: " + path);
                if (material.shader == null) throw new System.InvalidOperationException("Material has no shader: " + path);
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        throw new System.InvalidOperationException(path + ": " + message.message);
            }
            var dust = AssetDatabase.LoadAssetAtPath<Material>(DustPath);
            if (dust.GetFloat("_DstBlend") != (float)BlendMode.OneMinusSrcAlpha)
                throw new System.InvalidOperationException("Dust material is not alpha blended.");
            var glow = AssetDatabase.LoadAssetAtPath<Material>(GlowPath);
            if (glow.GetFloat("_DstBlend") != (float)BlendMode.One)
                throw new System.InvalidOperationException("Glow material is not additive.");
            Directory.CreateDirectory("TestResults/SnowballExplosion");
            File.WriteAllLines("TestResults/SnowballExplosion/assets.txt", new[]
            {
                "PASS SnowArena/SnowExplosion shader compiles without errors",
                "PASS Resources/SnowExplosionDust.mat (alpha blended powder)",
                "PASS Resources/SnowExplosionGlow.mat (additive flash, shell, shockwave, glints)",
                "PASS Resources/SnowExplosionSnow.mat (lit snow debris)",
                "PASS Resources/SnowExplosionIce.mat (lit ice shards)"
            });
        }

        private static void RequireNoShaderErrors(Shader shader)
        {
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    throw new System.InvalidOperationException("Snow explosion shader: " + message.message);
        }

        private static Material Save(string path, Shader shader)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = shader;
                return existing;
            }
            var material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return material;
        }
    }
}
