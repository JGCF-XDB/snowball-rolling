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
            cannon.Advance(19.98f);
            Check(!cannon.HasVisuals && cannon.ShotsFired == 0, "No cannon before 20 seconds");
            cannon.Advance(0.02f);
            Check(cannon.Stage == CannonStage.Warning && cannon.WarningVisible && cannon.FlashCount == 1, "Cannon spawns at 20 seconds with first flash");
            var direction = (cannon.LaneEnd - cannon.LaneStart).normalized;
            var center = (cannon.LaneStart + cannon.LaneEnd) * 0.5f;
            var side = Vector3.Cross(Vector3.up, direction);
            Check(Vector3.Distance(new Vector3(center.x, 0, center.z), match.surface.AimPoint) < 0.1f
                && Vector3.Distance(cannon.LaneStart, cannon.LaneEnd) > 21.5f, "Warning follows the real full diameter through the center");
            Place(0, center); Place(1, cannon.LaneStart + direction * 4f); Place(2, center + side * 4); Place(3, center - side * 4);
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
            cannon.Advance(0.8f); match.ResolveEliminations();
            Check(!match.Participants[1].alive && match.Participants[1].eliminationCause == EliminationCause.Cannon,
                "First physical hit wins even when a farther target appears earlier in the list");
            Check(match.Participants[0].alive && match.Participants[2].alive && match.Participants[3].alive,
                "A projectile crossing several targets in one step kills only the first");
            Check(!cannon.HasProjectile && cannon.CycleCount == 2 && cannon.Stage == CannonStage.Warning && cannon.ShotsFired == 1
                && Vector3.Distance(oldStart, cannon.LaneStart) > 5, "Impact removes projectile and starts warning at a different cannon position");

            // A complete miss must still travel to the far boundary before respawning.
            Setup(3); cannon.Advance(20);
            direction = (cannon.LaneEnd - cannon.LaneStart).normalized;
            side = Vector3.Cross(Vector3.up, direction);
            center = (cannon.LaneStart + cannon.LaneEnd) * 0.5f;
            for (int i = 0; i < 4; i++) Place(i, center + side * (i < 2 ? 4 : -4) + direction * (i % 2 == 0 ? 2 : -2));
            cannon.Advance(3); cannon.Advance(0.2f); Capture("cannon-projectile.png");
            Check(cannon.HasProjectile && cannon.CycleCount == 1, "Missed projectile remains alive during its flight");
            cannon.Advance(Vector3.Distance(cannon.LaneStart, cannon.LaneEnd) / cannon.projectileSpeed - 0.2f);
            Check(!cannon.HasProjectile && cannon.CycleCount == 2 && cannon.Stage == CannonStage.Warning,
                "Missed projectile disappears only at opposite edge and cannon relocates");

            Setup(2); cannon.Advance(20);
            center = (cannon.LaneStart + cannon.LaneEnd) * 0.5f;
            side = Vector3.Cross(Vector3.up, (cannon.LaneEnd - cannon.LaneStart).normalized);
            Place(0, center); Place(1, center + side * 4); Place(2, center - side * 4);
            cannon.Advance(3); cannon.Advance(0.8f); match.ResolveEliminations();
            Check(!match.Participants[0].alive && match.Notice == "你被炮弹击中" && match.Phase == MatchPhase.Running,
                "Hit player is eliminated and spectates remaining bots");

            Setup(1); cannon.Advance(20);
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
            cannon.Advance(19);
            Check(!cannon.HasVisuals, "Restart waits a fresh 20 seconds");
            cannon.Advance(1);
            center = (cannon.LaneStart + cannon.LaneEnd) * 0.5f;
            Place(0, center); cannon.Advance(3); cannon.Advance(0.8f); match.ResolveEliminations();
            Check(match.Result == "练习结束" && !cannon.HasVisuals, "Practice mode also ends immediately on cannon hit");
            Setup(2); cannon.Advance(20); match.ReturnToMenu();
            Check(cannon.Elapsed == 0 && !cannon.HasVisuals && match.Participants.Count == 0, "Return to menu removes warning and resets hazards");
            RunEscalation();
        }
        private void CheckAngles(string name)
        {
            bool distinct = true;
            var cannons = match.Cannon.Cannons;
            for (int i = 0; i < cannons.Count; i++)
                for (int j = i + 1; j < cannons.Count; j++)
                {
                    float angle = Vector3.Angle(cannons[i].LaneEnd - cannons[i].LaneStart, cannons[j].LaneEnd - cannons[j].LaneStart);
                    distinct &= angle >= 24.9f && angle <= 155.1f;
                }
            Check(distinct, name);
        }
        private void ParkOutside()
        {
            for (int i = 0; i < match.Participants.Count; i++) Place(i, new Vector3(100 + i * 4, 0, 100));
        }
        private void RunEscalation()
        {
            var cannon = match.Cannon;
            Setup(5); ParkOutside();
            cannon.Advance(39.98f);
            Check(cannon.ActiveCannonCount == 1, "Only one cannon before 40 seconds");
            var first = cannon.Cannons[0];
            cannon.Advance(0.02f);
            Check(cannon.ActiveCannonCount == 2 && cannon.Cannons[0] == first && cannon.Cannons[1].WarningVisible
                && cannon.Cannons[1].FlashCount == 1, "At 40 seconds second cannon joins with its own full warning");
            CheckAngles("Two cannons use different non-overlapping diameters");
            cannon.Advance(19.98f);
            Check(cannon.ActiveCannonCount == 2, "Only two cannons before 60 seconds");
            cannon.Advance(0.02f);
            Check(cannon.ActiveCannonCount == 3 && cannon.Cannons[2].WarningVisible && cannon.Cannons[2].FlashCount == 1,
                "At 60 seconds third cannon joins with its own full warning");
            CheckAngles("Three cannons use different non-overlapping diameters");
            Capture("cannon-three.png");
            bool bounded = true, spaced = true;
            for (int n = 0; n < 300; n++)
            {
                cannon.Advance(0.5f);
                bounded &= cannon.ActiveCannonCount == 3;
                for (int i = 0; i < 3; i++) for (int j = i + 1; j < 3; j++)
                {
                    float angle = Vector3.Angle(cannon.Cannons[i].LaneEnd - cannon.Cannons[i].LaneStart,
                        cannon.Cannons[j].LaneEnd - cannon.Cannons[j].LaneStart);
                    spaced &= angle >= 24.9f && angle <= 155.1f;
                }
            }
            Check(bounded && cannon.CycleCount > 90, "Long-running repeated cycles retain exactly three cannons after 60 seconds");
            Check(spaced, "Repeated independent relocations maintain angle separation");
            match.ReturnToMenu();
            Check(!cannon.HasVisuals && !cannon.HasProjectile && cannon.ActiveCannonCount == 0 && cannon.Elapsed == 0,
                "Menu clears all three cannons, projectiles and escalation clock");
            Setup(1); ParkOutside(); cannon.Advance(20);
            Check(cannon.ActiveCannonCount == 1 && cannon.ShotsFired == 0, "Restart resets escalated difficulty to one cannon at 20 seconds");

            // Short thresholds let all three projectiles coexist in a controlled fixture.
            cannon.startAfterSeconds = 0; cannon.escalationInterval = 0.1f;
            try
            {
                Setup(3); ParkOutside(); cannon.Advance(0.2f);
                Check(cannon.ActiveCannonCount == 3 && cannon.WarningCount == 3 && cannon.ShotsFired == 0,
                    "Each newly spawned cannon starts warning without an immediate shot");
                var a = cannon.Cannons[0]; var b = cannon.Cannons[1]; var c = cannon.Cannons[2];
                Place(0, a.LaneStart + (a.LaneEnd - a.LaneStart).normalized * 6);
                Place(1, c.LaneStart + (c.LaneEnd - c.LaneStart).normalized * 2);
                cannon.Advance(2.8f); cannon.Advance(0.2f);
                Check(cannon.ProjectileCount == 3 && cannon.ShotsFired == 3,
                    "Three independent cannons each complete three flashes and fire one projectile");
                var otherOrigin = b.LaneStart;
                cannon.Advance(0.2f);
                Check(!match.Participants[0].alive && !match.Participants[1].alive
                    && match.Participants[2].alive && match.Participants[3].alive,
                    "Concurrent projectiles each eliminate their first target only");
                Check(!a.HasProjectile && !c.HasProjectile && a.Stage == CannonStage.Warning && c.Stage == CannonStage.Warning
                    && b.HasProjectile && b.LaneStart == otherOrigin,
                    "Hit cannons relocate independently while an unaffected projectile keeps flying");
                CheckAngles("Post-impact relocations still avoid both other cannon diameters");
                match.Eliminate(match.Participants[2], EliminationCause.Cannon);
                // Bring the surviving participant back onto the platform for victory.
                Place(3, Vector3.zero); match.ResolveEliminations();
                Check(match.Phase == MatchPhase.Results && cannon.ActiveCannonCount == 0 && !cannon.HasProjectile,
                    "Victory clears all simultaneous warnings and projectiles");
                int shots = cannon.ShotsFired; cannon.Advance(120);
                Check(cannon.ShotsFired == shots && cannon.ActiveCannonCount == 0,
                    "Finished match cannot escalate or fire further shots");
            }
            finally { cannon.startAfterSeconds = 20; cannon.escalationInterval = 20; }
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
