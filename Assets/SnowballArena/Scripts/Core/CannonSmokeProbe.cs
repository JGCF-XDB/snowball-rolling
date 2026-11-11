#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnowballArena
{
    internal sealed class CannonSmokeProbe : MonoBehaviour
    {
        private string output;
        private const int CannonSeed = 20260111;
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
            SnowballExplosion finalExplosion = null;
            try { Run(); finalExplosion = PrepareFinalExplosion(); } catch (Exception ex) { error = ex.ToString(); }
            if (error == null)
            {
                // Let real player frames, rather than manual Advance calls, expire the final hit.
                float deadline = Time.realtimeSinceStartup + 5f;
                bool resultsWithExplosion = false;
                while ((finalExplosion != null || match.Phase != MatchPhase.Results) && Time.realtimeSinceStartup < deadline)
                {
                    resultsWithExplosion |= match.Phase == MatchPhase.Results && finalExplosion != null;
                    yield return null;
                }
                try
                {
                    Check(resultsWithExplosion, "Results panel opens while the final-hit explosion keeps playing");
                    Check(finalExplosion == null && SnowballExplosion.Active.Count == 0,
                        "Final-hit explosion expires and unregisters automatically in player frames");
                    match.ReturnToMenu();
                }
                catch (Exception ex) { error = ex.ToString(); }
            }
            File.WriteAllText(Path.Combine(output, "cannon-validation.txt"), "passed=" + (error == null) + "\n" + string.Join("\n", checks) + "\nerror=" + error);
            Application.Quit(error == null ? 0 : 1);
        }
        private void Run()
        {
            // Cannon lanes pick a random angle, and some sequences leave no legal placement for
            // the third cannon, which made this probe fail intermittently. Seed it so every run
            // takes the same path.
            UnityEngine.Random.InitState(CannonSeed);
            match = FindFirstObjectByType<MatchController>(); match.enabled = false;
            match.celebrateVictory = false;
            CheckSnowballExplosionAssets();
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
            CheckWarning(cannon.Cannons[0]);
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
            CheckVisuals(cannon.Cannons[0]);
            CaptureOldProjection(cannon.Cannons[0]);
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
            RunSelectedVisuals();
            RunEscalation();
        }
        private void CheckSnowballExplosionAssets()
        {
            var dust = Resources.Load<Material>(SnowballExplosion.DustMaterialName);
            var glow = Resources.Load<Material>(SnowballExplosion.GlowMaterialName);
            var snow = Resources.Load<Material>(SnowballExplosion.SnowMaterialName);
            var ice = Resources.Load<Material>(SnowballExplosion.IceMaterialName);
            Check(dust != null && glow != null && snow != null && ice != null,
                "Player build includes all snowball explosion materials");
            Check(dust != null && dust.shader != null && dust.shader.name == "SnowArena/SnowExplosion"
                && dust.shader.isSupported && glow != null && glow.shader != null
                && glow.shader.name == "SnowArena/SnowExplosion" && glow.shader.isSupported,
                "Player build includes the supported snowball explosion shader");
            Check(match.Cannon.snowballExplosionStyle == SnowballExplosionStyle.IceShatter,
                "Selected ice-shatter explosion style is the default");
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
        private void CheckVisuals(CannonState state)
        {
            var camera = Camera.main;
            var framing = camera.GetComponent<ArenaCamera>();
            var backdrop = framing != null ? framing.backgroundCamera : null;
            bool correctCamera = true;
            foreach (var renderer in state.root.GetComponentsInChildren<Renderer>(true))
            {
                int mask = 1 << renderer.gameObject.layer;
                correctCamera &= (camera.cullingMask & mask) != 0
                    && (backdrop == null || (backdrop.cullingMask & mask) == 0);
            }
            Check(correctCamera, "Cannon, projectile and trail render through the gameplay camera, never the distant backdrop");
            Vector3 viewport = camera.WorldToViewportPoint(state.shot.transform.position);
            Check(viewport.z > camera.nearClipPlane && viewport.x > 0 && viewport.x < 1
                && viewport.y > 0 && viewport.y < 1, "Flying cannonball projects inside the gameplay viewport");
            Vector3 direction = (state.LaneEnd - state.LaneStart).normalized;
            float length = Vector3.Distance(state.trail.GetPosition(0), state.trail.GetPosition(1));
            Check(state.trail.enabled && length > 0 && length <= match.Cannon.projectileSpeed * 0.12f + 0.001f
                && Vector3.Distance(state.trail.GetPosition(1), state.ShotPosition) < 0.001f
                && Vector3.Dot(state.ShotPosition - state.trail.GetPosition(0), direction) > 0,
                "Short flight trail ends at the projectile and stays behind it on the actual lane");
            bool noColliders = true;
            foreach (var collider in state.root.GetComponentsInChildren<Collider>()) noColliders &= !collider.enabled;
            Check(noColliders, "Hazard visuals introduce no physical obstacles or extra collision hits");
            var art = Resources.Load<CannonArt>("CannonArt");
            Check(state.shot.GetComponentInChildren<MeshFilter>().sharedMesh == art.projectilePrefab.GetComponentInChildren<MeshFilter>().sharedMesh,
                "Flying cannonball uses the selected Blender-authored ice-core mesh");
            Check(Vector3.Distance(state.visuals.muzzle.position, state.LaneStart) < 0.001f
                && Mathf.Abs(state.visuals.muzzleInnerDiameter * state.visuals.transform.lossyScale.x - match.Cannon.projectileRadius * 2) < 0.001f,
                "Modeled muzzle stays at the firing origin and its inner diameter equals the projectile diameter");
        }
        private void CheckWarning(CannonState state)
        {
            var warning = state.warning;
            Check(warning.sharedMaterial.shader.name == "SnowArena/CannonWarning" && warning.sharedMaterial.shader.isSupported,
                "Player build includes the supported infrared gradient warning shader");
            var mesh = new Mesh();
            try
            {
                // Inspect rendered geometry, including both edges of the gradient
                // ribbon. A camera-facing ribbon would fail the height and platform checks.
                warning.BakeMesh(mesh, Camera.main, true);
                bool grounded = mesh.vertexCount >= 4;
                bool inFront = true;
                Vector3 direction = (state.LaneEnd - state.LaneStart).normalized;
                var collider = match.surface.GetComponent<Collider>();
                float laneLength = Vector3.Distance(state.LaneStart, state.LaneEnd);
                foreach (var vertex in mesh.vertices)
                {
                    grounded &= collider.Raycast(new Ray(vertex + Vector3.up, Vector3.down), out var hit, 2)
                        && vertex.y - hit.point.y > 0 && vertex.y - hit.point.y < 0.05f;
                    float along = Vector3.Dot(vertex - state.LaneStart, direction);
                    inFront &= along > 0.1f && along < laneLength - 0.1f;
                }
                Check(grounded, "Entire infrared ribbon stays just above supported snow instead of standing across the cannon");
                Check(inFront, "Warning geometry begins in front of the muzzle and ends inside the opposite platform rim");
            }
            finally { Destroy(mesh); }
        }
        private void RunSelectedVisuals()
        {
            var cannon = match.Cannon;
            Setup(3); ParkOutside(); cannon.Advance(20);
            var state = cannon.Cannons[0];
            var direction = (state.LaneEnd - state.LaneStart).normalized;
            var firingOrigin = state.LaneStart;
            Place(1, state.LaneStart + direction * 5);
            CaptureClose("selected-cannon-warning-close.png", state.LaneStart - direction * 1.5f - Vector3.up * 0.2f, direction);
            cannon.Advance(0.5f);
            Check(state.visuals.signals[0].sharedMaterial == state.visuals.signalOff, "Modeled runes turn off with the warning line");
            cannon.Advance(0.5f);
            Check(state.visuals.signals[0].sharedMaterial == state.visuals.signalOn, "Modeled runes turn on with the warning line");
            cannon.Advance(2);
            Check(state.visuals.muzzleFlash.activeSelf && Vector3.Distance(state.shot.transform.position, state.visuals.muzzle.position) < 0.001f,
                "Selected cannon fires from the fitted muzzle with a brief flash");
            cannon.Advance(0.04f);
            Check(state.visuals.barrel.localPosition.z < -0.05f && state.visuals.muzzleFlash.activeSelf,
                "Barrel recoils while the flash is visible without moving the logical firing lane");
            CaptureClose("selected-cannon-launch-close.png", state.LaneStart - direction * 0.8f, direction);
            cannon.Advance(0.19f);
            Check(!match.Participants[1].alive && cannon.ActiveEffectCount == 1, "Impact removes one target and emits one decorative ice-and-ember burst");
            Check(SnowballExplosion.Active.Count == 1, "Player build spawns exactly one snowball explosion per cannon kill");
            var explosion = SnowballExplosion.Active[0];
            Check(explosion != null && explosion.Style == cannon.snowballExplosionStyle
                && explosion.PuffCount > 30 && explosion.ChunkCount >= 10
                && explosion.InfluenceRadius > explosion.Radius * 2.4f,
                "Snowball explosion uses the selected style and scales with the victim radius");
            Check(explosion.GetComponentsInChildren<Collider>(true).Length == 0,
                "Snowball explosion adds no colliders or gameplay obstacles");
            for (int step = 0; step < 4; step++) explosion.Advance(0.05f);
            Capture("snowball-explosion-runtime.png");
            cannon.Advance(0.07f);
            CaptureClose("selected-cannon-impact-close.png", firingOrigin + direction * 3.85f, direction);
            cannon.Advance(0.4f);
            for (int step = 0; step < 32; step++) explosion.Advance(0.05f);
            Check(!explosion.Advance(0.05f), "Snowball explosion finishes within its authored lifetime");
            Check(cannon.ActiveEffectCount == 0, "Decorative impact fragments expire after their short lifetime");
            match.ReturnToMenu();
            Check(SnowballExplosion.Active.Count == 0, "Menu clears every live snowball explosion");
            Check(explosion == null || !explosion.gameObject.activeSelf,
                "Menu immediately hides old explosion visuals before deferred destruction");
            Check(cannon.ActiveEffectCount == 0 && cannon.ActiveCannonCount == 0, "Menu removes selected models, muzzle flashes and decorative effects");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--cannon-motion") >= 0) CaptureMotion();
        }

        private SnowballExplosion PrepareFinalExplosion()
        {
            match.celebrateVictory = true;
            match.victorySeconds = 0.3f;
            Setup(1);
            var cannon = match.Cannon;
            cannon.Advance(20);
            var state = cannon.Cannons[0];
            var direction = (state.LaneEnd - state.LaneStart).normalized;
            var center = (state.LaneStart + state.LaneEnd) * 0.5f;
            Place(0, center + Vector3.Cross(Vector3.up, direction) * 5f);
            Place(1, state.LaneStart + direction * 5f);
            cannon.Advance(3);
            cannon.Advance(0.4f);
            match.ResolveEliminations();
            Check(match.Phase == MatchPhase.Victory && SnowballExplosion.Active.Count == 1,
                "Victory stage stops the cannon while preserving exactly one final-hit explosion");
            var explosion = SnowballExplosion.Active[0];
            Check(explosion != null && explosion.gameObject.activeInHierarchy && !explosion.Manual,
                "Final-hit explosion keeps playing automatically during the victory stage");
            match.enabled = true;
            return explosion;
        }

        private void CaptureMotion()
        {
            var cannon = match.Cannon;
            Setup(3); ParkOutside(); cannon.Advance(20);
            var state = cannon.Cannons[0];
            var direction = (state.LaneEnd - state.LaneStart).normalized;
            var side = Vector3.Cross(Vector3.up, direction);
            var center = (state.LaneStart + state.LaneEnd) * 0.5f;
            Place(0, center + side * 4); Place(1, state.LaneStart + direction * 8);
            Place(2, center - side * 4); Place(3, center + side * 6 - direction * 3);
            Directory.CreateDirectory(Path.Combine(output, "motion"));
            for (int frame = 0; frame < 120; frame++)
            {
                Capture("motion/" + frame.ToString("D3") + ".png");
                // This entire capture runs in one player frame, so step the same effect clock
                // that Update normally drives and hide finished visuals before rendering again.
                foreach (var explosion in SnowballExplosion.Active)
                    if (!explosion.Advance(1f / 24f)) explosion.gameObject.SetActive(false);
                cannon.Advance(1f / 24f);
            }
            match.ReturnToMenu();
        }

        private void CaptureClose(string name, Vector3 target, Vector3 direction)
        {
            var camera = Camera.main;
            var position = camera.transform.position; var rotation = camera.transform.rotation;
            float fov = camera.fieldOfView;
            try
            {
                camera.transform.position = target + direction * 3.8f + Vector3.Cross(Vector3.up, direction) * 3.0f + Vector3.up * 3.0f;
                camera.transform.LookAt(target); camera.fieldOfView = 36;
                Capture(name);
            }
            finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }
        }
        private void CaptureOldProjection(CannonState state)
        {
            // Reproduce the old layer assignment at the exact same shot position,
            // then restore it without advancing gameplay or changing the cameras.
            var renderers = state.root.GetComponentsInChildren<MeshRenderer>();
            var layers = new int[renderers.Length];
            bool trailEnabled = state.trail.enabled;
            for (int i = 0; i < renderers.Length; i++)
            {
                layers[i] = renderers[i].gameObject.layer;
                renderers[i].gameObject.layer = 2;
            }
            try
            {
                state.trail.enabled = false;
                Capture("cannon-projection-before.png");
            }
            finally
            {
                for (int i = 0; i < renderers.Length; i++) renderers[i].gameObject.layer = layers[i];
                state.trail.enabled = trailEnabled;
            }
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
            checks.Add($"60-second boundary: elapsed={cannon.Elapsed:R}, active={cannon.ActiveCannonCount}, warnings={cannon.WarningCount}, thirdFlash={(cannon.ActiveCannonCount > 2 ? cannon.Cannons[2].FlashCount : -1)}");
            Check(cannon.ActiveCannonCount == 3 && cannon.Cannons[2].WarningVisible && cannon.Cannons[2].FlashCount == 1,
                "At 60 seconds third cannon joins with its own full warning");
            foreach (var state in cannon.Cannons) CheckWarning(state);
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
            var camera = Camera.main;
            var framing = camera.GetComponent<ArenaCamera>();
            if (framing != null) camera = framing.RenderRoot;
            var previous = RenderTexture.active; var target = new RenderTexture(1280, 720, 24);
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG());
            RenderTexture.active = previous; Destroy(image); Destroy(target);
        }
    }
}
#endif
