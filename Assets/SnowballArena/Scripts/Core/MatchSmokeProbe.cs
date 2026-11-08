#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SnowballArena
{
    internal sealed class MatchSmokeProbe : MonoBehaviour
    {
        private string output;
        private MatchController match;
        private Keyboard keyboard;
        private readonly List<string> checks = new List<string>();
        private string error;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--match-smoke");
            if (index >= 0 && index + 1 < args.Length) new GameObject("Match smoke validation").AddComponent<MatchSmokeProbe>().output = args[index + 1];
        }
        private void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) error = message + "\n" + stack;
        }
        private IEnumerator Start()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Application.logMessageReceived += Log; Directory.CreateDirectory(output);
            keyboard = InputSystem.AddDevice<Keyboard>(); match = FindFirstObjectByType<MatchController>();
            yield return null;
            if (!Check(match != null && match.Phase == MatchPhase.Menu, "Starts at menu")) yield break;
            yield return KeyPress(Key.LeftArrow);
            if (!Check(match.botCount == 4, "Keyboard changes robot count")) yield break;
            yield return KeyPress(Key.Enter);
            if (!Check(match.Participants.Count == 5 && match.Phase == MatchPhase.Countdown, "Enter starts selected count and countdown")) yield break;
            yield return new WaitForSeconds(3.3f);
            if (!Check(match.Phase == MatchPhase.Running, "Countdown enables play")) yield break;
            var player = match.Participants[0].body; var start = player.RigidBody.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D)); yield return new WaitForSeconds(0.7f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            if (!Check((player.RigidBody.position - start).magnitude > 0.1f, "WASD moves the live player")) yield break;
            yield return new WaitForSeconds(2f);
            Capture("live-match.png");
            bool activeBot = false;
            for (int i = 1; i < match.Participants.Count; i++) activeBot |= match.Participants[i].body.GetComponent<SnowballGrowth>().RolledDistance > 0.2;
            if (!Check(activeBot, "Bots move and grow through shared physics")) yield break;

            match.StartMatch(1); match.BeginRunning();
            match.Participants[1].body.RigidBody.position = Vector3.down * 20; match.ResolveEliminations();
            if (!Check(match.Phase == MatchPhase.Results && match.Result == "你赢了！", "Last player wins")) yield break;
            yield return KeyPress(Key.R); yield return null;
            if (!Check(match.Phase == MatchPhase.Countdown && match.Participants.Count == 2 && match.Participants[0].body.Radius < 0.66f, "R restarts with reset bodies")) yield break;
            match.BeginRunning();
            foreach (var p in match.Participants) p.body.RigidBody.position = Vector3.down * 20;
            match.ResolveEliminations();
            if (!Check(match.Phase == MatchPhase.Results && match.Result == "平局", "Same-step elimination is a draw")) yield break;
            match.StartMatch(1); match.BeginRunning();
            match.Participants[1].body.RigidBody.position = Vector3.down * 20;
            match.Participants[0].body.RigidBody.position = new Vector3(20, -4, 0);
            match.ResolveEliminations();
            if (!Check(match.Phase == MatchPhase.Running, "Last airborne sphere is not incorrectly declared winner")) yield break;
            match.Participants[0].body.RigidBody.position = Vector3.down * 20; match.ResolveEliminations();
            if (!Check(match.Result == "平局", "All falling spheres eventually resolve to a draw")) yield break;
            match.StartMatch(3); match.BeginRunning();
            match.Participants[0].body.RigidBody.position = Vector3.down * 20; match.ResolveEliminations();
            if (!Check(match.Phase == MatchPhase.Running && !match.Participants[0].alive && match.Remaining == 3, "Player elimination continues spectating")) yield break;
            yield return KeyPress(Key.Escape);
            if (!Check(match.Phase == MatchPhase.Menu && match.Participants.Count == 0, "Escape returns to clean menu")) yield break;
            match.StartMatch(0); match.BeginRunning();
            if (!Check(match.Participants.Count == 1 && match.Phase == MatchPhase.Running, "Zero bots is practice, not instant victory")) yield break;
            match.StartMatch(15); match.BeginRunning();
            bool spaced = match.Participants.Count == 16;
            for (int i = 0; i < match.Participants.Count; i++)
                for (int j = i + 1; j < match.Participants.Count; j++)
                    spaced &= Vector3.Distance(match.Participants[i].body.RigidBody.position, match.Participants[j].body.RigidBody.position) > 1.3f;
            if (!Check(spaced, "Sixteen participants spawn without overlap")) yield break;
            // Let a crowded match resolve naturally. The human is deliberately idle.
            Time.timeScale = 4f;
            float elapsed = 0;
            while (match.Phase == MatchPhase.Running && elapsed < 150)
            { yield return new WaitForSeconds(1); elapsed++; }
            checks.Add("Natural match remaining=" + match.Remaining);
            if (match.Phase == MatchPhase.Running)
                foreach (var p in match.Participants)
                    if (p.alive) checks.Add($"Survivor {p.displayName}: radius={p.body.Radius:F2}, speed={p.body.RigidBody.linearVelocity.magnitude:F2}, direction={p.motor.DesiredDirection}, position={p.body.RigidBody.position}");
            if (match.Phase == MatchPhase.Running) Capture("stalled-match.png");
            if (!Check(match.Phase == MatchPhase.Results, "Crowded bot match reaches a natural result within 150 simulated seconds")) yield break;
            checks.Add("Natural result after " + elapsed + " simulated seconds: " + match.Result);
            Capture("completed-match.png");
            Time.timeScale = 1;
            match.ReturnToMenu();
            yield return null;
            Finish();
        }
        private IEnumerator KeyPress(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }
        private bool Check(bool pass, string name)
        {
            checks.Add((pass ? "PASS " : "FAIL ") + name);
            if (!pass) { error = name; Finish(); }
            return pass;
        }
        private void Capture(string name)
        {
            var camera = Camera.main; var previous = RenderTexture.active; var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG()); camera.targetTexture = null; RenderTexture.active = previous; Destroy(image); Destroy(target);
        }
        private void Finish()
        {
            Time.timeScale = 1;
            File.WriteAllText(Path.Combine(output, "match-validation.txt"), "passed=" + string.IsNullOrEmpty(error) + "\n" + string.Join("\n", checks) + "\nerror=" + error);
            Application.logMessageReceived -= Log;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            Application.Quit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
}
#endif
