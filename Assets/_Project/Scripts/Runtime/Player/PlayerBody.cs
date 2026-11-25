using UnityEngine;
using UnityEngine.Rendering;

namespace DMI.Xenoarch.Player
{
    /// <summary>
    /// Full third-person body under the first-person camera (Kade). Visible when looking down and casts the player's
    /// shadow. The head/neck are split into their own shadows-only mesh at build time so the local camera never sees
    /// the inside of the head while the shadow stays complete. Locomotion only (idle/walk/run/sprint/jump/fall);
    /// no root motion, the <see cref="FirstPersonController"/> moves the player.
    /// Animator parameters: MoveX, MoveZ (local planar velocity, m/s), Speed, Grounded, Jump, VerticalSpeed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerBody : MonoBehaviour
    {
        public static readonly int MoveXId = Animator.StringToHash("MoveX");
        public static readonly int MoveZId = Animator.StringToHash("MoveZ");
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        public static readonly int GroundedId = Animator.StringToHash("Grounded");
        public static readonly int JumpId = Animator.StringToHash("Jump");
        public static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");

        [SerializeField] private FirstPersonController controller;
        [SerializeField] private Animator animator;
        [Tooltip("Body without head/neck: rendered and casts shadows.")]
        [SerializeField] private SkinnedMeshRenderer bodyRenderer;
        [Tooltip("Head/neck only: shadows-only in first person, fully rendered otherwise.")]
        [SerializeField] private SkinnedMeshRenderer headRenderer;
        [SerializeField] private float moveDamp = 0.1f;
        [Tooltip("Seconds off the ground before the body switches to falling (CharacterController.isGrounded flickers on steps/slopes).")]
        [SerializeField] private float ungroundedGrace = 0.15f;
        [SerializeField] private bool firstPersonView = true;

        [Header("Centering")]
        [Tooltip("Spine centreline bones (set by the builder). The camera is kept laterally centred between them.")]
        [SerializeField] private Transform hips;
        [SerializeField] private Transform neck;
        [Tooltip("Keep the body's spine laterally under the camera at runtime (slow integrator; walk sway averages out).")]
        [SerializeField] private bool autoCenter = true;
        [SerializeField] private float centerTime = 1.0f;
        [SerializeField] private float maxCenterCorrection = 0.2f;

        [Header("Sprint stability")]
        [Tooltip("Extra distance (m) the body slides back at full sprint so the forward run lean never reaches the camera.")]
        [SerializeField] private float sprintExtraBack = 0.12f;

        float _airTime, _centerCorr, _baseX, _baseZ, _back;

        public Animator Animator => animator;
        public Transform Hips => hips;
        public Transform Neck => neck;

        /// <summary>Lateral offset (m, controller-root space) of the body's spine centreline from the camera. ~0 when centred.</summary>
        public float LateralOffsetFromCamera()
        {
            if (controller == null || hips == null || neck == null) return 0f;
            var root = controller.Root;
            float spine = root.InverseTransformPoint((hips.position + neck.position) * 0.5f).x;
            var cam = controller.ViewCamera;
            float camX = cam != null ? root.InverseTransformPoint(cam.transform.position).x : 0f;
            return spine - camX;
        }
        public bool FirstPersonView => firstPersonView;

        /// <summary>First person hides the head from the camera (shadow kept); off = full body, e.g. photo/cutscene camera.</summary>
        public void SetFirstPersonView(bool fp)
        {
            firstPersonView = fp;
            if (headRenderer != null) headRenderer.shadowCastingMode = fp ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        void Awake()
        {
            if (controller == null) controller = GetComponentInParent<FirstPersonController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.applyRootMotion = false;
            if (animator != null && animator.isHuman)
            {
                if (hips == null) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (neck == null) neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            }
            _baseX = transform.localPosition.x;
            _baseZ = transform.localPosition.z;
            SetFirstPersonView(firstPersonView);
        }

        void OnEnable() { if (controller != null) controller.Jumped += OnJump; }
        void OnDisable() { if (controller != null) controller.Jumped -= OnJump; }
        void OnJump() { if (animator != null) animator.SetTrigger(JumpId); }

        void Update()
        {
            if (animator == null || controller == null) return;
            float dt = Time.deltaTime;
            Vector3 local = controller.Root.InverseTransformDirection(controller.PlanarVelocity);
            animator.SetFloat(MoveXId, local.x, moveDamp, dt);
            animator.SetFloat(MoveZId, local.z, moveDamp, dt);
            animator.SetFloat(SpeedId, controller.PlanarSpeed, moveDamp, dt);
            animator.SetFloat(VerticalSpeedId, controller.VerticalVelocity);
            _airTime = controller.IsGrounded ? 0f : _airTime + dt;
            animator.SetBool(GroundedId, _airTime < ungroundedGrace);
        }

        void LateUpdate()
        {
            // After animation: nudge the body sideways so its spine stays under the camera on average.
            if (controller == null) return;
            float dt = Time.deltaTime;
            // slide the body back with speed above walk (sprint lean), smoothly
            float over = Mathf.InverseLerp(controller.walkSpeed, controller.SprintSpeed, controller.PlanarSpeed);
            _back = Mathf.MoveTowards(_back, over * sprintExtraBack, dt * 0.6f);
            if (autoCenter && hips != null && neck != null)
            {
                float err = LateralOffsetFromCamera();
                _centerCorr = Mathf.Clamp(_centerCorr - err * Mathf.Clamp01(dt / Mathf.Max(0.05f, centerTime)), -maxCenterCorrection, maxCenterCorrection);
            }
            var lp = transform.localPosition; lp.x = _baseX + _centerCorr; lp.z = _baseZ - _back; transform.localPosition = lp;
        }
    }
}
