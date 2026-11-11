using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SnowballArena.Editor
{
    /// <summary>
    /// Renders the cannonball-versus-snowball explosion review from the real Match scene.
    ///
    /// The current behaviour and the three new styles (A 雪雾爆裂 / B 冰晶碎裂 / C 雪崩冲击)
    /// are staged with the real snowball and cannonball assets, stepped with an explicit clock
    /// and captured as PNG frame sequences. No scene is saved. Frames are later encoded by
    /// Tools/Art/present_snowball_explosion.py.
    /// </summary>
    public static class SnowballExplosionPreview
    {
        private const string Output = "docs/art/cannon-snowball-explosion";
        private const string SmokeOutput = "Previews/snowball-explosion-smoke";
        private const float PreviewRadius = 1.30f;
        private const float BallPrefabRadius = 0.85f;
        private const int GameplayWidth = 1920;
        private const int GameplayHeight = 1080;
        private const int CloseupWidth = 1280;
        private const int CloseupHeight = 720;

        private sealed class Clip
        {
            public string id;
            public string label;
            public SnowballExplosionStyle? style; // null renders today's instant disappearance
            public bool closeup;
            public int width, height;
            public int preroll, frames;
            public float frameSeconds;
            public bool still;
        }

        [MenuItem("Snowball Arena/Preview Snowball Explosion")]
        public static void Render()
        {
            SnowExplosionAssets.Ensure();
            SnowExplosionAssets.Validate();
            Validate();
            var clips = BuildClips();
            Run(clips);
        }

        [MenuItem("Snowball Arena/Preview Snowball Explosion (smoke)")]
        public static void Smoke()
        {
            SnowExplosionAssets.Ensure();
            RunSmoke();
        }

        [MenuItem("Snowball Arena/Validate Snowball Explosion")]
        public static void ValidateMenu() => Validate();

        // ------------------------------------------------------------------ clips

        private static List<Clip> BuildClips()
        {
            var clips = new List<Clip>
            {
                new Clip { id = "gameplay-current", label = "现状：命中即消失", style = null, width = GameplayWidth,
                    height = GameplayHeight, preroll = 12, frames = 76, frameSeconds = 1f / 60f, still = true },
                new Clip { id = "gameplay-a-snow-powder", label = "A 雪雾爆裂", style = SnowballExplosionStyle.SnowPowder,
                    width = GameplayWidth, height = GameplayHeight, preroll = 12, frames = 76, frameSeconds = 1f / 60f, still = true },
                new Clip { id = "gameplay-b-ice-shatter", label = "B 冰晶碎裂", style = SnowballExplosionStyle.IceShatter,
                    width = GameplayWidth, height = GameplayHeight, preroll = 12, frames = 76, frameSeconds = 1f / 60f, still = true },
                new Clip { id = "gameplay-c-avalanche-blast", label = "C 雪崩冲击", style = SnowballExplosionStyle.AvalancheBlast,
                    width = GameplayWidth, height = GameplayHeight, preroll = 12, frames = 76, frameSeconds = 1f / 60f, still = true },
                new Clip { id = "closeup-a-snow-powder", label = "A 雪雾爆裂 · 慢放近景", style = SnowballExplosionStyle.SnowPowder,
                    closeup = true, width = CloseupWidth, height = CloseupHeight, preroll = 14, frames = 108, frameSeconds = 1f / 120f },
                new Clip { id = "closeup-b-ice-shatter", label = "B 冰晶碎裂 · 慢放近景", style = SnowballExplosionStyle.IceShatter,
                    closeup = true, width = CloseupWidth, height = CloseupHeight, preroll = 14, frames = 108, frameSeconds = 1f / 120f },
                new Clip { id = "closeup-c-avalanche-blast", label = "C 雪崩冲击 · 慢放近景", style = SnowballExplosionStyle.AvalancheBlast,
                    closeup = true, width = CloseupWidth, height = CloseupHeight, preroll = 14, frames = 108, frameSeconds = 1f / 120f }
            };
            return clips;
        }

        // ------------------------------------------------------------------ main render

        private static void Run(List<Clip> clips)
        {
            var frameRoot = Output + "/frames";
            EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            Directory.CreateDirectory(Output);
            if (Directory.Exists(frameRoot)) Directory.Delete(frameRoot, true);
            Directory.CreateDirectory(frameRoot);

            var match = Object.FindFirstObjectByType<MatchController>();
            if (match == null || match.snowballPrefab == null) throw new InvalidOperationException("Match scene is not ready.");
            var art = Resources.Load<CannonArt>("CannonArt");
            if (art == null || art.projectilePrefab == null || art.impactPrefab == null)
                throw new InvalidOperationException("Cannon resources are missing. Run Rebuild Selected Cannon B.");
            var view = Camera.main;
            if (view == null) throw new InvalidOperationException("The match scene has no Main Camera.");
            var framing = view.GetComponent<ArenaCamera>();
            var capture = framing != null ? framing.RenderRoot : view;
            if (framing != null) framing.enabled = false;
            SnowballExplosion.BillboardCamera = view;

            Vector3 originalPosition = view.transform.position;
            Quaternion originalRotation = view.transform.rotation;
            float originalFov = view.fieldOfView;
            float originalAspect = view.aspect;
            float captureAspect = capture.aspect;

            var stage = new GameObject("Snowball explosion review - temporary");
            var report = new StringBuilder();
            report.AppendLine("Cannon → snowball explosion review render");
            report.AppendLine("Scene: " + MatchSceneBuilder.MatchPath);
            report.AppendLine("Projection: real 60-degree match camera (perspective), background camera stack captured.");
            report.AppendLine("Ball radius: " + PreviewRadius.ToString("F2") + " m (grown mid-match ball; effect parameters scale with radius).");
            try
            {
                if (!Physics.Raycast(new Vector3(0f, 30f, 0f), Vector3.down, out var groundHit, 80f, 1 << 8))
                    throw new InvalidOperationException("Cannot find the snow platform surface.");
                Vector3 ground = groundHit.point;
                Vector3 center = ground + Vector3.up * PreviewRadius;
                // A shot coming from screen-right and slightly in front of the ball, so the
                // directional style reads clearly instead of pointing straight at the camera.
                Vector3 direction = new Vector3(-0.92f, -0.06f, 0.39f).normalized;

                var ball = Object.Instantiate(match.snowballPrefab, stage.transform);
                ball.name = "Review snowball";
                ball.transform.position = center;
                ball.transform.rotation = Quaternion.identity;
                ball.transform.localScale = Vector3.one * (PreviewRadius / BallPrefabRadius);
                var appearance = ball.GetComponent<SnowballAppearance>();
                Color identity = Color.white;
                if (appearance != null)
                {
                    appearance.SetIdentity(0);
                    if (appearance.outlineRenderer != null && appearance.outlineRenderer.sharedMaterial != null)
                        identity = appearance.outlineRenderer.sharedMaterial.GetColor("_OutlineColor");
                }

                var projectile = Object.Instantiate(art.projectilePrefab, stage.transform);
                projectile.name = "Review cannonball";
                projectile.transform.localScale = Vector3.one * (0.30f / Mathf.Max(0.01f, art.projectileRadius));
                Vector3 projectileStart = center - direction * (PreviewRadius + 3.2f);
                Vector3 projectileContact = center - direction * (PreviewRadius + 0.30f);

                var burstObject = Object.Instantiate(art.impactPrefab, stage.transform);
                burstObject.name = "Current impact burst";
                burstObject.transform.position = center - direction * PreviewRadius;
                burstObject.transform.rotation = Quaternion.LookRotation(direction);
                burstObject.transform.localScale = Vector3.one * (0.30f / Mathf.Max(0.01f, art.projectileRadius));
                var burst = burstObject.GetComponent<CannonBurst>();
                Transform[] fragments = burst != null ? burst.fragments : new Transform[0];
                var fragmentScales = new Vector3[fragments.Length];
                for (int i = 0; i < fragments.Length; i++) fragmentScales[i] = fragments[i].localScale;

                report.AppendLine("Impact point: " + (center - direction * PreviewRadius).ToString("F2"));
                report.AppendLine("Shot direction: " + direction.ToString("F2"));
                report.AppendLine();
                report.AppendLine("clip,style,resolution,fps,preroll,effect_frames,duration_s");
                foreach (var clip in clips)
                {
                    string folder = frameRoot + "/" + clip.id;
                    Directory.CreateDirectory(folder);
                    ConfigureCamera(view, capture, clip, center, originalPosition, originalRotation);
                    view.aspect = clip.width / (float)clip.height;
                    capture.aspect = view.aspect;

                    ball.SetActive(true);
                    projectile.SetActive(true);
                    burstObject.SetActive(false);
                    float burstAge = 0f;
                    SnowballExplosion effect = null;
                    Vector3 baseViewPosition = view.transform.position;
                    var target = new RenderTexture(clip.width, clip.height, 24) { name = "Explosion review capture" };
                    var image = new Texture2D(clip.width, clip.height, TextureFormat.RGB24, false);
                    int total = clip.preroll + clip.frames;
                    try
                    {
                        for (int frame = 0; frame < total; frame++)
                        {
                            if (frame < clip.preroll)
                            {
                                float t = clip.preroll <= 1 ? 1f : frame / (float)(clip.preroll - 1);
                                projectile.transform.position = Vector3.Lerp(projectileStart, projectileContact, t);
                                projectile.transform.rotation = Quaternion.LookRotation(direction);
                                view.transform.position = baseViewPosition;
                            }
                            else
                            {
                                if (frame == clip.preroll)
                                {
                                    ball.SetActive(false);
                                    projectile.SetActive(false);
                                    if (clip.style == null)
                                    {
                                        burstObject.SetActive(true);
                                        ApplyCurrentBurst(fragments, fragmentScales, 0f);
                                    }
                                    else
                                    {
                                        effect = SnowballExplosion.Play(center, PreviewRadius, direction, clip.style.Value,
                                            identity, stage.transform, true, SeedFor(clip.style.Value));
                                        if (effect == null)
                                            throw new InvalidOperationException("Snowball explosion shader/materials are missing.");
                                    }
                                }
                                else if (effect != null) effect.Advance(clip.frameSeconds);
                                else
                                {
                                    burstAge += clip.frameSeconds;
                                    ApplyCurrentBurst(fragments, fragmentScales, burstAge);
                                }
                                ApplyShake(view, baseViewPosition, clip, (frame - clip.preroll) * clip.frameSeconds);
                            }

                            string path = Path.Combine(folder, "frame_" + frame.ToString("0000") + ".png");
                            RenderFrame(capture, target, image, path);
                            if (clip.still && frame == clip.preroll + 13)
                                File.Copy(path, Path.Combine(Output, clip.id + "-still.png"), true);
                        }
                        report.AppendLine($"{clip.id},{(clip.style == null ? "current" : clip.style.ToString())},{clip.width}x{clip.height}," +
                            $"{(clip.closeup ? 30 : 60)},{clip.preroll},{clip.frames},{(clip.frames * clip.frameSeconds):F2}");
                    }
                    finally
                    {
                        view.transform.position = baseViewPosition;
                        if (effect != null) SnowballExplosion.ClearAll();
                        RenderTexture.active = null;
                        target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
                    }
                }

                report.AppendLine();
                report.AppendLine("Effect layer budgets:");
                foreach (SnowballExplosionStyle style in Enum.GetValues(typeof(SnowballExplosionStyle)))
                {
                    var sample = SnowballExplosion.Play(center, PreviewRadius, direction, style, identity, null, true, SeedFor(style));
                    if (sample != null)
                    {
                        report.AppendLine($"  {style}: puffs={sample.PuffCount} chunks={sample.ChunkCount} glints={sample.GlintCount} " +
                            $"lifetime={sample.Lifetime:F2}s influence={sample.InfluenceRadius:F2}m");
                    }
                    SnowballExplosion.ClearAll();
                }
                report.AppendLine();
                report.AppendLine("Still frames: gameplay-*.png (captured 13 frames / 0.22 s after impact, when the debris cloud is at its widest).");
                report.AppendLine("Slow-motion closeups: rendered at 120 fps, encoded at 30 fps (4x slower than gameplay).");
                report.AppendLine("Current behaviour footage uses the existing CannonBurst with its original 0.30 s timing.");
                File.WriteAllText(Path.Combine(Output, "render-report.txt"), report.ToString());
                WriteClipManifest(clips);
                Debug.Log("SNOWBALL EXPLOSION REVIEW: " + Path.GetFullPath(Output));
            }
            finally
            {
                view.transform.position = originalPosition;
                view.transform.rotation = originalRotation;
                view.fieldOfView = originalFov;
                view.aspect = originalAspect;
                capture.aspect = captureAspect;
                SnowballExplosion.BillboardCamera = null;
                SnowballExplosion.ClearAll();
                Object.DestroyImmediate(stage);
            }
        }

        // ------------------------------------------------------------------ smoke

        private static void RunSmoke()
        {
            EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            Directory.CreateDirectory(SmokeOutput);
            var match = Object.FindFirstObjectByType<MatchController>();
            var art = Resources.Load<CannonArt>("CannonArt");
            var view = Camera.main;
            var framing = view.GetComponent<ArenaCamera>();
            var capture = framing != null ? framing.RenderRoot : view;
            if (framing != null) framing.enabled = false;
            SnowballExplosion.BillboardCamera = view;
            Vector3 originalPosition = view.transform.position;
            Quaternion originalRotation = view.transform.rotation;
            float originalFov = view.fieldOfView;
            float originalAspect = view.aspect;
            float captureAspect = capture.aspect;
            var stage = new GameObject("Explosion smoke - temporary");
            try
            {
                if (!Physics.Raycast(new Vector3(0f, 30f, 0f), Vector3.down, out var groundHit, 80f, 1 << 8))
                    throw new InvalidOperationException("Cannot find the snow platform surface.");
                Vector3 center = groundHit.point + Vector3.up * PreviewRadius;
                Vector3 direction = new Vector3(-0.92f, -0.06f, 0.39f).normalized;
                var ball = Object.Instantiate(match.snowballPrefab, stage.transform);
                ball.transform.position = center;
                ball.transform.localScale = Vector3.one * (PreviewRadius / BallPrefabRadius);
                var appearance = ball.GetComponent<SnowballAppearance>();
                Color identity = Color.white;
                if (appearance != null)
                {
                    appearance.SetIdentity(0);
                    if (appearance.outlineRenderer != null && appearance.outlineRenderer.sharedMaterial != null)
                        identity = appearance.outlineRenderer.sharedMaterial.GetColor("_OutlineColor");
                }
                view.aspect = 1280f / 720f; capture.aspect = view.aspect;
                view.transform.position = originalPosition; view.transform.rotation = originalRotation;
                view.fieldOfView = originalFov;

                var target = new RenderTexture(1280, 720, 24);
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                try
                {
                    foreach (SnowballExplosionStyle style in Enum.GetValues(typeof(SnowballExplosionStyle)))
                    {
                        ball.SetActive(false);
                        var effect = SnowballExplosion.Play(center, PreviewRadius, direction, style, identity, stage.transform, true, SeedFor(style));
                        if (effect == null) throw new InvalidOperationException("Snowball explosion assets are missing.");
                        for (int step = 0; step < 4; step++) effect.Advance(1f / 60f);
                        RenderFrame(capture, target, image, Path.Combine(SmokeOutput, style + ".png"));
                        SnowballExplosion.ClearAll();
                        ball.SetActive(true);
                    }

                    // Closeup inspection of the recommended style at flash, spread and settle.
                    float distance = PreviewRadius * 4.6f;
                    Vector3 closePosition = center + new Vector3(-distance * 0.62f, distance * 0.52f, -distance * 0.86f);
                    view.transform.position = closePosition;
                    view.transform.rotation = Quaternion.LookRotation((center + Vector3.up * 0.1f) - closePosition);
                    view.fieldOfView = 40f;
                    foreach (int steps in new[] { 2, 6, 16, 40 })
                    {
                        ball.SetActive(false);
                        var effect = SnowballExplosion.Play(center, PreviewRadius, direction, SnowballExplosionStyle.IceShatter,
                            identity, stage.transform, true, SeedFor(SnowballExplosionStyle.IceShatter));
                        for (int step = 0; step < steps; step++) effect.Advance(1f / 60f);
                        RenderFrame(capture, target, image, Path.Combine(SmokeOutput, "closeup-" + steps + ".png"));
                        SnowballExplosion.ClearAll();
                        ball.SetActive(true);
                    }
                }
                finally
                {
                    RenderTexture.active = null;
                    target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
                }
                Debug.Log("SNOWBALL EXPLOSION SMOKE: " + Path.GetFullPath(SmokeOutput));
            }
            finally
            {
                view.transform.position = originalPosition; view.transform.rotation = originalRotation;
                view.fieldOfView = originalFov; view.aspect = originalAspect; capture.aspect = captureAspect;
                SnowballExplosion.BillboardCamera = null;
                SnowballExplosion.ClearAll();
                Object.DestroyImmediate(stage);
            }
        }

        // ------------------------------------------------------------------ validation

        public static void Validate()
        {
            SnowExplosionAssets.Ensure();
            var lines = new List<string>();
            var direction = new Vector3(-0.92f, -0.06f, 0.39f).normalized;
            bool all = true;
            foreach (SnowballExplosionStyle style in Enum.GetValues(typeof(SnowballExplosionStyle)))
            {
                var first = SnowballExplosion.Play(Vector3.zero, PreviewRadius, direction, style, Color.red, null, true, SeedFor(style));
                if (first == null) { all = false; lines.Add("FAIL " + style + ": effect was not created"); continue; }
                for (int step = 0; step < 12; step++) first.Advance(1f / 60f);
                float puffSum = first.PuffChecksum, chunkSum = first.ChunkChecksum;
                float influence = first.InfluenceRadius;
                int puffCount = first.PuffCount, chunkCount = first.ChunkCount;
                int colliders = first.GetComponentsInChildren<Collider>(true).Length;
                float lifetime = first.Lifetime;
                SnowballExplosion.ClearAll();

                var second = SnowballExplosion.Play(Vector3.zero, PreviewRadius, direction, style, Color.red, null, true, SeedFor(style));
                for (int step = 0; step < 12; step++) second.Advance(1f / 60f);
                bool deterministic = Mathf.Abs(second.PuffChecksum - puffSum) < 0.001f && Mathf.Abs(second.ChunkChecksum - chunkSum) < 0.001f;
                SnowballExplosion.ClearAll();

                bool pass = deterministic && colliders == 0 && puffCount > 30 && chunkCount >= 10
                    && lifetime >= 0.85f && lifetime <= 1.2f && influence > PreviewRadius * 2.4f;
                all &= pass;
                lines.Add((pass ? "PASS " : "FAIL ") + $"{style}: puffs={puffCount} chunks={chunkCount} lifetime={lifetime:F2}s " +
                    $"influence={influence:F2}m colliders={colliders} deterministic={(deterministic ? "yes" : "no")}");
            }
            var small = SnowballExplosion.Play(Vector3.zero, BallPrefabRadius, direction, SnowballExplosionStyle.IceShatter, Color.red, null, true, 5);
            var large = SnowballExplosion.Play(Vector3.zero, 2.40f, direction, SnowballExplosionStyle.IceShatter, Color.red, null, true, 5);
            bool scales = small != null && large != null && large.InfluenceRadius > small.InfluenceRadius * 2.4f;
            all &= scales;
            lines.Add((scales ? "PASS " : "FAIL ") + "influence scales with ball radius: " +
                (small != null ? small.InfluenceRadius.ToString("F2") : "?") + " m @0.85 m vs " +
                (large != null ? large.InfluenceRadius.ToString("F2") : "?") + " m @2.40 m");
            SnowballExplosion.ClearAll();
            bool cleaned = SnowballExplosion.Active.Count == 0;
            all &= cleaned;
            lines.Add((cleaned ? "PASS " : "FAIL ") + "ClearAll removes every live explosion");
            lines.Add("Effect is visual-only: no colliders, no rigidbodies, no gameplay rule changes.");
            Directory.CreateDirectory("TestResults/SnowballExplosion");
            File.WriteAllLines("TestResults/SnowballExplosion/validation.txt", lines);
            if (!all)
            {
                File.WriteAllLines("TestResults/SnowballExplosion/validation.txt", lines);
                throw new InvalidOperationException("Snowball explosion validation failed.");
            }
            Debug.Log("SNOWBALL EXPLOSION VALIDATION: " + string.Join(" | ", lines));
        }

        // ------------------------------------------------------------------ helpers

        private static void WriteClipManifest(List<Clip> clips)
        {
            var json = new StringBuilder();
            json.AppendLine("[");
            for (int i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                int fps = clip.closeup ? 30 : 60;
                json.AppendLine("  {");
                json.AppendLine("    \"id\": \"" + clip.id + "\",");
                json.AppendLine("    \"label\": \"" + clip.label + "\",");
                json.AppendLine("    \"style\": \"" + (clip.style == null ? "current" : clip.style.ToString()) + "\",");
                json.AppendLine("    \"camera\": \"" + (clip.closeup ? "closeup" : "gameplay") + "\",");
                json.AppendLine("    \"width\": " + clip.width + ",");
                json.AppendLine("    \"height\": " + clip.height + ",");
                json.AppendLine("    \"fps\": " + fps + ",");
                json.AppendLine("    \"preroll_frames\": " + clip.preroll + ",");
                json.AppendLine("    \"effect_frames\": " + clip.frames + ",");
                json.AppendLine("    \"duration_seconds\": " + (clip.frames * clip.frameSeconds).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                json.AppendLine("  }" + (i == clips.Count - 1 ? "" : ","));
            }
            json.AppendLine("]");
            File.WriteAllText(Path.Combine(Output, "clips.json"), json.ToString());
        }

        private static int SeedFor(SnowballExplosionStyle style) => 90210 + (int)style * 97;

        private static void ConfigureCamera(Camera view, Camera capture, Clip clip, Vector3 center,
            Vector3 originalPosition, Quaternion originalRotation)
        {
            view.fieldOfView = 34f;
            if (!clip.closeup)
            {
                view.transform.position = originalPosition;
                view.transform.rotation = originalRotation;
                return;
            }
            float distance = PreviewRadius * 6.4f;
            Vector3 position = center + new Vector3(-distance * 0.62f, distance * 0.52f, -distance * 0.86f);
            view.transform.position = position;
            view.transform.rotation = Quaternion.LookRotation((center + Vector3.up * 0.25f) - position);
            view.fieldOfView = 38f;
        }

        private static void ApplyShake(Camera view, Vector3 basePosition, Clip clip, float age)
        {
            if (clip.style == null) return;
            float amplitude = clip.style == SnowballExplosionStyle.AvalancheBlast ? 0.12f
                : clip.style == SnowballExplosionStyle.IceShatter ? 0.09f : 0.05f;
            if (clip.closeup) amplitude *= 0.35f;
            float envelope = Mathf.Exp(-age * 7f) * Mathf.Clamp01(1f - age / 0.32f);
            if (envelope <= 0f) { view.transform.position = basePosition; return; }
            view.transform.position = basePosition + new Vector3(
                Mathf.Sin(age * 91f + 0.7f), Mathf.Cos(age * 73f) * 0.6f, 0f) * (amplitude * envelope);
        }

        private static void ApplyCurrentBurst(Transform[] fragments, Vector3[] scales, float age)
        {
            float t = Mathf.Clamp01(age / CannonBurst.Lifetime);
            for (int i = 0; i < fragments.Length; i++)
            {
                float angle = i * 2.399963f;
                var direction = new Vector3(Mathf.Cos(angle), 0.3f + i % 3 * 0.18f, Mathf.Sin(angle));
                fragments[i].localPosition = direction * (age * 2.8f) + Vector3.down * (age * age * 2f);
                fragments[i].localScale = scales[i] * (1f - t);
                fragments[i].localRotation = Quaternion.Euler(i * 31 + age * 280, i * 47, age * 160);
            }
        }

        private static void RenderFrame(Camera capture, RenderTexture target, Texture2D image, string path)
        {
            RenderPipeline.SubmitRenderRequest(capture, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
    }
}
