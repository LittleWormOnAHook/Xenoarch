using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMI.Xenoarch.Player
{
    /// <summary>
    /// Xenoarch first-person controller (CharacterController + Input System). The FPS_Starter_Pack provides arms and
    /// animations only, so locomotion is ours; arms are driven by <see cref="FirstPersonArms"/>.
    /// Gravity is per scene (Moon 1.62, Mars 3.71, ship/hub 9.81) and set on the scene instance.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class FirstPersonController : MonoBehaviour, IPlayerRig
    {
        [Header("References")]
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Transform head;

        [Header("Movement")]
        public float walkSpeed = 3.2f;
        public float sprintSpeed = 5.8f;
        public float acceleration = 12f;
        public float jumpHeight = 1.0f;
        [Tooltip("Downward acceleration in m/s^2 (Earth 9.81, Moon 1.62, Mars 3.71).")]
        public float gravity = 9.81f;
        [Range(0f, 1f)] public float airControl = 0.35f;

        [Header("Look")]
        public float mouseSensitivity = 0.08f;
        public float stickSensitivity = 140f;
        public float pitchLimit = 85f;

        CharacterController _cc;
        InputAction _move, _sprint, _jump;
        Vector3 _planarVelocity;
        float _verticalVelocity, _pitch;

        public Transform Root => transform;
        public Camera ViewCamera => viewCamera != null ? viewCamera : (viewCamera = GetComponentInChildren<Camera>());
        public bool InputEnabled { get; private set; } = true;

        /// <summary>Horizontal speed in m/s (arms animation, footsteps).</summary>
        public float PlanarSpeed => _planarVelocity.magnitude;
        /// <summary>Horizontal velocity in world space (body locomotion blend).</summary>
        public Vector3 PlanarVelocity => _planarVelocity;
        /// <summary>Vertical velocity in m/s (positive = rising).</summary>
        public float VerticalVelocity => _verticalVelocity;
        public float SprintSpeed => sprintSpeed;
        public bool IsGrounded => _cc != null && _cc.isGrounded;
        public bool IsSprinting { get; private set; }
        /// <summary>Raised on the frame a jump starts.</summary>
        public event System.Action Jumped;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (head == null && ViewCamera != null) head = ViewCamera.transform;
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddBinding("<Gamepad>/leftStick");
            _sprint = new InputAction("Sprint", InputActionType.Button);
            _sprint.AddBinding("<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");
            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");
        }

        void OnEnable()
        {
            _move.Enable(); _sprint.Enable(); _jump.Enable();
            UIState.ApplyCursor();
        }

        void OnDisable() { _move.Disable(); _sprint.Disable(); _jump.Disable(); }
        void OnDestroy() { _move.Dispose(); _sprint.Dispose(); _jump.Dispose(); }

        public void SetInputEnabled(bool enabled)
        {
            InputEnabled = enabled;
            if (!enabled) _planarVelocity = Vector3.zero;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _cc.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
            _cc.enabled = true;
            _planarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
            _pitch = 0f;
            if (head != null) head.localRotation = Quaternion.identity;
        }

        void Update()
        {
            bool canAct = InputEnabled && !UIState.BlocksGameplay;
            float dt = Time.deltaTime;
            if (canAct) Look(dt);

            Vector2 mv = canAct ? Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f) : Vector2.zero;
            IsSprinting = canAct && _sprint.IsPressed() && mv.y > 0.1f;
            Vector3 wish = (transform.right * mv.x + transform.forward * mv.y) * (IsSprinting ? sprintSpeed : walkSpeed);
            float accel = acceleration * (_cc.isGrounded ? 1f : airControl);
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, wish, accel * Mathf.Max(walkSpeed, 1f) * dt);

            if (_cc.isGrounded)
            {
                if (_verticalVelocity < 0f) _verticalVelocity = -2f; // stick to ground/slopes
                if (canAct && _jump.WasPressedThisFrame())
                {
                    _verticalVelocity = Mathf.Sqrt(2f * Mathf.Max(0.01f, gravity) * jumpHeight);
                    Jumped?.Invoke();
                }
            }
            _verticalVelocity -= gravity * dt;

            var flags = _cc.Move((_planarVelocity + Vector3.up * _verticalVelocity) * dt);
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;
        }

        void Look(float dt)
        {
            Vector2 delta;
            var stick = Gamepad.current != null ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;
            if (stick.sqrMagnitude > 0.01f) delta = stick * (stickSensitivity * dt);
            else delta = (Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero) * mouseSensitivity;
            transform.Rotate(0f, delta.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - delta.y, -pitchLimit, pitchLimit);
            if (head != null) head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }
}
