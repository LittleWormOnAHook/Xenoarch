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
        [Tooltip("Down stop marker (auto: a 'Down Position' object under or near the platform). Its world position is cached at start, so it can be a child that moves with the platform.")] public Transform downPosition;
        [Tooltip("Fallback only (no Down Position): travel (m) from the up stop down to the bay. 0 = auto (height between the Down Console and Up Console).")] public float travel;
        public float speed = 1.6f;
        [Tooltip("Up/Down console objects (auto by name if empty).")] public Transform upConsole, downConsole;

        [Header("Audio (empty = Resources/XenoarchShipSfx, then procedural)")]
        public AudioClip startClip;
        public AudioClip loopClip;
        public AudioClip stopClip;
        [Range(0f, 1f)] public float clunkVolume = 0.8f;
        [Range(0f, 1f)] public float humVolume = 0.45f;
        [Range(0.5f, 2f)] public float humPitch = 1f;
        [Tooltip("3D falloff: full volume within this distance (m).")] public float minDistance = 3f;
        public float maxDistance = 30f;

        public enum State { Up, MovingDown, Down, MovingUp }
        public State Current { get; private set; } = State.Up;
        public float Travel { get; private set; }
        public bool IsDown => Current == State.Down;
        public bool Moving => Current == State.MovingDown || Current == State.MovingUp;
        public Transform InteractionTransform => transform;

        Vector3 _upPos, _downPos; Collider _col;
        AudioSource _loop, _oneShot;
        public bool UsesDownMarker { get; private set; }

        void Awake()
        {
            _upPos = transform.position;
            _col = GetComponentInChildren<Collider>();
            if (upConsole == null) upConsole = FindNamed("Up Console");
            if (downConsole == null) downConsole = FindNamed("Down Console");
            if (downPosition == null) downPosition = FindDownMarker();
            if (downPosition != null && _upPos.y - downPosition.position.y > 0.3f)
            {
                _downPos = downPosition.position; // cached now: as a child it rides down with the platform
                UsesDownMarker = true;
            }
            else
            {
                if (downPosition != null) Debug.LogWarning("[Xenoarch] Shuttle lift: 'Down Position' is not below the platform; using the console-height estimate.");
                float t = travel > 0.1f ? travel : upConsole != null && downConsole != null ? Mathf.Abs(downConsole.position.y - upConsole.position.y) : 6f;
                if (t < 0.5f) t = 6f;
                _downPos = _upPos + Vector3.down * t;
            }
            Travel = Vector3.Distance(_upPos, _downPos);
            SetupAudio();
            var mr = GetComponent<MeshRenderer>();
            if (mr != null && mr.isPartOfStaticBatch) Debug.LogWarning("[Xenoarch] Shuttle Platform is static-batched, so its mesh can't move. Clear 'Static' on it (Xenoarch ship hookup does this).");
        }

        void SetupAudio()
        {
            var set = ShipSfx.Get();
            if (startClip == null) startClip = set != null && set.liftStart != null ? set.liftStart : ShipSfx.ProceduralClunk("Lift Start", 1.1f);
            if (loopClip == null) loopClip = set != null && set.liftLoop != null ? set.liftLoop : ShipSfx.ProceduralHum("Lift Hum");
            if (stopClip == null) stopClip = set != null && set.liftStop != null ? set.liftStop : ShipSfx.ProceduralClunk("Lift Stop", 0.85f);
            _loop = NewSource(); _loop.loop = true; _loop.clip = loopClip;
            _oneShot = NewSource();
        }

        AudioSource NewSource()
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = minDistance; a.maxDistance = maxDistance; a.dopplerLevel = 0f;
            return a;
        }

        void OnMoveStarted()
        {
            if (_oneShot != null && startClip != null) _oneShot.PlayOneShot(startClip, clunkVolume);
            if (_loop != null && loopClip != null && !_loop.isPlaying) { _loop.volume = humVolume; _loop.pitch = humPitch; _loop.Play(); }
        }

        void OnMoveStopped()
        {
            if (_loop != null) _loop.Stop();
            if (_oneShot != null && stopClip != null) _oneShot.PlayOneShot(stopClip, clunkVolume);
        }

        static bool IsDownName(string n) => n.Replace(" ", "").Replace("_", "").ToLowerInvariant() == "downposition";

        /// <summary>'Down Position' under the platform first, else the nearest one in the scene within 20 m.</summary>
        Transform FindDownMarker()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t != transform && IsDownName(t.name)) return t;
            Transform best = null; float bd = 20f * 20f;
            foreach (var r in gameObject.scene.GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true))
                    if (IsDownName(t.name)) { float d = (t.position - transform.position).sqrMagnitude; if (d < bd) { bd = d; best = t; } }
            return best;
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
        public void GoDown() { if (Current == State.Down || Current == State.MovingDown) return; bool was = Moving; Current = State.MovingDown; if (!was) OnMoveStarted(); }
        public void GoUp() { if (Current == State.Up || Current == State.MovingUp) return; bool was = Moving; Current = State.MovingUp; if (!was) OnMoveStarted(); }

        void Update()
        {
            if (!Moving) return;
            var target = Current == State.MovingDown ? _downPos : _upPos;
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
            if ((to - target).sqrMagnitude < 1e-6f) { Current = Current == State.MovingDown ? State.Down : State.Up; OnMoveStopped(); }
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
