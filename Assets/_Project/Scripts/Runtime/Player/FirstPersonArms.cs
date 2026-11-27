using System.Collections.Generic;
using DMI.Xenoarch.Interaction;
using UnityEngine;

namespace DMI.Xenoarch.Player
{
    public enum Hand { Left = 0, Right = 1 }

    /// <summary>
    /// Drives the first-person arms (FPS_Starter_Pack rig, Xenoarch controller built by Xenoarch/Setup).
    /// Empty-handed, each arm swings down out of view (the full body's own arms are what you see at your sides);
    /// an arm comes up only while that hand holds a tool/device (<see cref="SetHeld"/>), or briefly on Interact.
    /// Animator parameters: Speed (0 idle .. 1 walk .. 2 sprint), Grounded, Jump, Interact, UsingDevice,
    /// LeftHeld, RightHeld, ToolHeld. Layers 1/2 ("LeftHand"/"RightHand") are arm-masked override layers whose weight
    /// follows the hand's raise weight; tools can swap their "Held" clip via an AnimatorOverrideController.
    /// Attach tool models to <see cref="LeftSlot"/>/<see cref="RightSlot"/> (children of hand_l/hand_r).
    /// </summary>
    public sealed class FirstPersonArms : MonoBehaviour
    {
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        public static readonly int GroundedId = Animator.StringToHash("Grounded");
        public static readonly int JumpId = Animator.StringToHash("Jump");
        public static readonly int InteractId = Animator.StringToHash("Interact");
        public static readonly int UsingDeviceId = Animator.StringToHash("UsingDevice");
        public static readonly int LeftHeldId = Animator.StringToHash("LeftHeld");
        public static readonly int RightHeldId = Animator.StringToHash("RightHeld");
        public static readonly int ToolHeldId = Animator.StringToHash("ToolHeld");
        public const string LeftLayer = "LeftHand", RightLayer = "RightHand";

        [SerializeField] private FirstPersonController controller;
        [SerializeField] private Interactor interactor;
        [SerializeField] private Animator animator;
        [SerializeField] private float speedDamp = 0.12f;

        [Header("Hands")]
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform rightUpperArm;
        [SerializeField] private Transform leftSlot;
        [SerializeField] private Transform rightSlot;
        [Tooltip("How far an empty arm swings down (degrees about the view's right axis).")]
        [SerializeField] private float loweredAngle = 80f;
        [Tooltip("Extra drop of the whole arms rig (local, metres) when both hands are empty.")]
        [SerializeField] private Vector3 loweredOffset = new Vector3(0f, -0.25f, -0.05f);
        [Tooltip("Raise/lower speed (weight per second).")]
        [SerializeField] private float raiseSpeed = 5f;
        [Tooltip("Seconds the right arm comes up for an Interact (pick-up/press) while empty-handed.")]
        [SerializeField] private float interactRaiseTime = 0.9f;

        readonly bool[] _held = new bool[2];
        readonly float[] _weight = new float[2];
        readonly float[] _tempUntil = new float[2];
        int _leftLayer = -1, _rightLayer = -1;
        Vector3 _restLocalPos;
        Transform _arms;
        readonly List<Renderer> _renderers = new List<Renderer>();
        bool _visible = true;
        /// <summary>t213u: hide first-person arms and held items (rover follow camera).</summary>
        public bool ForcedHidden { get; set; }
        Transform _rightHand;

        // Procedural reach (cockpit console buttons): right arm aims at a world point, out -> press -> back.
        [Header("Reach (button press)")]
        [SerializeField] private float reachOutSeconds = 0.35f;
        [SerializeField] private float reachHoldSeconds = 0.12f;
        [SerializeField] private float reachBackSeconds = 0.3f;
        [Tooltip("How far (m) the whole arms rig leans toward the target at full reach.")]
        [SerializeField] private float reachLean = 0.18f;
        Vector3 _reachTarget;
        float _reachStart = -1f, _reachWeight;
        bool _reachFired;
        System.Action _onPress, _onComplete;

        /// <summary>True while a reach gesture is playing.</summary>
        public bool Reaching => _reachStart >= 0f;

        /// <summary>Reach the right hand toward <paramref name="worldTarget"/>, press, return. <paramref name="onPress"/>
        /// fires at the press (or immediately if the arms rig is missing). <paramref name="onComplete"/> fires when the hand is back.</summary>
        public void Reach(Vector3 worldTarget, System.Action onPress, System.Action onComplete = null)
        {
            if (rightUpperArm == null || _rightHand == null) { onPress?.Invoke(); onComplete?.Invoke(); return; }
            if (Reaching)
            {
                var prev = _onPress; _onPress = null; var prevDone = _onComplete; _onComplete = null;
                prev?.Invoke(); prevDone?.Invoke();
            }
            _reachTarget = worldTarget; _reachStart = Time.time; _reachFired = false; _onPress = onPress; _onComplete = onComplete;
            RaiseTemporarily(Hand.Right, reachOutSeconds + reachHoldSeconds + reachBackSeconds + 0.1f);
        }

        public Transform LeftSlot => leftSlot;
        public Transform RightSlot => rightSlot;
        public Transform Slot(Hand h) => h == Hand.Left ? leftSlot : rightSlot;
        public bool IsHeld(Hand h) => _held[(int)h];
        /// <summary>0 = lowered out of view, 1 = raised.</summary>
        public float RaiseWeight(Hand h) => _weight[(int)h];
        public bool AnyHeld => _held[0] || _held[1];

        public bool UsingDevice
        {
            get => animator != null && animator.GetBool(UsingDeviceId);
            set { if (animator != null) animator.SetBool(UsingDeviceId, value); }
        }

        /// <summary>Mark a hand as holding (or not holding) a tool/device. Arms raise/lower smoothly.</summary>
        public void SetHeld(Hand hand, bool held)
        {
            _held[(int)hand] = held;
            if (animator == null) return;
            animator.SetBool(hand == Hand.Left ? LeftHeldId : RightHeldId, held);
            animator.SetBool(ToolHeldId, AnyHeld);
        }

        /// <summary>Raise a hand for a short gesture without equipping anything.</summary>
        public void RaiseTemporarily(Hand hand, float seconds) => _tempUntil[(int)hand] = Mathf.Max(_tempUntil[(int)hand], Time.time + seconds);

        void Awake()
        {
            if (controller == null) controller = GetComponentInParent<FirstPersonController>();
            if (interactor == null) interactor = GetComponentInParent<Interactor>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _restLocalPos = transform.localPosition;
            _arms = animator != null ? animator.transform : transform;
            if (leftUpperArm == null) leftUpperArm = FindDeep(_arms, "upperarm_l");
            if (rightUpperArm == null) rightUpperArm = FindDeep(_arms, "upperarm_r");
            _rightHand = FindDeep(_arms, "hand_r");
            foreach (var r in _arms.GetComponentsInChildren<Renderer>(true)) if (r.enabled) _renderers.Add(r);
            if (animator != null)
            {
                _leftLayer = animator.GetLayerIndex(LeftLayer);
                _rightLayer = animator.GetLayerIndex(RightLayer);
                animator.cullingMode = AnimatorCullingMode.CullCompletely; // hidden arms (renderers off) cost nothing
            }
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

        void OnJump() { if (animator != null && AnyHeld) animator.SetTrigger(JumpId); }

        void OnInteract(IInteractable _)
        {
            if (!_held[(int)Hand.Right]) RaiseTemporarily(Hand.Right, interactRaiseTime);
            if (animator != null) animator.SetTrigger(InteractId);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            UpdateReach();
            for (int i = 0; i < 2; i++)
            {
                bool up = _held[i] || Time.time < _tempUntil[i];
                _weight[i] = Mathf.MoveTowards(_weight[i], up ? 1f : 0f, raiseSpeed * dt);
            }
            if (animator != null)
            {
                if (_leftLayer > 0) animator.SetLayerWeight(_leftLayer, _held[0] ? _weight[0] : 0f);
                if (_rightLayer > 0) animator.SetLayerWeight(_rightLayer, _held[1] ? _weight[1] : 0f);
            }
            bool anyUp = !ForcedHidden && (_weight[0] > 0.001f || _weight[1] > 0.001f);
            _visible = anyUp;
            // Re-collect every frame so a held tool spawned after Awake is shown or hidden with the arms.
            if (_arms != null)
            {
                _renderers.Clear();
                foreach (var r in _arms.GetComponentsInChildren<Renderer>(true)) if (r != null) _renderers.Add(r);
            }
            foreach (var r in _renderers) if (r != null) r.enabled = anyUp;

            if (animator == null || controller == null || !anyUp) return;
            float s = controller.PlanarSpeed;
            float norm = s < 0.1f ? 0f : (controller.IsSprinting ? 2f : Mathf.Clamp01(s / Mathf.Max(0.1f, controller.walkSpeed)));
            animator.SetFloat(SpeedId, norm, speedDamp, dt);
            animator.SetBool(GroundedId, controller.IsGrounded);
        }

        void LateUpdate()
        {
            // Runs after the Animator: swing empty arms down about the view's right axis, pivoting at the shoulder.
            if (!_visible) return; // culled Animator does not reset the pose; don't accumulate
            var view = transform.parent != null ? transform.parent : transform;
            Vector3 axis = view.right;
            Lower(leftUpperArm, 1f - Smooth(_weight[0]), axis);
            Lower(rightUpperArm, 1f - Smooth(_weight[1]), axis);
            float both = 1f - Smooth(Mathf.Max(_weight[0], _weight[1]));
            transform.localPosition = _restLocalPos + loweredOffset * both;
            ApplyReach();
        }

        void UpdateReach()
        {
            if (!Reaching) { _reachWeight = 0f; return; }
            float t = Time.time - _reachStart;
            float press = reachOutSeconds + reachHoldSeconds * 0.5f;
            if (!_reachFired && t >= press) { _reachFired = true; var cb = _onPress; _onPress = null; cb?.Invoke(); }
            if (t < reachOutSeconds) _reachWeight = Smooth(t / reachOutSeconds);
            else if (t < reachOutSeconds + reachHoldSeconds) _reachWeight = 1f;
            else if (t < reachOutSeconds + reachHoldSeconds + reachBackSeconds) _reachWeight = 1f - Smooth((t - reachOutSeconds - reachHoldSeconds) / reachBackSeconds);
            else { _reachWeight = 0f; _reachStart = -1f; var done = _onComplete; _onComplete = null; done?.Invoke(); }
        }

        void ApplyReach()
        {
            if (_reachWeight <= 0.0001f || rightUpperArm == null || _rightHand == null) return;
            // Lean the rig toward the target (a press has some push), then swing the upper arm so shoulder->hand points at it.
            var view = transform.parent != null ? transform.parent : transform;
            Vector3 lean = view.InverseTransformDirection((_reachTarget - transform.position).normalized) * reachLean;
            transform.localPosition += lean * _reachWeight;
            Vector3 cur = _rightHand.position - rightUpperArm.position;
            Vector3 want = _reachTarget - rightUpperArm.position;
            if (cur.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return;
            var full = Quaternion.FromToRotation(cur, want);
            rightUpperArm.rotation = Quaternion.Slerp(Quaternion.identity, full, _reachWeight) * rightUpperArm.rotation;
        }

        void Lower(Transform bone, float amount, Vector3 axis)
        {
            if (bone == null || amount <= 0.0001f) return;
            bone.rotation = Quaternion.AngleAxis(loweredAngle * amount, axis) * bone.rotation;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }
    }
}
