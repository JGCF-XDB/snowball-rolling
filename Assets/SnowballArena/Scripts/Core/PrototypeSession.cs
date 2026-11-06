using UnityEngine;
using UnityEngine.InputSystem;

namespace SnowballArena
{
    // Temporary single-ball practice flow. MatchController replaces this in milestone B.
    public sealed class PrototypeSession : MonoBehaviour
    {
        public SnowballBody player;
        public ArenaDefinition arena;
        public bool HasFallen { get; private set; }
        private GUIStyle title, label, small, button;
        private Font font;

        private void Update()
        {
            if (player.RigidBody.position.y < arena.fallHeight)
            {
                HasFallen = true;
                player.GetComponent<SnowballMotor>().controlEnabled = false;
            }
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetPractice();
        }

        public void ResetPractice()
        {
            player.ResetAt(arena.spawnPosition);
            var motor = player.GetComponent<SnowballMotor>();
            motor.SetMoveDirection(Vector3.zero);
            motor.controlEnabled = true;
            HasFallen = false;
        }

        private void OnGUI()
        {
            if (title == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Segoe UI" }, 24);
                title = Style(30, FontStyle.Bold, Color.white);
                label = Style(18, FontStyle.Normal, new Color(0.85f, 0.95f, 1f));
                small = Style(14, FontStyle.Normal, new Color(0.56f, 0.77f, 0.86f));
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 18 };
            }
            var old = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            float width = Screen.width / scale, height = Screen.height / scale;
            Panel(new Rect(24, 22, 310, 105));
            GUI.Label(new Rect(44, 34, 280, 40), "雪山 · 滚雪球", title);
            GUI.Label(new Rect(44, 83, 280, 25), "操控练习  /  单雪球原型", small);
            Panel(new Rect(24, height - 80, 450, 56));
            GUI.Label(new Rect(44, height - 66, 420, 30), "W A S D  移动方向     ·     R  重新练习", label);
            if (HasFallen)
            {
                var rect = new Rect(width / 2f - 180, height / 2f - 95, 360, 190);
                Panel(rect);
                GUI.Label(new Rect(rect.x + 34, rect.y + 22, 300, 40), "已离开平台", title);
                GUI.Label(new Rect(rect.x + 34, rect.y + 77, 300, 30), "注意惯性，提前调整方向。", label);
                if (GUI.Button(new Rect(rect.x + 34, rect.y + 125, 292, 42), "重新练习  ·  R", button)) ResetPractice();
            }
            GUI.matrix = old;
        }

        private GUIStyle Style(int size, FontStyle weight, Color color)
        {
            var style = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = weight };
            style.normal.textColor = color;
            return style;
        }

        private static void Panel(Rect rect)
        {
            var old = GUI.color;
            GUI.color = new Color(0.025f, 0.08f, 0.12f, 0.9f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = new Color(0.35f, 0.88f, 0.96f, 1f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3, rect.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void OnDestroy() { if (font != null) Destroy(font); }
    }
}
