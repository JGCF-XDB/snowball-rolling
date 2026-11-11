#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SnowballArena
{
    // Opt-in player capture includes the actual OnGUI layer, unlike camera-only art renders.
    internal sealed class UiReviewProbe : MonoBehaviour
    {
        private string output, error;
        private MatchController match;
        private readonly List<string> captures = new List<string>();
        private bool finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--ui-review");
            if (index >= 0 && index + 1 < args.Length)
                new GameObject("UI review capture").AddComponent<UiReviewProbe>().output = args[index + 1];
        }

        private void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = message + "\n" + stack;
        }

        private void Update()
        {
            if (!finished && error != null) Finish();
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            Application.logMessageReceived += Log;
            Directory.CreateDirectory(output);
            yield return null;
            match = FindFirstObjectByType<MatchController>();
            if (match == null || Resources.Load<Font>("UI/LXGWMarkerGothic-Regular") == null || Resources.Load<Texture2D>("UI/GameTitle") == null)
            { error = "UI scene/font/wordmark is missing."; Finish(); yield break; }
            Time.timeScale = 0;
            yield return Capture("01-home.png");
            match.AdvanceMenu();
            yield return Capture("02-tutorial.png");
            match.AdvanceMenu();
            yield return Capture("03-opponents.png");
            match.botCount = 15;
            yield return Capture("03b-max-opponents.png");
            match.botCount = 0;
            yield return Capture("04-practice-menu.png");
            match.botCount = 5;
            match.AdvanceMenu();
            yield return Capture("05-countdown.png");
            match.BeginRunning();
            Freeze();
            yield return Capture("06-match.png");

            match.StartMatch(1);
            match.BeginRunning();
            match.Participants[1].body.RigidBody.position = Vector3.down * 20;
            match.ResolveEliminations();
            Freeze();
            Time.timeScale = 1;
            yield return new WaitForSecondsRealtime(1.3f);
            yield return Capture("07-victory-stage.png");
            yield return new WaitForSecondsRealtime(2f);
            yield return Capture("08-win-results.png");

            Time.timeScale = 0;
            match.celebrateVictory = false;
            match.StartMatch(15);
            match.BeginRunning();
            for (int i = 0; i < 15; i++) match.Participants[i].body.RigidBody.position = Vector3.down * 20;
            match.ResolveEliminations();
            yield return Capture("09-other-winner.png");
            match.StartMatch(1);
            match.BeginRunning();
            foreach (var participant in match.Participants) participant.body.RigidBody.position = Vector3.down * 20;
            match.ResolveEliminations();
            yield return Capture("10-draw.png");
            match.StartMatch(0);
            match.BeginRunning();
            match.Participants[0].body.RigidBody.position = Vector3.down * 20;
            match.ResolveEliminations();
            yield return Capture("11-practice-results.png");
            Finish();
        }

        private void Freeze()
        {
            foreach (var participant in match.Participants)
            {
                participant.motor.controlEnabled = false;
                participant.body.RigidBody.isKinematic = true;
                participant.body.GetComponent<SnowballGrowth>().enabled = false;
            }
        }

        private IEnumerator Capture(string filename)
        {
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            if (image == null)
            {
                error = "No rendered screen is available. Run UI review in a visible, unminimized game window.";
                Finish();
                yield break;
            }
            File.WriteAllBytes(Path.Combine(output, filename), image.EncodeToPNG());
            Destroy(image);
            captures.Add(filename);
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            Time.timeScale = 1;
            File.WriteAllText(Path.Combine(output, "ui-review.txt"),
                "passed=" + (error == null && captures.Count == 12 && HandwrittenTextAtlas.MissingCount == 0) + "\nproduct=" + Application.productName +
                "\nsize=" + Screen.width + "x" + Screen.height + "\nmissingLettering=" + HandwrittenTextAtlas.MissingCount +
                "\n" + string.Join("\n", captures) + "\nerror=" + error);
            Application.logMessageReceived -= Log;
            Application.Quit(error == null && captures.Count == 12 && HandwrittenTextAtlas.MissingCount == 0 ? 0 : 1);
        }
    }
}
#endif
