using DMI.Xenoarch.Interaction;
using UnityEngine;

namespace DMI.Xenoarch.Player
{
    /// <summary>
    /// Drives the first-person arms Animator (Xenoarch controller built from FPS_Starter_Pack clips by
    /// Xenoarch/Setup). Parameters: Speed (0 idle .. 1 walk .. 2 sprint), Grounded, Jump, Interact, UsingDevice.
    /// Tools/devices later set UsingDevice (PC/tablet pose) or add their own layers.
    /// </summary>
    public sealed class FirstPersonArms : MonoBehaviour
    {
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        public static readonly int GroundedId = Animator.StringToHash("Grounded");
        public static readonly int JumpId = Animator.StringToHash("Jump");
        public static readonly int InteractId = Animator.StringToHash("Interact");
        public static readonly int UsingDeviceId = Animator.StringToHash("UsingDevice");

        [SerializeField] private FirstPersonController controller;
        [SerializeField] private Interactor interactor;
        [SerializeField] private Animator animator;
        [SerializeField] private float speedDamp = 0.12f;

        public bool UsingDevice
        {
            get => animator != null && animator.GetBool(UsingDeviceId);
            set { if (animator != null) animator.SetBool(UsingDeviceId, value); }
        }

        void Awake()
        {
            if (controller == null) controller = GetComponentInParent<FirstPersonController>();
            if (interactor == null) interactor = GetComponentInParent<Interactor>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        void OnEnable()
        {
            if (controller != null) controller.Jumped += OnJump;
            if (interactor != null) interactor.Interacted += OnInteract;
        }

        void OnDisable()
        {
            if (controller != null) controller.Jumped -= OnJump;
            if (interactor != null) interactor.Interacted -= OnInteract;
        }

        void OnJump() { if (animator != null) animator.SetTrigger(JumpId); }
        void OnInteract(IInteractable _) { if (animator != null) animator.SetTrigger(InteractId); }

        void Update()
        {
            if (animator == null || controller == null) return;
            float s = controller.PlanarSpeed;
            float norm = s < 0.1f ? 0f : (controller.IsSprinting ? 2f : Mathf.Clamp01(s / Mathf.Max(0.1f, controller.walkSpeed)));
            animator.SetFloat(SpeedId, norm, speedDamp, Time.deltaTime);
            animator.SetBool(GroundedId, controller.IsGrounded);
        }
    }
}
