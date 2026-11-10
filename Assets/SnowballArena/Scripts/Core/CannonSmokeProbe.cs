#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SnowballArena
{
    internal sealed class CannonSmokeProbe : MonoBehaviour
    {
        private string output;
        private MatchController match;
        private readonly List<string> checks = new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--cannon-smoke");
            if (index >= 0 && index + 1 < args.Length)
                new GameObject("Cannon validation").AddComponent<CannonSmokeProbe>().output = args[index + 1];
        }
        private IEnumerator Start()
        {
            yield return null;
            Directory.CreateDirectory(output);
            string error = null;
            try { Run(); } catch (Exception ex) { error = ex.ToString(); }
            File.WriteAllText(Path.Combine(output, "cannon-validation.txt"), "passed=" + (error == null) + "\n" + string.Join("\n", checks) + "\nerror=" + error);
            Application.Quit(error == null ? 0 : 1);
        }
        private void Run()
        {
            match = FindFirstObjectByType<MatchController>(); match.enabled = false;
            match.StartMatch(3);
            var cannon = match.Cannon;
            cannon.Advance(70);
            Check(cannon.Elapsed == 0 && !cannon.HasVisuals, "Countdown does not advance cannon clock");
            match.BeginRunning(); Freeze();
            cannon.Advance(59.98f);
            Check(!cannon.HasVisuals && cannon.ShotsFired == 0, "No cannon before 60 seconds");
            cannon.Advance(0.02f);
            Check(cannon.Stage == CannonStage.Warning && cannon.WarningVisible && cannon.FlashCount == 1, "Cannon spawns at 60 seconds with first flash");
            var direction = (cannon.LaneEnd - cannon.LaneStart).normalized;
            var center = (cannon.LaneStart + cannon.LaneEnd) * 0.5f;
            var side = Vector3.Cross(Vector3.up, direction);
            Check(Vector3.Distance(new Vector3(center.x, 0, center.z), match.surface.AimPoint) < 0.1f
                && Vector3.Distance(cannon.LaneStart, cannon.LaneEnd) > 21.5f, "Warning follows the real full diameter through the center");
            Place(0, center); Place(1, cannon.LaneStart + direction * 0.2f); Place(2, center + side * 4); Place(3, center - side * 4);
            cannon.Advance(0.001f); Capture("cannon-warning.png");
            cannon.Advance(0.499f);
            Check(!cannon.WarningVisible && cannon.ShotsFired == 0, "First flash turns off without firing");
            cannon.Advance(0.5f);
            Check(cannon.WarningVisible && cannon.FlashCount == 2, "Second flash turns on");
            cannon.Advance(0.5f);
            Check(!cannon.WarningVisible && cannon.ShotsFired == 0, "Second flash turns off without firing");
            cannon.Advance(0.5f);
            Check(cannon.WarningVisible && cannon.FlashCount == 3, "Third flash turns on");
            cannon.Advance(0.5f);
            Check(!cannon.WarningVisible && cannon.ShotsFired == 0, "Third flash turns off before firing");
            cannon.Advance(0.5f);
            Check(cannon.Stage == CannonStage.Flying && cannon.ShotsFired == 1 && cannon.FlashCount == 3, "Exactly one projectile fires after all three flashes");
            var oldStart = cannon.LaneStart;
            cannon.Advance(0.04f); match.ResolveEliminations();
            Check(!match.Participants[1].alive && match.Participants[1].eliminationCause == EliminationCause.Cannon, "Projectile instantly eliminates a hit bot");
            cannon.Advance(0.2f); Capture("cannon-projectile.png");
            cannon.Advance(0.45f); match.ResolveEliminations();
            Check(!match.Participants[0].alive && match.Notice == "你被炮弹击中" && match.Phase == MatchPhase.Running, "Hit player is eliminated and spectates remaining bots");
            Check(match.Participants[2].alive && match.Participants[3].alive, "Snowballs outside the shot lane survive");
            cannon.Advance(0.8f);
            Check(cannon.CycleCount == 2 && cannon.Stage == CannonStage.Warning && cannon.ShotsFired == 1
                && Vector3.Distance(oldStart, cannon.LaneStart) > 5, "Shot exits at the opposite edge and cannon respawns elsewhere");

            Setup(1); cannon.Advance(60);
            direction = (cannon.LaneEnd - cannon.LaneStart).normalized;
            side = Vector3.Cross(Vector3.up, direction);
            center = (cannon.LaneStart + cannon.LaneEnd) * 0.5f;
            Place(0, center + side * 5);
            Place(1, cannon.LaneStart + direction * (cannon.projectileSpeed * 0.2f) + side * 4);
            cannon.Advance(3);
            Place(1, cannon.LaneStart + direction * (cannon.projectileSpeed * 0.2f) - side * 4);
            cannon.Advance(0.4f); match.ResolveEliminations();
            Check(!match.Participants[1].alive && match.Participants[1].eliminationCause == EliminationCause.Cannon, "Relative sweep detects a fast snowball crossing the projectile between steps");
            Check(match.Result == "你赢了！" && cannon.Stage == CannonStage.Inactive && !cannon.HasVisuals, "Last survivor wins and all cannon visuals are removed");
            int cycles = cannon.CycleCount; cannon.Advance(120);
            Check(cannon.CycleCount == cycles && !cannon.HasVisuals, "Results stop the cannon cycle");

            Setup(0);
            Check(cannon.Elapsed == 0 && cannon.ShotsFired == 0 && !cannon.HasVisuals, "Restart clears the clock and old projectile");
            cannon.Advance(59);
            Check(!cannon.HasVisuals, "Restart waits a fresh 60 seconds");
            cannon.Advance(1);
            center = (cannon.LaneStart + cannon.LaneEnd) * 0.5f;
            Place(0, center); cannon.Advance(3); cannon.Advance(0.8f); match.ResolveEliminations();
            Check(match.Result == "练习结束" && !cannon.HasVisuals, "Practice mode also ends immediately on cannon hit");
            Setup(2); cannon.Advance(60); match.ReturnToMenu();
            Check(cannon.Elapsed == 0 && !cannon.HasVisuals && match.Participants.Count == 0, "Return to menu removes warning and resets hazards");
        }
        private void Setup(int bots) { match.StartMatch(bots); match.BeginRunning(); Freeze(); }
        private void Freeze()
        {
            for (int i = 0; i < match.Participants.Count; i++)
            {
                var p = match.Participants[i];
                var brain = p.body.GetComponent<BotBrain>(); if (brain != null) brain.enabled = false;
                p.body.GetComponent<PlayerInputSource>().enabled = false;
                p.body.GetComponent<SnowballGrowth>().enabled = false;
                p.motor.controlEnabled = false; p.body.RigidBody.isKinematic = true;
                Place(i, new Vector3(i % 2 == 0 ? -4 : 4, 0, i < 2 ? -4 : 4));
            }
        }
        private void Place(int index, Vector3 point)
        {
            var p = match.Participants[index]; point.y = p.body.Radius;
            p.body.RigidBody.position = point; Physics.SyncTransforms();
        }
        private void Check(bool value, string name)
        {
            checks.Add((value ? "PASS " : "FAIL ") + name);
            if (!value) throw new Exception(name);
        }
        private void Capture(string name)
        {
            var camera = Camera.main; var previous = RenderTexture.active; var target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous; Destroy(image); Destroy(target);
        }
    }
}
#endif
