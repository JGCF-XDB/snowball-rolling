using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SnowballArena.Editor
{
    internal static class PreviewCameraPitch
    {
        private const string PanelName = "snowball-pitch-panel";
        private static readonly System.Type GameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        [MenuItem("Snowball Arena/Preview Camera Pitch")]
        public static void Show()
        {
            var camera = Camera.main;
            if (camera == null || camera.orthographic)
            {
                Debug.LogWarning("Open the perspective Match camera before adjusting its pitch.");
                return;
            }

            var view = EditorWindow.GetWindow(GameViewType);
            var root = view.rootVisualElement;
            root.Q(PanelName)?.RemoveFromHierarchy();

            // The arena centre line crosses the optical axis at the orbit pivot.
            var axis = camera.transform.forward;
            var centrePlane = new Plane(Vector3.forward, Vector3.zero);
            if (!centrePlane.Raycast(new Ray(camera.transform.position, axis), out float distance)) return;
            var pivot = camera.transform.position + axis * distance;
            var heading = Vector3.ProjectOnPlane(axis, Vector3.up).normalized;

            var panel = new VisualElement { name = PanelName };
            panel.style.position = Position.Absolute;
            panel.style.left = 12;
            panel.style.bottom = 12;
            panel.style.width = 270;
            panel.style.paddingLeft = panel.style.paddingRight = 10;
            panel.style.paddingTop = panel.style.paddingBottom = 6;
            panel.style.backgroundColor = new Color(.07f, .12f, .17f, .94f);
            root.Add(panel);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var readout = new Label();
            readout.style.color = Color.white;
            readout.style.flexGrow = 1;
            panel.Add(row);
            row.Add(readout);
            var slider = new Slider(20f, 80f);
            row.Add(new Button(() => slider.value = 48f) { text = "48°" });
            row.Add(new Button(() => slider.value = 56f) { text = "56°" });
            row.Add(new Button(panel.RemoveFromHierarchy) { text = "×", tooltip = "关闭俯角面板" });
            panel.Add(slider);
            slider.SetValueWithoutNotify(camera.transform.eulerAngles.x);
            readout.text = $"俯视角度  {slider.value:F1}°";
            slider.RegisterValueChangedCallback(evt =>
            {
                if (!camera || camera != Camera.main)
                {
                    panel.RemoveFromHierarchy();
                    return;
                }
                float pitch = evt.newValue * Mathf.Deg2Rad;
                var forward = heading * Mathf.Cos(pitch) - Vector3.up * Mathf.Sin(pitch);
                camera.transform.SetPositionAndRotation(pivot - forward * distance, Quaternion.LookRotation(forward));
                readout.text = $"俯视角度  {evt.newValue:F1}°";
                EditorApplication.QueuePlayerLoopUpdate();
                view.Repaint();
            });
            view.Focus();
            view.Repaint();
        }
    }
}
