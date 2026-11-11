#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

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
            // The victory stage is a visual beat with a 3 s hold; automation checks phases, not cinematics.
            if (match != null) match.celebrateVictory = false;
            yield return null;
            if (!Check(match != null && match.Phase == MatchPhase.Menu, "Starts at menu")) yield break;
            int initialBots = match.botCount;
            yield return KeyPress(Key.LeftArrow);
            if (!Check(match.Page == MenuPage.Home && match.botCount == initialBots, "Home does not change opponent count")) yield break;
            yield return KeyPress(Key.Enter);
            if (!Check(match.Page == MenuPage.Tutorial && match.Participants.Count == 0, "Start opens tutorial without spawning balls")) yield break;
            yield return KeyPress(Key.Enter);
            if (!Check(match.Page == MenuPage.Opponents && match.Participants.Count == 0, "Tutorial continues to opponent selection")) yield break;
            yield return KeyPress(Key.LeftArrow);
            if (!Check(match.botCount == 4, "Keyboard changes robot count")) yield break;
            yield return KeyPress(Key.Enter);
            if (!Check(match.Participants.Count == 5 && match.Phase == MatchPhase.Countdown, "Enter starts selected count and countdown")) yield break;
            // Wait for the state instead of assuming a frame budget: one dropped frame during
            // the countdown used to land these assertions in the middle of the transition.
            yield return WaitUntil(() => match.Phase == MatchPhase.Running, 6f);
            if (!Check(match.Phase == MatchPhase.Running, "Countdown enables play")) yield break;
            var player = match.Participants[0].body; var start = player.RigidBody.position;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D));
            yield return WaitUntil(() => (player.RigidBody.position - start).magnitude > 0.1f, 2f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            if (!Check((player.RigidBody.position - start).magnitude > 0.1f, "WASD moves the live player")) yield break;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--menu-only") >= 0)
            {
                yield return KeyPress(Key.Escape);
                if (!Check(match.Phase == MatchPhase.Menu && match.Page == MenuPage.Home && match.Participants.Count == 0,
                    "Escape resets the menu flow and clears participants")) yield break;
                Finish(); yield break;
            }
            yield return new WaitForSeconds(2f);
            Capture("live-match.png");
            bool activeBot = false;
            for (int i = 1; i < match.Participants.Count; i++) activeBot |= match.Participants[i].body.GetComponent<SnowballGrowth>().RolledDistance > 0.2;
            if (!Check(activeBot, "Bots move and grow through shared physics")) yield break;

            match.StartMatch(1); match.BeginRunning();
            match.Participants[1].body.RigidBody.position = Vector3.down * 20; match.ResolveEliminations();
            if (!Check(match.Phase == MatchPhase.Results && match.Result == "你赢了！", "Last player wins")) yield break;
            // A synthetic key event can land on a frame the controller already consumed, so
            // wait for the transition and retry the press once before calling it a failure.
            yield return KeyPress(Key.R);
            yield return WaitUntil(() => match.Phase == MatchPhase.Countdown, 1.5f);
            if (match.Phase != MatchPhase.Countdown)
            {
                yield return KeyPress(Key.R);
                yield return WaitUntil(() => match.Phase == MatchPhase.Countdown, 1.5f);
            }
            if (!Check(match.Phase == MatchPhase.Countdown && match.Participants.Count == 2 && Mathf.Abs(match.Participants[0].body.Radius - match.Participants[0].body.settings.initialRadius) < 0.001f, "R restarts with reset bodies")) yield break;
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
                    spaced &= Vector3.Distance(match.Participants[i].body.RigidBody.position, match.Participants[j].body.RigidBody.position) > match.Participants[i].body.Radius + match.Participants[j].body.Radius;
            if (!Check(spaced, "Sixteen participants spawn without overlap")) yield break;
            // Safer bots are allowed long survival matches. Verify actual combat and
            // eliminations, rather than treating a short round full of suicides as success.
            var contacts = new List<BotContactProbe>();
            foreach (var p in match.Participants) contacts.Add(p.body.gameObject.AddComponent<BotContactProbe>());
            Time.timeScale = 4f;
            float elapsed = 0; int previousContacts = 0;
            bool ongoingCombat = true;
            while (match.Phase == MatchPhase.Running && elapsed < 120)
            {
                yield return new WaitForSeconds(1); elapsed++;
                if (elapsed % 30 == 0)
                {
                    int total = 0;
                    foreach (var tracker in contacts) total += tracker.Contacts;
                    ongoingCombat &= total - previousContacts >= 2;
                    checks.Add($"Combat window: contacts={total - previousContacts}, remaining={match.Remaining}");
                    previousContacts = total;
                }
            }
            checks.Add("Natural match remaining=" + match.Remaining);
            int untouchedFalls = 0, totalContacts = 0;
            for (int i = 1; i < match.Participants.Count; i++)
            {
                totalContacts += contacts[i].Contacts;
                if (!match.Participants[i].alive && match.Participants[i].eliminationCause == EliminationCause.Fall && !contacts[i].Hit) untouchedFalls++;
            }
            checks.Add($"Bot contacts={totalContacts}, falls before any contact={untouchedFalls}");
            if (!Check(contacts[0].Hit && untouchedFalls == 0 && match.Remaining < 16 &&
                (match.Phase == MatchPhase.Results || ongoingCombat && totalContacts >= 30),
                "Crowded bots attack the idle player and sustain real combat without unopposed suicide")) yield break;
            checks.Add("State after " + elapsed + " simulated seconds: " + match.Phase + " " + match.Result);
            Capture(match.Phase == MatchPhase.Results ? "completed-match.png" : "ongoing-match.png");
            Time.timeScale = 1;
            match.ReturnToMenu();
            yield return null;
            Finish();
        }
        // Polls a condition with a timeout so tests do not depend on a fixed frame budget.
        private IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds)
        {
            float deadline = Time.time + timeoutSeconds;
            while (!condition() && Time.time < deadline) yield return null;
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
            var camera = Camera.main;
            var framing = camera.GetComponent<ArenaCamera>();
            if (framing != null) camera = framing.RenderRoot;
            var previous = RenderTexture.active; var target = new RenderTexture(1280, 720, 24);
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG()); RenderTexture.active = previous; Destroy(image); Destroy(target);
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
