#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SnowballArena
{
    // Explicit opt-in diagnostic for a standalone development build. Never active in normal play.
    internal sealed class RuntimeSmokeProbe : MonoBehaviour
    {
        private Keyboard keyboard;
        private PrototypeSession session;
        private string output;
        private float startTime;
        private int phase;
        private bool diagonal, moved, fell, reset;
        private string failure;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfRequested()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--snowball-smoke");
            if (index < 0 || index + 1 >= args.Length) return;
            var probe = new GameObject("Standalone smoke probe").AddComponent<RuntimeSmokeProbe>();
            probe.output = args[index + 1];
        }

        private void Start()
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.targetFrameRate = 60;
            Directory.CreateDirectory(output);
            keyboard = InputSystem.AddDevice<Keyboard>();
            session = FindFirstObjectByType<PrototypeSession>();
            startTime = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLog;
        }

        private void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                failure = message + "\n" + trace;
        }

        private void Update()
        {
            float elapsed = Time.realtimeSinceStartup - startTime;
            if (session == null) { Finish("Practice session missing"); return; }
            if (phase == 0 && elapsed > 1f)
            {
                StartCoroutine(Capture("practice.png"));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D));
                phase = 1;
            }
            if (phase == 1)
            {
                if (!diagonal && elapsed > 1.5f)
                {
                    Vector3 direction = session.player.GetComponent<SnowballMotor>().DesiredDirection;
                    diagonal = direction.x > 0 && direction.z > 0 && Mathf.Abs(direction.magnitude - 1f) < 0.001f;
                    if (diagonal) InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
                }
                moved |= session.player.RigidBody.position.x > 1f;
                if (session.HasFallen)
                {
                    fell = true;
                    StartCoroutine(Capture("fallen.png"));
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    startTime = Time.realtimeSinceStartup;
                    phase = 2;
                }
            }
            else if (phase == 2 && elapsed > 1f)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R)); phase = 3;
            }
            else if (phase == 3)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                reset = !session.HasFallen && Mathf.Abs(session.player.RigidBody.position.x) < 0.1f;
                if (reset) { startTime = Time.realtimeSinceStartup; phase = 4; }
            }
            else if (phase == 4 && elapsed > 1f)
            {
                Finish(failure); return;
            }
            if (elapsed > 20f) Finish("Runtime input/fall/reset sequence timed out");
        }

        private void Finish(string error)
        {
            bool passed = diagonal && moved && fell && reset && string.IsNullOrEmpty(error);
            File.WriteAllText(Path.Combine(output, "runtime-smoke.txt"),
                $"passed={passed}\ndiagonal={diagonal}\nmoved={moved}\nfell={fell}\nreset={reset}\nerror={error}\n");
            Application.logMessageReceived -= OnLog;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            Application.Quit(passed ? 0 : 1);
            enabled = false;
        }

        private IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            // Managed file I/O also supports project paths containing Chinese characters.
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null)
            {
                // Hidden Windows players may have no presentable back buffer. Render the
                // camera separately; this image intentionally excludes the OnGUI overlay.
                var camera = Camera.main;
                var previous = RenderTexture.active;
                var target = new RenderTexture(1280, 720, 24);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Destroy(target);
                filename = Path.GetFileNameWithoutExtension(filename) + "-camera.png";
            }
            File.WriteAllBytes(Path.Combine(output, filename), texture.EncodeToPNG());
            Destroy(texture);
        }
    }
}
#endif
