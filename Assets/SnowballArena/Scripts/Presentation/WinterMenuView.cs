using System.Collections.Generic;
using UnityEngine;

namespace SnowballArena
{
    // Shared winter artwork for the menu, HUD and results. Textures are cached per size.
    internal sealed class WinterMenuView : System.IDisposable
    {
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private readonly Dictionary<Vector2Int, Texture2D> panels = new Dictionary<Vector2Int, Texture2D>();
        private readonly Texture2D card, inset, snow;
        private readonly GUIStyle brand, title, subtitle, caption, count, hint, noticeStyle, stepButton, startButton;
        private static readonly Color Ink = new Color(0.10f, 0.25f, 0.34f);
        public GUIStyle HudHeading { get; }
        public GUIStyle HudSmall { get; }
        public GUIStyle Hazard { get; }
        public GUIStyle Countdown { get; }
        public GUIStyle ResultTitle => title;
        public GUIStyle PrimaryButton => startButton;
        public GUIStyle SecondaryButton { get; }

        public WinterMenuView(Font font)
        {
            card = Surface(520, 448, 26, new Color(0.96f, 0.99f, 1f), new Color(0.74f, 0.87f, 0.94f), true);
            inset = Surface(64, 64, 12, new Color(0.76f, 0.87f, 0.93f), new Color(0.88f, 0.95f, 0.98f));
            snow = Surface(32, 32, 16, Color.white, Color.white);
            brand = Text(font, 17, FontStyle.Bold, new Color(0.27f, 0.46f, 0.56f));
            title = Text(font, 38, FontStyle.Bold, Ink);
            subtitle = Text(font, 21, FontStyle.Normal, new Color(0.25f, 0.43f, 0.53f));
            caption = Text(font, 16, FontStyle.Normal, new Color(0.30f, 0.47f, 0.57f));
            count = Text(font, 25, FontStyle.Bold, Ink);
            hint = Text(font, 14, FontStyle.Normal, new Color(0.29f, 0.45f, 0.54f));
            noticeStyle = Text(font, 14, FontStyle.Bold, new Color(0.62f, 0.22f, 0.12f));
            noticeStyle.wordWrap = true;
            stepButton = Button(font, 27, false);
            startButton = Button(font, 23, true);
            SecondaryButton = Button(font, 20, false);
            HudHeading = Text(font, 28, FontStyle.Bold, Ink);
            HudHeading.alignment = TextAnchor.MiddleLeft;
            HudSmall = Text(font, 16, FontStyle.Normal, new Color(0.22f, 0.39f, 0.49f));
            HudSmall.alignment = TextAnchor.MiddleLeft;
            Hazard = Text(font, 22, FontStyle.Bold, new Color(0.61f, 0.20f, 0.07f));
            Countdown = Text(font, 64, FontStyle.Bold, new Color(0.61f, 0.20f, 0.07f));
        }

        public void Panel(Rect rect, bool decorate = true)
        {
            var size = new Vector2Int(Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height));
            if (!panels.TryGetValue(size, out var texture))
            {
                texture = Surface(size.x, size.y, Mathf.Min(22, rect.height / 3),
                    new Color(0.96f, 0.99f, 1f), new Color(0.74f, 0.87f, 0.94f), true);
                panels.Add(size, texture);
            }
            TintTexture(new Rect(rect.x + 2, rect.y + 5, rect.width, rect.height), texture, new Color(0.03f, 0.13f, 0.22f, 0.24f));
            GUI.DrawTexture(rect, texture);
            if (!decorate) return;
            TintTexture(new Rect(rect.x + 24, rect.y - 3, 63, 8), snow, Color.white);
            TintTexture(new Rect(rect.x + 39, rect.y - 5, 28, 10), snow, Color.white);
            Snowflake(new Vector2(rect.xMax - 18, rect.y + 19), 8, new Color(0.41f, 0.65f, 0.77f, 0.48f));
        }

        public void DimBackground(float width, float height)
        {
            Fill(new Rect(0, 0, width, height), new Color(0.06f, 0.20f, 0.30f, 0.32f));
        }

        public bool Draw(float width, float height, ref int bots, string notice)
        {
            Color oldColor = GUI.color;
            bool oldEnabled = GUI.enabled;
            GUI.color = Color.white;
            Fill(new Rect(0, 0, width, height), new Color(0.06f, 0.20f, 0.30f, 0.32f));
            // Quiet frost at the edges leaves the mountain scene visible.
            for (int i = 0; i < 18; i++)
            {
                float alpha = (1f - i / 18f) * 0.014f;
                Fill(new Rect(i * 3, 0, 3, height), new Color(0.81f, 0.94f, 1f, alpha * 8));
                Fill(new Rect(width - i * 3 - 3, 0, 3, height), new Color(0.81f, 0.94f, 1f, alpha * 8));
                Fill(new Rect(0, i * 3, width, 3), new Color(0.81f, 0.94f, 1f, alpha * 8));
                Fill(new Rect(0, height - i * 3 - 3, width, 3), new Color(0.81f, 0.94f, 1f, alpha * 8));
            }
            Border(new Rect(22, 22, width - 44, height - 44), new Color(0.88f, 0.97f, 1f, 0.45f));
            Snowflake(new Vector2(44, 44), 13, new Color(0.92f, 0.98f, 1f, 0.85f));
            Snowflake(new Vector2(width - 44, height - 44), 13, new Color(0.92f, 0.98f, 1f, 0.85f));
            for (int i = 0; i < 26; i++)
            {
                float x = Mathf.Repeat(i * 137.7f + Mathf.Sin(Time.unscaledTime * 0.24f + i) * 16, width);
                float y = Mathf.Repeat(i * 73.3f + Time.unscaledTime * (7 + i % 5), height);
                float size = 2 + i % 4;
                TintTexture(new Rect(x, y, size, size), snow, new Color(1, 1, 1, 0.26f + i % 3 * 0.12f));
            }

            var r = new Rect((width - 520) / 2, (height - 448) / 2, 520, 448);
            TintTexture(new Rect(r.x - 5, r.y + 9, r.width + 10, r.height + 10), card, new Color(0.03f, 0.13f, 0.22f, 0.24f));
            GUI.DrawTexture(r, card);
            // Small snow pillows sit on the rim rather than behind the copy.
            TintTexture(new Rect(r.x + 32, r.y - 5, 104, 13), snow, new Color(0.97f, 1f, 1f));
            TintTexture(new Rect(r.x + 55, r.y - 9, 43, 18), snow, Color.white);
            TintTexture(new Rect(r.xMax - 114, r.y - 4, 67, 11), snow, Color.white);
            Snowflake(new Vector2(r.x + 33, r.y + 50), 15, new Color(0.47f, 0.69f, 0.80f, 0.75f));
            Snowflake(new Vector2(r.xMax - 36, r.yMax - 40), 18, new Color(0.41f, 0.65f, 0.77f, 0.45f));
            GUI.Label(new Rect(r.x + 60, r.y + 31, 400, 30), "雪山 · 滚雪球", brand);
            GUI.Label(new Rect(r.x + 24, r.y + 82, 472, 58), "尽情地碰撞吧！", title);
            GUI.Label(new Rect(r.x + 24, r.y + 143, 472, 36), "小心周围召唤的炮弹~", subtitle);
            Fill(new Rect(r.x + 100, r.y + 200, 320, 1), new Color(0.47f, 0.67f, 0.77f, 0.38f));
            GUI.Label(new Rect(r.x + 40, r.y + 214, 440, 25), "机器人数量", caption);
            GUI.DrawTexture(new Rect(r.x + 104, r.y + 249, 312, 55), inset);
            GUI.enabled = oldEnabled && bots > 0;
            if (GUI.Button(new Rect(r.x + 40, r.y + 249, 54, 55), "−", stepButton)) bots--;
            GUI.enabled = oldEnabled;
            GUI.Label(new Rect(r.x + 108, r.y + 250, 304, 52), bots == 0 ? "自由练习" : bots + "  位对手", count);
            GUI.enabled = oldEnabled && bots < 15;
            if (GUI.Button(new Rect(r.xMax - 94, r.y + 249, 54, 55), "+", stepButton)) bots++;
            GUI.enabled = oldEnabled;
            bool start = GUI.Button(new Rect(r.x + 40, r.y + 326, 440, 58), bots == 0 ? "开始练习   ·   Enter" : "开始游戏   ·   Enter", startButton);
            GUI.Label(new Rect(r.x + 30, r.y + 397, 460, 35), notice ?? "← → 调整人数    WASD 控制红色描边雪球", notice == null ? hint : noticeStyle);
            GUI.color = oldColor;
            GUI.enabled = oldEnabled;
            return start;
        }

        private static GUIStyle Text(Font font, int size, FontStyle weight, Color color)
        {
            var style = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = weight, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = color;
            return style;
        }

        private GUIStyle Button(Font font, int size, bool primary)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                font = font, fontSize = size, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, border = new RectOffset(15, 15, 15, 15),
                padding = new RectOffset(8, 8, 2, 2)
            };
            Color top = primary ? new Color(0.22f, 0.51f, 0.66f) : new Color(0.98f, 1f, 1f);
            Color bottom = primary ? new Color(0.10f, 0.33f, 0.48f) : new Color(0.74f, 0.87f, 0.94f);
            style.normal.background = Surface(64, 64, 12, top, bottom);
            style.hover.background = Surface(64, 64, 12, Color.Lerp(top, Color.white, 0.14f), Color.Lerp(bottom, Color.white, 0.14f));
            style.active.background = Surface(64, 64, 12, bottom, top);
            style.focused.background = style.hover.background;
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = primary ? Color.white : Ink;
            return style;
        }

        private Texture2D Surface(int width, int height, float radius, Color top, Color bottom, bool frost = false)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Winter menu surface", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - width / 2f) - (width / 2f - radius);
                float dy = Mathf.Abs(y + 0.5f - height / 2f) - (height / 2f - radius);
                float distance = new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude + Mathf.Min(Mathf.Max(dx, dy), 0) - radius;
                Color color = Color.Lerp(bottom, top, y / (height - 1f));
                if (frost)
                {
                    float grain = ((x * 17 + y * 131 + x * y * 3) % 31) / 30f - 0.5f;
                    color += new Color(grain * 0.018f, grain * 0.018f, grain * 0.018f, 0);
                    if (distance > -3) color = new Color(0.97f, 1f, 1f);
                    else if (distance > -5) color = new Color(0.51f, 0.73f, 0.84f);
                    else if (distance < -10 && distance > -11.5f) color = new Color(0.97f, 1f, 1f, 0.95f);
                }
                else if (distance > -1.5f) color = Color.Lerp(color, Color.white, 0.35f);
                color.a *= Mathf.Clamp01(0.5f - distance);
                pixels[y * width + x] = color;
            }
            texture.SetPixels(pixels); texture.Apply(false, true); textures.Add(texture);
            return texture;
        }

        private static void Fill(Rect rect, Color color) { TintTexture(rect, Texture2D.whiteTexture, color); }
        private static void TintTexture(Rect rect, Texture2D texture, Color color)
        {
            Color previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, texture); GUI.color = previous;
        }
        private static void Border(Rect r, Color color)
        {
            Fill(new Rect(r.x, r.y, r.width, 1), color); Fill(new Rect(r.x, r.yMax - 1, r.width, 1), color);
            Fill(new Rect(r.x, r.y, 1, r.height), color); Fill(new Rect(r.xMax - 1, r.y, 1, r.height), color);
        }
        private static void Line(Vector2 from, Vector2 to, Color color)
        {
            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg, from);
            Fill(new Rect(from.x, from.y - 0.7f, Vector2.Distance(from, to), 1.4f), color);
            GUI.matrix = previous;
        }
        private static void Snowflake(Vector2 center, float radius, Color color)
        {
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var side = new Vector2(-direction.y, direction.x);
                Line(center, center + direction * radius, color);
                Vector2 joint = center + direction * radius * 0.62f;
                Line(joint, joint - direction * radius * 0.23f + side * radius * 0.23f, color);
                Line(joint, joint - direction * radius * 0.23f - side * radius * 0.23f, color);
            }
        }
        public void Dispose()
        {
            foreach (var texture in textures) Object.Destroy(texture);
            textures.Clear();
            panels.Clear();
        }
    }
}
