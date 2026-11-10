#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SnowballArena
{
    // Temporary diagnostic: run the built player with --audio-check <dir>.
    // Distinguishes "audio device never initialised" from "clips missing" and
    // "sources play but produce no time".
    internal sealed class AudioDiagnostic : MonoBehaviour
    {
        private string output;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "--audio-check");
            if (index >= 0 && index + 1 < args.Length)
                new GameObject("Audio diagnostic").AddComponent<AudioDiagnostic>().output = args[index + 1];
        }

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            Directory.CreateDirectory(output);
            var lines = new List<string>();
            yield return new WaitForSeconds(0.6f);

            lines.Add("outputSampleRate=" + AudioSettings.outputSampleRate);
            lines.Add("speakerMode=" + AudioSettings.speakerMode);
            lines.Add("driverCapabilities=" + AudioSettings.driverCapabilities);
            lines.Add("listenerVolume=" + AudioListener.volume);
            lines.Add("dspTime=" + AudioSettings.dspTime.ToString("F3"));
            lines.Add("cameras=" + Camera.allCamerasCount);
            var listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            var names = new List<string>();
            foreach (var listener in listeners) names.Add(listener.gameObject.name + (listener.enabled ? "" : "(disabled)"));
            lines.Add("listeners=" + listeners.Length + " [" + string.Join(",", names) + "]");

            var feedback = ArenaFeedback.Instance;
            lines.Add("feedback=" + (feedback != null));
            if (feedback == null || feedback.Clips == null) { Finish(lines); yield break; }
            var lib = feedback.Clips;
            lines.Add($"clips soft={lib.softHits.Length} hard={lib.hardHits.Length} ground={lib.groundHits.Length}");
            lines.Add($"clipLengths roll={ClipLength(lib.rollLoop)} click={ClipLength(lib.click)} " +
                $"victory={ClipLength(lib.victory)} menu={ClipLength(lib.musicMenu)} match={ClipLength(lib.musicMatch)}");

            feedback.UiClick();
            feedback.Impact(new Vector3(0f, 1f, 0f), 1f, false);
            yield return new WaitForSecondsRealtime(0.3f);
            lines.Add("dspTimeAfter=" + AudioSettings.dspTime.ToString("F3"));

            int playing = 0;
            foreach (var source in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (!source.isPlaying) continue;
                playing++;
                if (playing <= 4)
                    lines.Add($"playing clip={source.clip.name} vol={source.volume:F2} " +
                        $"time={source.time:F3} spatial={source.spatialBlend:F1} pos={source.transform.position}");
            }
            lines.Add("playingSources=" + playing);
            lines.Add("musicPlaying=" + (lib.musicMatch != null || lib.musicMenu != null));
            Finish(lines);
        }

        private static string ClipLength(AudioClip clip) => clip == null ? "null" : clip.length.ToString("F2");

        private void Finish(List<string> lines)
        {
            File.WriteAllText(Path.Combine(output, "audio-diagnostic.txt"), string.Join("\n", lines) + "\n");
            Application.Quit(0);
        }
    }
}
#endif
