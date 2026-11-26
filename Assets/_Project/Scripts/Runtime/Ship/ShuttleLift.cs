using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Shuttle bay lift on the "Shuttle Platform": moves straight down to the shuttle bay and back up. Starts in the up
    /// position (where it is saved). E while standing on it (look at the floor), or at the Up / Down consoles. The
    /// player riding it is carried along. Runtime motion only; the saved position is the "up" stop.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ShuttleLift : MonoBehaviour, IInteractable
    {
        [Tooltip("Travel (m) from the up stop down to the bay. 0 = auto (height between the Down Console and Up Console).")] public float travel;
        public float speed = 1.6f;
        [Tooltip("Up/Down console objects (auto by name if empty).")] public Transform upConsole, downConsole;

        public enum State { Up, MovingDown, Down, MovingUp }
        public State Current { get; private set; } = State.Up;
        public float Travel { get; private set; }
        public bool IsDown => Current == State.Down;
        public bool Moving => Current == State.MovingDown || Current == State.MovingUp;
        public Transform InteractionTransform => transform;

        Vector3 _upPos; Collider _col;

        void Awake()
        {
            _upPos = transform.position;
            _col = GetComponentInChildren<Collider>();
            if (upConsole == null) upConsole = FindNamed("Up Console");
            if (downConsole == null) downConsole = FindNamed("Down Console");
            Travel = travel > 0.1f ? travel : upConsole != null && downConsole != null ? Mathf.Abs(downConsole.position.y - upConsole.position.y) : 6f;
            if (Travel < 0.5f) Travel = 6f;
            var mr = GetComponent<MeshRenderer>();
            if (mr != null && mr.isPartOfStaticBatch) Debug.LogWarning("[Xenoarch] Shuttle Platform is static-batched, so its mesh can't move. Clear 'Static' on it (Xenoarch ship hookup does this).");
        }

        Transform FindNamed(string n)
        {
            foreach (var r in gameObject.scene.GetRootGameObjects())
            {
                var t = Deep(r.transform, n);
                if (t != null) return t;
            }
            return null;
        }

        static Transform Deep(Transform t, string n)
        {
            if (t.name == n && t.gameObject.activeInHierarchy) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Deep(t.GetChild(i), n); if (r != null) return r; }
            return null;
        }

        public void Toggle() { if (Current == State.Up || Current == State.MovingUp) GoDown(); else GoUp(); }
        public void GoDown() { if (Current != State.Down) Current = State.MovingDown; }
        public void GoUp() { if (Current != State.Up) Current = State.MovingUp; }

        void Update()
        {
            if (!Moving) return;
            var target = Current == State.MovingDown ? _upPos + Vector3.down * Travel : _upPos;
            var from = transform.position;
            var to = Vector3.MoveTowards(from, target, speed * Time.deltaTime);
            var delta = to - from;
            var rider = RiderController(from);
            transform.position = to;
            if (rider != null && delta != Vector3.zero)
            {
                bool was = rider.enabled; rider.enabled = false;
                rider.transform.position += delta;
                rider.enabled = was;
            }
            if ((to - target).sqrMagnitude < 1e-6f) Current = Current == State.MovingDown ? State.Down : State.Up;
        }

        /// <summary>The player's CharacterController if they stand on the platform (inside its footprint, near its top).</summary>
        CharacterController RiderController(Vector3 platformPos)
        {
            var rig = PlayerRigLocator.Find();
            if (rig == null || _col == null) return null;
            var b = _col.bounds; var p = rig.Root.position;
            if (p.x < b.min.x - 0.2f || p.x > b.max.x + 0.2f || p.z < b.min.z - 0.2f || p.z > b.max.z + 0.2f) return null;
            if (p.y < b.min.y - 0.3f || p.y > b.max.y + 1.0f) return null;
            return rig.Root.GetComponent<CharacterController>();
        }

        public bool PlayerOnPlatform => RiderController(transform.position) != null;

        public string ActionText => Current switch
        {
            State.Up => "Lower platform to the shuttle bay",
            State.Down => "Raise platform",
            _ => "Platform moving",
        };

        public float GetPriority(in InteractionContext c) => Moving ? -1f : 0.5f;
        public string GetPrompt(in InteractionContext c) => ActionText;
        public float GetHoldDuration(in InteractionContext c) => 0f;
        public bool TryInteract(in InteractionContext c)
        {
            if (Moving) return false;
            if (Current == State.Up) GoDown(); else GoUp();
            return true;
        }
    }
}
