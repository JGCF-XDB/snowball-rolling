using System;
using System.Collections.Generic;
using UnityEngine;

namespace SnowballArena
{
    // The approved lettering is artwork, not a system-font approximation.
    // UVs address original transparent sheets; no bitmap resampling occurs here.
    public sealed class HandwrittenTextAtlas
    {
        [Serializable] private sealed class Glyph
        {
            public string key, sheet;
            public UvBounds uv;
            public float aspect;
        }
        [Serializable] private sealed class UvBounds { public float x, y, width, height; }
        [Serializable] private sealed class GlyphSet { public Glyph[] glyphs; }
        private readonly Dictionary<string, Glyph> glyphs = new Dictionary<string, Glyph>();
        private readonly Dictionary<string, Texture2D> sheets = new Dictionary<string, Texture2D>();
        private readonly HashSet<string> missing = new HashSet<string>();
        public static int MissingCount { get; private set; }
        public int GlyphCount => glyphs.Count;

        public HandwrittenTextAtlas()
        {
            var manifest = Resources.Load<TextAsset>("UI/Handwriting/Lettering");
            if (manifest == null) throw new InvalidOperationException("Handwritten lettering manifest is missing.");
            var set = JsonUtility.FromJson<GlyphSet>(manifest.text);
            if (set == null || set.glyphs == null) throw new InvalidOperationException("Invalid lettering manifest.");
            foreach (var glyph in set.glyphs)
            {
                if (glyph.uv == null || glyph.uv.width <= 0 || glyph.uv.height <= 0 || glyph.aspect <= 0)
                    throw new InvalidOperationException("Invalid lettering bounds: " + glyph.key);
                glyphs.Add(glyph.key, glyph);
                if (sheets.ContainsKey(glyph.sheet)) continue;
                var image = Resources.Load<Texture2D>("UI/Handwriting/" + glyph.sheet);
                if (image == null) throw new InvalidOperationException("Lettering sheet is missing: " + glyph.sheet);
                sheets.Add(glyph.sheet, image);
            }
        }

        public bool Draw(Rect rect, string key, float maximumHeight = float.PositiveInfinity)
        {
            if (!glyphs.TryGetValue(key, out var glyph))
            {
                if (missing.Add(key)) { MissingCount++; Debug.LogWarning("Handwritten text asset is missing: " + key); }
                return false;
            }
            float height = Mathf.Min(rect.height, maximumHeight, rect.width / glyph.aspect);
            var target = new Rect(rect.center.x - height * glyph.aspect / 2, rect.center.y - height / 2,
                height * glyph.aspect, height);
            Color previous = GUI.color;
            if (!GUI.enabled) GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * .4f);
            GUI.DrawTextureWithTexCoords(target, sheets[glyph.sheet],
                new Rect(glyph.uv.x, glyph.uv.y, glyph.uv.width, glyph.uv.height), true);
            GUI.color = previous;
            return true;
        }

        public void Number(Rect rect, int value, bool outlined = false, float maximumHeight = float.PositiveInfinity)
        {
            var keys = new List<string>();
            foreach (char digit in value.ToString()) keys.Add((outlined ? "outline/" : "digit/") + digit);
            Sequence(rect, keys, maximumHeight, 4);
        }

        public bool Winner(Rect rect, string value)
        {
            var words = value.Split(' ');
            if (words.Length != 3 || words[0] != "雪球" || words[2] != "获胜") return false;
            var keys = new List<string> { "雪球" };
            foreach (char digit in words[1]) keys.Add("outline/" + digit);
            keys.Add("获胜");
            Sequence(rect, keys, 80, 12);
            return true;
        }

        private void Sequence(Rect rect, List<string> keys, float maximumHeight, float gap)
        {
            float aspect = 0;
            foreach (string key in keys)
            {
                if (!glyphs.TryGetValue(key, out var glyph)) { Draw(rect, key); return; }
                aspect += glyph.aspect;
            }
            float spaces = gap * Mathf.Max(0, keys.Count - 1);
            float height = Mathf.Min(rect.height, maximumHeight, (rect.width - spaces) / aspect);
            float left = rect.center.x - (height * aspect + spaces) / 2;
            foreach (string key in keys)
            {
                float width = glyphs[key].aspect * height;
                Draw(new Rect(left, rect.center.y - height / 2, width, height), key);
                left += width + gap;
            }
        }
    }
}
