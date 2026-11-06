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
        private bool diagonal, moved, grew, fell, reset;
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
            if (session != null && session.practiceTargets.Length == 2 &&
                session.practiceTargets[1].Radius <= session.player.Radius * 1.4f)
                failure = "Heavy practice target did not retain its configured size at runtime";
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
                grew |= session.player.Radius > session.player.settings.initialRadius + 0.025f;
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
                reset &= Mathf.Abs(session.player.Radius - session.player.settings.initialRadius) < 0.001f;
                var growth = session.player.GetComponent<SnowballGrowth>();
                reset &= growth == null || growth.RolledDistance < 0.01d;
                for (int i = 0; i < session.practiceTargets.Length; i++)
                    reset &= Vector3.Distance(session.practiceTargets[i].RigidBody.position, session.targetSpawns[i]) < 0.1f;
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
            bool growthRequired = session != null && session.player.GetComponent<SnowballGrowth>() != null;
            bool passed = diagonal && moved && (!growthRequired || grew) && fell && reset && string.IsNullOrEmpty(error);
            File.WriteAllText(Path.Combine(output, "runtime-smoke.txt"),
                $"passed={passed}\ndiagonal={diagonal}\nmoved={moved}\ngrew={grew}\nfell={fell}\nreset={reset}\nfixedDeltaTime={Time.fixedDeltaTime}\nerror={error}\n");
            Application.logMessageReceived -= OnLog;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            Application.Quit(passed ? 0 : 1);
            enabled = false;
        }

        private IEnumerator Capture(string filename)
        {
            yield return new WaitForEndOfFrame();
            // Hidden Windows players do not expose a presentable screen buffer.
            // These diagnostic images contain the scene camera, not the OnGUI overlay.
            var camera = Camera.main;
            var previous = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            texture.Apply();
            camera.targetTexture = null;
            RenderTexture.active = previous;
            Destroy(target);
            filename = Path.GetFileNameWithoutExtension(filename) + "-camera.png";
            File.WriteAllBytes(Path.Combine(output, filename), texture.EncodeToPNG());
            Destroy(texture);
        }
    }
}
#endif
