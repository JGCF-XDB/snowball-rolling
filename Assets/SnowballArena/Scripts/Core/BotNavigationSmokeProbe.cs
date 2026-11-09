#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SnowballArena
{
    // Runs the real brain, motor, growth and collisions in the shipped match scene.
    internal sealed class BotNavigationSmokeProbe : MonoBehaviour
    {
        private string output, error;
        private MatchController match;
        private readonly List<string> checks = new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--bot-smoke");
            if (index >= 0 && index + 1 < args.Length)
                new GameObject("Bot navigation validation").AddComponent<BotNavigationSmokeProbe>().output = args[index + 1];
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true; Application.targetFrameRate = 60;
            Application.logMessageReceived += Log;
            Directory.CreateDirectory(output);
            match = FindFirstObjectByType<MatchController>();
            Time.timeScale = 3;
            yield return null;
            // A stationary player must actually get hit, not merely be approached.
            for (int i = 0; i < 6; i++)
            {
                Vector3 direction = Quaternion.Euler(0, i * 60, 0) * Vector3.right;
                Setup(direction * 2, -direction * 6);
                var bot = match.Participants[1];
                var contact = bot.body.gameObject.AddComponent<BotContactProbe>();
                bool safe = true;
                float started = Time.time;
                while (!contact.Hit && bot.alive && Time.time - started < 12)
                {
                    yield return new WaitForFixedUpdate();
                    safe &= Supported(bot);
                }
                if (!Check(contact.Hit && safe, $"Actively hits idle player from heading {i * 60} without prior fall ({Time.time - started:F2}s)")) yield break;
            }
            // Start with recoverable outward momentum, including a grown heavy ball.
            for (int i = 0; i < 8; i++)
            {
                Vector3 direction = Quaternion.Euler(0, i * 45, 0) * Vector3.right;
                Setup(new Vector3(0, 12, 0), direction * 7.5f);
                var player = match.Participants[0]; player.body.RigidBody.isKinematic = true;
                var bot = match.Participants[1];
                if (i % 2 == 1) bot.body.Accrete(4d / 3d * Math.PI * Math.Pow(1.4, 3) - bot.body.Volume, Vector3.zero, true);
                bot.body.RigidBody.linearVelocity = direction * (i % 2 == 1 ? 2.5f : 4.5f);
                bot.body.RigidBody.angularVelocity = Vector3.Cross(Vector3.up, direction) * bot.body.RigidBody.linearVelocity.magnitude / bot.body.Radius;
                bool safe = true;
                float started = Time.time;
                while (Time.time - started < 4)
                {
                    yield return new WaitForFixedUpdate();
                    safe &= bot.alive && Supported(bot);
                }
                if (!Check(safe && bot.body.RigidBody.linearVelocity.magnitude < 0.5f,
                    $"Brakes before edge, heading {i * 45}, {(i % 2 == 1 ? "grown" : "initial")} size")) yield break;
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 direction = Quaternion.Euler(0, i * 90, 0) * Vector3.right;
                Setup(Vector3.zero, direction * 10.6f);
                var recovering = match.Participants[1];
                if (i % 2 == 1) recovering.body.Accrete(4d / 3d * Math.PI * Math.Pow(1.4, 3) - recovering.body.Volume, Vector3.zero, true);
                var contact = recovering.body.gameObject.AddComponent<BotContactProbe>();
                float started = Time.time; bool safe = true;
                while (!contact.Hit && recovering.alive && Time.time - started < 12)
                {
                    yield return new WaitForFixedUpdate();
                    safe &= Supported(recovering);
                }
                if (!Check(contact.Hit && safe, $"Recovers inward from edge margin and attacks, heading {i * 90}")) yield break;
            }
            Setup(new Vector3(10, 0, 0), new Vector3(3, 0, 0));
            var attacker = match.Participants[1];
            var edgeContact = attacker.body.gameObject.AddComponent<BotContactProbe>();
            float attackStart = Time.time;
            while (!edgeContact.Hit && attacker.alive && Time.time - attackStart < 12)
                yield return new WaitForFixedUpdate();
            if (!Check(edgeContact.Hit && Supported(attacker), "Attacks an opponent near the edge")) yield break;

            // Reproduce a slow but winnable push. A low speed must not trigger a retreat.
            for (int i = 0; i < 3; i++)
            {
                Vector3 direction = Quaternion.Euler(0, i * 120, 0) * Vector3.right;
                float radius = i == 2 ? 1.35f : 0.85f;
                Setup(direction * 9.4f, direction * (9.4f - 2f * radius - 0.08f));
                var defender = match.Participants[0]; var pusher = match.Participants[1];
                if (i == 2)
                    foreach (var p in match.Participants)
                        p.body.Accrete(4d / 3d * Math.PI * Math.Pow(radius, 3) - p.body.Volume, Vector3.zero, true);
                defender.motor.controlEnabled = true;
                defender.motor.SetMoveDirection(-direction * 0.75f);
                int pressureSteps = 0, retreatSteps = 0;
                float maximumGap = 0;
                float withdrawalTime = 0, longestWithdrawal = 0;
                float started = Time.time;
                while (match.Phase == MatchPhase.Running && Time.time - started < 18)
                {
                    yield return new WaitForFixedUpdate();
                    if (Time.time - started > 0.2f && defender.alive && Supported(defender) && Supported(pusher))
                    {
                        pressureSteps++;
                        if (Vector3.Dot(pusher.motor.DesiredDirection, direction) < 0.1f) retreatSteps++;
                        Vector3 offset = Vector3.ProjectOnPlane(defender.body.RigidBody.position - pusher.body.RigidBody.position, Vector3.up);
                        maximumGap = Mathf.Max(maximumGap, offset.magnitude - defender.body.Radius - pusher.body.Radius);
                        bool voluntarilyRetreating = Vector3.Dot(pusher.motor.DesiredDirection, offset.normalized) < -0.2f
                            && Vector3.Dot(pusher.body.RigidBody.linearVelocity, offset.normalized) < -0.2f;
                        withdrawalTime = voluntarilyRetreating ? withdrawalTime + Time.fixedDeltaTime : 0;
                        longestWithdrawal = Mathf.Max(longestWithdrawal, withdrawalTime);
                    }
                }
                checks.Add($"Pressure case {i}: duration={Time.time - started:F2}s, opposing input={retreatSteps}/{pressureSteps}, maximum gap={maximumGap:F2}m, voluntary withdrawal={longestWithdrawal:F2}s, result={match.Result}");
                if (match.Phase != MatchPhase.Results)
                    checks.Add($"Pressure snapshot: attacker={pusher.body.RigidBody.position}, velocity={pusher.body.RigidBody.linearVelocity}, input={pusher.motor.DesiredDirection}; defender={defender.body.RigidBody.position}, velocity={defender.body.RigidBody.linearVelocity}");
                // Negative force may brake forward velocity and impacts may separate
                // the balls. Reject sustained voluntary movement away from the opponent.
                if (!Check(match.Phase == MatchPhase.Results && pusher.alive && !defender.alive && pressureSteps > 10
                    && longestWithdrawal < 0.75f, $"Maintains winning pressure against resisting edge opponent, case {i}")) yield break;
            }

            Setup(new Vector3(14, 0, 0), new Vector3(8, 0, 0));
            match.Participants[0].body.RigidBody.isKinematic = true;
            var observer = match.Participants[1];
            observer.body.RigidBody.linearVelocity = Vector3.right * 2f;
            bool stays = true;
            float watchStart = Time.time;
            while (Time.time - watchStart < 6)
            {
                yield return new WaitForFixedUpdate();
                stays &= observer.alive && Supported(observer);
            }
            if (!Check(stays && observer.body.RigidBody.linearVelocity.magnitude < 0.3f, "Does not follow an unsupported opponent off the arena")) yield break;
            foreach (int count in new[] { 1, 5 })
            {
                match.StartMatch(count); match.BeginRunning();
                match.Participants[0].body.GetComponent<PlayerInputSource>().enabled = false;
                match.Participants[0].motor.controlEnabled = false;
                var trackers = new List<BotContactProbe>();
                foreach (var p in match.Participants) trackers.Add(p.body.gameObject.AddComponent<BotContactProbe>());
                float started = Time.time;
                float nextWindow = started + 30;
                int previousContacts = 0;
                bool ongoingCombat = true;
                while (match.Phase == MatchPhase.Running && Time.time - started < 120)
                {
                    yield return new WaitForSeconds(0.5f);
                    if (Time.time >= nextWindow)
                    {
                        int total = 0;
                        foreach (var tracker in trackers) total += tracker.Contacts;
                        ongoingCombat &= total - previousContacts >= 2;
                        checks.Add($"Combat window: {count} bots, contacts={total - previousContacts}, remaining={match.Remaining}");
                        previousContacts = total; nextWindow += 30;
                    }
                }
                int untouchedFalls = 0, contacts = 0;
                for (int i = 1; i < match.Participants.Count; i++)
                {
                    contacts += trackers[i].Contacts;
                    if (!match.Participants[i].alive && !trackers[i].Hit) untouchedFalls++;
                }
                checks.Add($"Natural {count}-bot match: time={Time.time - started:F1}s, contacts={contacts}, falls before any contact={untouchedFalls}, remaining={match.Remaining}, result={match.Result}");
                bool result = count == 1 ? match.Phase == MatchPhase.Results && match.Participants[1].alive && !match.Participants[0].alive :
                    match.Phase == MatchPhase.Results || ongoingCombat && contacts >= 20 && match.Remaining < count + 1;
                if (!Check(result && trackers[0].Hit && untouchedFalls == 0,
                    count == 1 ? "Single bot defeats idle player naturally" :
                    "Five bots sustain combat and cause eliminations without unopposed suicide")) yield break;
            }
            Finish();
        }

        private void Setup(Vector3 playerPosition, Vector3 botPosition)
        {
            match.StartMatch(1);
            var player = match.Participants[0]; var bot = match.Participants[1];
            player.body.GetComponent<PlayerInputSource>().enabled = false;
            playerPosition.y += player.body.Radius; botPosition.y += bot.body.Radius;
            player.body.ResetAt(playerPosition); bot.body.ResetAt(botPosition);
            Physics.SyncTransforms();
            match.BeginRunning();
            player.motor.controlEnabled = false;
        }
        private bool Supported(MatchParticipant p) => ArenaQuery.TrySupport(gameObject.scene.GetPhysicsScene(),
            p.body.RigidBody.position, p.body.Radius, p.motor.supportLayers, 0.14f, 0.6f, out _);
        private void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = message + "\n" + stack;
        }
        private bool Check(bool passed, string name)
        {
            checks.Add((passed ? "PASS " : "FAIL ") + name);
            if (!passed) { error = name; Finish(); }
            return passed;
        }
        private void Finish()
        {
            Time.timeScale = 1;
            File.WriteAllText(Path.Combine(output, "bot-validation.txt"), "passed=" + string.IsNullOrEmpty(error) + "\n" + string.Join("\n", checks) + "\nerror=" + error);
            Application.logMessageReceived -= Log;
            Application.Quit(string.IsNullOrEmpty(error) ? 0 : 1);
        }
    }
    internal sealed class BotContactProbe : MonoBehaviour
    {
        public bool Hit { get; private set; }
        public int Contacts { get; private set; }
        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.GetComponent<SnowballBody>() != null) { Hit = true; Contacts++; }
        }
    }
}
#endif
