using UnityEngine;
using UnityEngine.InputSystem;

namespace SnowballArena
{
    [RequireComponent(typeof(SnowballMotor))]
    public sealed class PlayerInputSource : MonoBehaviour
    {
        public InputActionAsset controls;
        public Transform cameraFrame;
        private InputActionAsset instance;
        private InputAction move;
        private SnowballMotor motor;

        private void OnEnable()
        {
            motor = GetComponent<SnowballMotor>();
            instance = Instantiate(controls);
            move = instance.FindAction("Gameplay/Move", true);
            move.Enable();
        }

        private void Update()
        {
            Vector2 input = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            motor.SetMoveDirection(ToWorldDirection(input, cameraFrame));
        }

        public static Vector3 ToWorldDirection(Vector2 input, Transform frame)
        {
            var forward = frame != null ? Vector3.ProjectOnPlane(frame.forward, Vector3.up).normalized : Vector3.forward;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            return Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && motor != null) motor.SetMoveDirection(Vector3.zero);
        }

        private void OnDisable()
        {
            move?.Disable();
            if (motor != null) motor.SetMoveDirection(Vector3.zero);
            if (instance != null) Destroy(instance);
        }
    }
}
