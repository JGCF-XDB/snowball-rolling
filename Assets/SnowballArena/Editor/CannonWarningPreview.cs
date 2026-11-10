using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace SnowballArena.Editor
{
    // Renders the production warning beside the old design without saving the scene.
    public static class CannonWarningPreview
    {
        private const string Output = "docs/art/cannon-infrared-warning";

        [MenuItem("Snowball Arena/Preview Infrared Warning")]
        public static void Render()
        {
            EditorSceneManager.OpenScene(MatchSceneBuilder.MatchPath);
            Directory.CreateDirectory(Output);
            var temporary = new List<Object>();
            var root = new GameObject("Warning design review - temporary");
            temporary.Add(root);
            try
            {
                var match = Object.FindFirstObjectByType<MatchController>();
                var surface = match.surface;
                Physics.SyncTransforms();
                if (!surface.TryGetCannonLane(new Vector3(-0.86f, 0, 0.51f), out var start, out var end))
                    throw new InvalidOperationException("Cannot find the platform warning lane.");
                Vector3 direction = (end - start).normalized;
                var art = Resources.Load<CannonArt>("CannonArt");
                var cannon = Object.Instantiate(art.cannonPrefab, root.transform);
                var visuals = cannon.GetComponent<CannonVisuals>();
                cannon.transform.rotation = Quaternion.LookRotation(direction);
                cannon.transform.position = start - cannon.transform.TransformVector(visuals.muzzle.localPosition);
                visuals.SetWarning(true);

                var positions = new[] { new Vector3(-2, .85f, -4), new Vector3(-4, .85f, 4),
                    new Vector3(5, .85f, 3), new Vector3(4, .85f, -3) };
                for (int i = 0; i < positions.Length; i++)
                {
                    var ball = Object.Instantiate(match.snowballPrefab, positions[i], Quaternion.identity, root.transform);
                    ball.GetComponent<SnowballAppearance>().SetIdentity(i);
                }

                var old = new GameObject("Current 0.60 m warning").AddComponent<LineRenderer>();
                old.transform.SetParent(root.transform);
                old.sharedMaterial = Resources.Load<Material>("CannonSignal");
                old.useWorldSpace = true;
                old.positionCount = 2;
                old.widthMultiplier = .6f;
                old.SetPositions(new[] { start - Vector3.up * .75f, end - Vector3.up * .75f });

                var camera = Camera.main;
                var framing = camera.GetComponent<ArenaCamera>();
                if (framing != null) framing.enabled = false;
                camera.aspect = 16f / 9;
                Capture(camera, "01-current-overview.png");

                old.enabled = false;
                var material = Resources.Load<Material>("CannonWarning");
                if (material == null) throw new InvalidOperationException("CannonWarning material is missing.");
                foreach (var message in ShaderUtil.GetShaderMessages(material.shader))
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        throw new InvalidOperationException(message.message);
                var warning = CannonWarningVisual.Create(root.transform, surface, start, end, material);
                var laser = warning.gameObject;
                Capture(camera, "02-infrared-overview.png");

                var cameraPosition = camera.transform.position;
                var cameraRotation = camera.transform.rotation;
                float fov = camera.fieldOfView;
                Vector3 target = warning.GetPosition(0) + direction * .6f;
                camera.transform.position = target + direction * 5 + Vector3.Cross(Vector3.up, direction) * 5 + Vector3.up * 4.8f;
                camera.transform.LookAt(target);
                camera.orthographic = false; camera.fieldOfView = 46;
                Capture(camera, "03-infrared-muzzle-detail.png");
                laser.SetActive(false); old.enabled = true;
                Capture(camera, "04-current-muzzle-detail.png");
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.fieldOfView = fov;

                File.WriteAllText(Output + "/render-report.txt",
                    "Production warning rendered in the Unity scene; no scene saves.\n" +
                    $"Core width: approximately {CannonWarningVisual.Width * CannonWarningVisual.CoreFraction:F3} m; gradient ribbon width: {CannonWarningVisual.Width:F2} m.\n" +
                    "Across the width: vivid red core, two-stage soft glow fading to a transparent edge.\n" +
                    "Along the lane: low frequency energy variation, tapered ends, slow breathing and a travelling highlight.\n" +
                    $"Ground offset: {CannonWarningVisual.GroundOffset:F3} m; boundary inset: {CannonWarningVisual.BoundaryInset:F2} m; normal depth testing.\n" +
                    $"Lane start: {start}; lane end: {end}.\n");
                Debug.Log("INFRARED WARNING REVIEW: " + Path.GetFullPath(Output));
            }
            finally
            {
                for (int i = temporary.Count - 1; i >= 0; i--)
                    if (temporary[i] != null) Object.DestroyImmediate(temporary[i]);
            }
        }

        private static void Capture(Camera camera, string filename)
        {
            var framing = camera.GetComponent<ArenaCamera>();
            if (framing != null) camera = framing.RenderRoot;
            var target = new RenderTexture(1920, 1080, 24);
            var previous = RenderTexture.active;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); image.Apply();
                File.WriteAllBytes(Path.Combine(Output, filename), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image);
            }
        }
    }
}
