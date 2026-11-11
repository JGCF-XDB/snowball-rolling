using System.Collections.Generic;
using UnityEngine;

namespace SnowballArena
{
    // Presentation only. MatchController still owns the page flow and all match rules.
    internal sealed class WinterMenuView : System.IDisposable
    {
        public enum ResultAction { None, Restart, Menu }
        private static readonly Color Ink = new Color(.19f, .37f, .53f);
        private static readonly Color Outline = new Color(.34f, .50f, .66f);
        private static readonly Color Snow = new Color(.99f, .997f, 1f, .97f);
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private readonly Dictionary<Vector2Int, Texture2D> panels = new Dictionary<Vector2Int, Texture2D>();
        private readonly Texture2D wordmark, keycap;
        private readonly Texture2D[] lessons;
        private readonly HandwrittenTextAtlas lettering;
        private readonly GUIStyle heading, lead, subtitle, caption, number, numberCaption, hint, noticeStyle;
        private readonly GUIStyle primary, secondary, primaryText, secondaryText, step, keyText, lessonText, countdown;

        public WinterMenuView(Font font)
        {
            lettering = new HandwrittenTextAtlas();
            wordmark = Resources.Load<Texture2D>("UI/GameTitle");
            lessons = new[] { Resources.Load<Texture2D>("Tutorial/Movement"),
                Resources.Load<Texture2D>("Tutorial/Cannon"), Resources.Load<Texture2D>("Tutorial/Victory") };
            heading = Text(font, 57, FontStyle.Bold, Color.white);
            lead = Text(font, 27, FontStyle.Bold, Ink);
            subtitle = Text(font, 23, FontStyle.Normal, Ink);
            caption = Text(font, 22, FontStyle.Bold, Ink);
            number = Text(font, 92, FontStyle.Bold, Ink);
            numberCaption = Text(font, 24, FontStyle.Bold, Ink);
            hint = Text(font, 19, FontStyle.Normal, Ink);
            noticeStyle = Text(font, 20, FontStyle.Bold, new Color(.68f, .25f, .18f));
            noticeStyle.wordWrap = true;
            keyText = Text(font, 20, FontStyle.Bold, Outline);
            lessonText = Text(font, 27, FontStyle.Bold, Ink);
            lessonText.wordWrap = true;
            countdown = Text(font, 124, FontStyle.Bold, Color.white);
            primary = Button(font, 34);
            secondary = Button(font, 26);
            primaryText = Text(font, 34, FontStyle.Bold, Ink);
            secondaryText = Text(font, 26, FontStyle.Bold, Ink);
            step = Button(font, 44);
            keycap = Surface(64, 40, 13, new Color(.82f, .88f, .95f), false);
        }

        public bool Draw(float width, float height, ref int bots, string notice, MenuPage page)
        {
            if (page == MenuPage.Tutorial) return DrawTutorial(width, height);
            Color old = GUI.color;
            GUI.color = Color.white;
            bool result = page == MenuPage.Home ? DrawHome(width, height) : DrawOpponents(width, height, ref bots, notice);
            GUI.color = old;
            return result;
        }

        private bool DrawHome(float width, float height)
        {
            float center = width / 2, top = (height - 390) / 2;
            var logoRect = new Rect(center - 300, top + 8, 600, 141.3f);
            if (wordmark != null)
            {
                // Visible ink bounds of the unmodified transparent source, excluding its safety margin.
                var uv = new Rect(110f / 2172f, 138f / 724f, 1945f / 2172f, 458f / 724f);
                GUI.DrawTextureWithTexCoords(logoRect, wordmark, uv, true);
            }
            else Outlined(logoRect, MatchController.GameTitle, heading, 2f);
            ArtLabel(new Rect(center - 380, top + 174, 760, 40), "尽情地碰撞吧！", lead, 29);
            ArtLabel(new Rect(center - 400, top + 218, 800, 36), "小心周围召唤的炮弹~", subtitle, 25);
            return ActionButton(new Rect(center - 180, top + 282, 360, 82), "开始游戏", "Enter");
        }

        private bool DrawOpponents(float width, float height, ref int bots, string notice)
        {
            float center = width / 2, top = (height - 500) / 2;
            lettering.Draw(new Rect(center - 400, top, 800, 90), "选择你的对手", 86);
            ArtLabel(new Rect(center - 400, top + 94, 800, 36), "准备好，向雪山之巅出发！", subtitle, 26);
            ArtLabel(new Rect(center - 200, top + 154, 400, 32), "机器人数量", caption, 27);
            bool oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && bots > 0;
            var minusRect = new Rect(center - 157, top + 208, 68, 68);
            if (GUI.Button(minusRect, GUIContent.none, step)) bots--;
            lettering.Draw(new Rect(minusRect.x + 18, minusRect.y + 18, 32, 32), "−", 22);
            GUI.enabled = oldEnabled && bots < 15;
            var plusRect = new Rect(center + 89, top + 208, 68, 68);
            if (GUI.Button(plusRect, GUIContent.none, step)) bots++;
            lettering.Draw(plusRect, "+", 32);
            GUI.enabled = oldEnabled;
            lettering.Number(new Rect(center - 80, top + 190, 160, 105), bots, false, 91);
            ArtLabel(new Rect(center - 160, top + 292, 320, 38), bots == 0 ? "自由练习" : "位对手", numberCaption, 27);
            bool start = ActionButton(new Rect(center - 190, top + 348, 380, 82), bots == 0 ? "开始练习" : "现在开始吧！", "Enter");
            ArtLabel(new Rect(center - 450, top + 444, 900, 54),
                notice ?? "← → 调整人数 · 0 人为自由练习", notice == null ? hint : noticeStyle, 20);
            EscapeHint(height);
            return start;
        }

        private bool DrawTutorial(float width, float height)
        {
            Fill(new Rect(0, 0, width, height), new Color(.91f, .96f, 1f, .66f));
            float top = (height - 600) / 2;
            lettering.Draw(new Rect(width / 2 - 440, top + 8, 880, 110), "雪山生存指南", 100);
            const float cardWidth = 350, gap = 20;
            float left = (width - cardWidth * 3 - gap * 2) / 2;
            string[] copy = { "WASD 控制\n雪球方向", "注意躲避外侧\n袭来的炮弹！", "最后留在场上的\n雪球获得胜利！" };
            for (int i = 0; i < lessons.Length; i++)
            {
                var panel = new Rect(left + i * (cardWidth + gap), top + 190, cardWidth, 318);
                Panel(panel);
                if (lessons[i] != null)
                    GUI.DrawTexture(new Rect(panel.x + 19, panel.y + 23, 312, 175.5f), lessons[i], ScaleMode.ScaleToFit);
                ArtLabel(new Rect(panel.x + 16, panel.y + 208, 318, 88), copy[i], lessonText, 82);
                var badge = new Rect(panel.x + 8, panel.y - 18, 56, 48);
                Panel(badge);
                lettering.Number(badge, i + 1, false, 28);
            }
            ArtLabel(new Rect(width / 2 - 370, top + 558, 740, 44), "点击任意处继续 · Enter", lead, 29);
            EscapeHint(height);
            // Consume a release, as before, so the opening click cannot skip the tutorial.
            if (Event.current.type != EventType.MouseUp) return false;
            Event.current.Use();
            return true;
        }

        public void DrawCountdown(float width, float height, float remaining)
        {
            lettering.Number(new Rect(width / 2 - 140, height / 2 - 100, 280, 200),
                Mathf.CeilToInt(remaining), true, 142);
        }

        public ResultAction DrawResults(float width, float height, string result)
        {
            Fill(new Rect(0, 0, width, height), new Color(.05f, .16f, .25f, .20f));
            float center = width / 2, top = height / 2 - 154;
            var titleRect = new Rect(center - 440, top, 880, 110);
            if (!lettering.Winner(titleRect, result)) lettering.Draw(titleRect, result, 88);
            if (ActionButton(new Rect(center - 180, top + 132, 360, 78), "再来一局", "R")) return ResultAction.Restart;
            if (ActionButton(new Rect(center - 154, top + 226, 308, 64), "返回菜单", "Esc", true)) return ResultAction.Menu;
            return ResultAction.None;
        }

        private bool ActionButton(Rect rect, string label, string shortcut, bool quiet = false)
        {
            GUIStyle style = quiet ? secondary : primary;
            bool clicked = GUI.Button(rect, GUIContent.none, style);
            ArtLabel(new Rect(rect.x + 16, rect.y + 2, rect.width - 112, rect.height - 4), label,
                quiet ? secondaryText : primaryText, quiet ? 32 : 42);
            var keyRect = new Rect(rect.xMax - 96, rect.center.y - 20, 76, 40);
            GUI.DrawTexture(keyRect, keycap);
            ArtLabel(new Rect(keyRect.x + 8, keyRect.y + 7, keyRect.width - 16, keyRect.height - 14), shortcut, keyText, 23);
            return clicked;
        }

        private void EscapeHint(float height)
        {
            ArtLabel(new Rect(28, height - 49, 240, 32), "Esc 返回首页", hint, 21);
        }

        private void ArtLabel(Rect rect, string value, GUIStyle fallback, float height)
        {
            if (!lettering.Draw(rect, value, height)) GUI.Label(rect, value, fallback);
        }

        private void Panel(Rect rect)
        {
            var size = new Vector2Int(Mathf.RoundToInt(rect.width), Mathf.RoundToInt(rect.height));
            if (!panels.TryGetValue(size, out var texture))
            {
                texture = Surface(size.x, size.y, Mathf.Min(27, rect.height * .43f), Snow, true);
                panels.Add(size, texture);
            }
            GUI.DrawTexture(rect, texture);
        }

        private static GUIStyle Text(Font font, int size, FontStyle weight, Color color)
        {
            var style = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = weight,
                alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip, richText = false };
            style.normal.textColor = color;
            return style;
        }

        private GUIStyle Button(Font font, int size)
        {
            var style = Text(font, size, FontStyle.Bold, Ink);
            style.border = new RectOffset(18, 18, 18, 18);
            style.normal.background = Surface(72, 72, 18, Snow, true);
            style.hover.background = Surface(72, 72, 18, new Color(.92f, .97f, 1f), true);
            style.active.background = Surface(72, 72, 18, new Color(.83f, .91f, .98f), true);
            style.focused.background = style.hover.background;
            style.hover.textColor = style.active.textColor = style.focused.textColor = Ink;
            return style;
        }

        private static void Outlined(Rect rect, string value, GUIStyle style, float stroke)
        {
            Color old = style.normal.textColor;
            style.normal.textColor = Outline;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * stroke;
                GUI.Label(new Rect(rect.position + offset, rect.size), value, style);
            }
            style.normal.textColor = Color.white;
            GUI.Label(rect, value, style);
            style.normal.textColor = old;
        }

        private Texture2D Surface(int width, int height, float radius, Color fill, bool outlined)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { name = "Handwritten UI surface", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = Mathf.Abs(x + .5f - width / 2f) - (width / 2f - radius - 2);
                float dy = Mathf.Abs(y + .5f - height / 2f) - (height / 2f - radius - 2);
                float distance = new Vector2(Mathf.Max(dx, 0), Mathf.Max(dy, 0)).magnitude
                    + Mathf.Min(Mathf.Max(dx, dy), 0) - radius;
                if (outlined) distance += Mathf.Sin(x * .054f + .6f) * .45f + Mathf.Sin(y * .063f) * .45f;
                var color = outlined ? Color.Lerp(fill, Outline, Mathf.Clamp01(distance + 2.2f)) : fill;
                color.a *= Mathf.Clamp01(.5f - distance);
                pixels[y * width + x] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            textures.Add(texture);
            return texture;
        }

        private static void Fill(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public void Dispose()
        {
            foreach (var texture in textures) Object.Destroy(texture);
            textures.Clear();
            panels.Clear();
        }
    }
}
