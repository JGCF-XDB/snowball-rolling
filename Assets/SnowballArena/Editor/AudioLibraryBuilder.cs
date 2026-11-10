using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SnowballArena.Editor
{
    // Writes the clip table the runtime feedback layer loads from Resources.
    // Re-run after adding or replacing files under Assets/SnowballArena/Audio.
    public static class AudioLibraryBuilder
    {
        private const string AudioRoot = PrototypeBuilder.Root + "/Audio";
        private const string AssetPath = "Assets/Resources/SnowballAudioLibrary.asset";

        [MenuItem("Snowball Arena/Build Audio Library")]
        public static void Build()
        {
            Directory.CreateDirectory("Assets/Resources");
            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AssetPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<AudioLibrary>();
                AssetDatabase.CreateAsset(library, AssetPath);
            }
            library.softHits = Load("Impact/impactSoft_medium_000", "Impact/impactSoft_medium_001", "Impact/impactSoft_medium_002");
            library.hardHits = Load("Impact/impactSoft_heavy_000", "Impact/impactSoft_heavy_001", "Impact/impactSoft_heavy_002",
                "Impact/impactPunch_heavy_000", "Impact/impactPunch_heavy_001");
            library.groundHits = Load("Roll/footstep_snow_000", "Roll/footstep_snow_001", "Roll/footstep_snow_002", "Roll/footstep_snow_003");
            library.rollLoop = LoadOne("Roll/roll_snow_loop");
            library.click = LoadOne("UI/click1");
            library.hover = LoadOne("UI/rollover1");
            library.confirm = LoadOne("Interface/confirmation_001");
            library.countdownTick = LoadOne("Interface/tick_001");
            library.victory = LoadOne("Jingle/jingle_victory");
            library.defeat = LoadOne("Jingle/jingle_defeat");
            library.musicMenu = LoadOne("Music/menu_theme");
            library.musicMatch = LoadOne("Music/match_theme");
            library.cannonDeploy = LoadOne("Cannon/cannon_deploy");
            library.cannonWarning = LoadOne("Cannon/cannon_warning");
            library.cannonFire = LoadOne("Cannon/cannon_fire");
            library.cannonFlight = LoadOne("Cannon/cannon_flight");
            library.cannonExplosion = LoadOne("Cannon/cannon_explosion");
            library.knockdown = LoadOne("Reward/knockdown");
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Audio library built: {AssetPath} (soft {library.softHits.Length}, hard {library.hardHits.Length}, " +
                $"ground {library.groundHits.Length})");
        }

        private static AudioClip[] Load(params string[] names)
        {
            var clips = new List<AudioClip>();
            foreach (string name in names)
            {
                AudioClip clip = LoadOne(name);
                if (clip != null) clips.Add(clip);
                else Debug.LogWarning("Missing audio clip: " + name);
            }
            return clips.ToArray();
        }

        private static AudioClip LoadOne(string name)
        {
            foreach (string extension in new[] { ".ogg", ".wav" })
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioRoot}/{name}{extension}");
                if (clip != null) return clip;
            }
            Debug.LogWarning("Missing audio clip: " + name);
            return null;
        }
    }
}
