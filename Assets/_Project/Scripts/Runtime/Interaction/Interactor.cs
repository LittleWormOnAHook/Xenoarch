using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using DMI.Xenoarch.Vehicles;
using UnityEngine;

namespace DMI.Xenoarch.Interaction
{
    /// <summary>
    /// Centre-screen interaction for a first-person view. Adapted from ASH ASHInteractor (camera ray + sphere aim assist,
    /// body-distance gate, blocker check, debounce, tap/hold). Changes: first-person reach (no third-person boom),
    /// reads the camera and root through <see cref="IPlayerRig"/>, input through <see cref="IInteractionInput"/>.
    /// Put it on the player root next to the IPlayerRig adapter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Interactor : MonoBehaviour
    {
        [Tooltip("Component implementing IPlayerRig. Empty = first one found on this object or its parents.")]
        [SerializeField] private MonoBehaviour rigComponent;
        [Tooltip("Component implementing IInteractionInput. Empty = found on this object, else one is added.")]
        [SerializeField] private MonoBehaviour inputComponent;
        [Range(1f, 6f)] [SerializeField] private float range = 2.5f;
        [Range(0f, 1f)] [SerializeField] private float aimAssistRadius = 0.25f;
        [SerializeField] private LayerMask interactMask = ~0;
        [Range(0f, 0.5f)] [SerializeField] private float useDebounce = 0.15f;
        [Range(0.02f, 0.3f)] [SerializeField] private float refreshInterval = 0.05f;

        public IInteractable Target { get; private set; }
        public string Prompt { get; private set; }
        public float HoldProgress01 { get; private set; }
        public event System.Action<IInteractable> Interacted;

        IPlayerRig _rig;
        IInteractionInput _input;
        float _nextRefresh, _nextUse, _holdTime;
        IInteractable _holdTarget;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        void Awake()
        {
            _rig = rigComponent as IPlayerRig ?? GetComponentInParent<IPlayerRig>();
            _input = inputComponent as IInteractionInput;
            if (_input == null && !TryGetComponent(out _input)) _input = gameObject.AddComponent<InputSystemInteractionInput>();
            if (_rig == null) Debug.LogError("[Xenoarch] Interactor needs an IPlayerRig adapter on the player", this);
        }

        void Update()
        {
            if (_rig == null || _rig.ViewCamera == null) return;
            if (!_rig.InputEnabled || UIState.BlocksGameplay) { ClearTarget(); return; }

            var ctx = BuildContext();
            if (Time.unscaledTime >= _nextRefresh || Target == null)
            {
                _nextRefresh = Time.unscaledTime + refreshInterval;
                Target = FindTarget(ctx);
                Prompt = Target != null ? Target.GetPrompt(ctx) : null;
            }
            if (Target == null) { ResetHold(); return; }

            float hold = Target.GetHoldDuration(ctx);
            if (hold <= 0f)
            {
                ResetHold();
                if (_input.InteractPressed && Time.unscaledTime >= _nextUse) Use(ctx);
                return;
            }
            if (_input.InteractHeld)
            {
                if (_holdTarget != Target) { _holdTarget = Target; _holdTime = 0f; }
                _holdTime += Time.deltaTime;
                HoldProgress01 = Mathf.Clamp01(_holdTime / hold);
                if (_holdTime >= hold) { Use(ctx); ResetHold(); }
            }
            else ResetHold();
        }

        /// <summary>Test/tooling hook: interact with the current target as if the button was pressed.</summary>
        public bool ForceInteract()
        {
            if (_rig == null) return false;
            var ctx = BuildContext();
            Target = FindTarget(ctx);
            return Target != null && Use(ctx);
        }

        bool Use(in InteractionContext ctx)
        {
            _nextUse = Time.unscaledTime + useDebounce;
            var used = Target;
            bool ok = used.TryInteract(ctx);
            if (ok) Interacted?.Invoke(used);
            _nextRefresh = 0f;
            return ok;
        }

        void ResetHold() { _holdTarget = null; _holdTime = 0f; HoldProgress01 = 0f; }
        void ClearTarget() { Target = null; Prompt = null; ResetHold(); }

        InteractionContext BuildContext()
        {
            var cam = _rig.ViewCamera;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return new InteractionContext(_rig, XenoarchRuntime.Session, ray, range);
        }

        IInteractable FindTarget(in InteractionContext ctx)
        {
            float maxDist = range + 0.5f;
            var best = Probe(ctx, Physics.RaycastNonAlloc(ctx.ViewRay, _hits, maxDist, interactMask, QueryTriggerInteraction.Collide));
            if (best == null && aimAssistRadius > 0f)
                best = Probe(ctx, Physics.SphereCastNonAlloc(ctx.ViewRay, aimAssistRadius, _hits, maxDist, interactMask, QueryTriggerInteraction.Collide));
            return best;
        }

        IInteractable Probe(in InteractionContext ctx, int count)
        {
            System.Array.Sort(_hits, 0, count, HitDistanceComparer.Instance);
            var root = _rig.Root;
            for (int i = 0; i < count; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(root)) continue;
                var seated = RoverVehicle.SeatedRover;
                if (seated != null && h.collider.transform.IsChildOf(seated.transform)) continue;
                var usable = h.collider.GetComponentInParent<IInteractable>();
                if (usable == null)
                {
                    if (!h.collider.isTrigger) return null; // solid blocker in front
                    continue;
                }
                if (h.distance > range) continue;
                if (usable.GetPriority(ctx) < 0f) continue;
                return usable; // nearest usable wins (first-person: what you look at)
            }
            return null;
        }

        sealed class HitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
    }
}
