using System.Collections;
using System.Collections.Generic;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Press E to sit in a cockpit chair. The player glides into the seat (root to the chair's centre, camera lowered to
    /// seated eye height, turned to face the console), locomotion stops and look is clamped. Press E (looking at nothing
    /// usable) or Space to stand back up where you sat down from. Nothing on the chair is moved: the anchor is computed
    /// from the chair's renderer bounds at runtime, or taken from <see cref="seatAnchor"/> if set.
    /// The Kade rig has no sit animation, so the body is hidden while seated (first-person view only).
    /// </summary>
    public sealed class CockpitSeat : MonoBehaviour, IInteractable
    {
        [Tooltip("Optional: exact seated root position/facing. Empty = chair bounds centre, facing the console.")]
        public Transform seatAnchor;
        [Tooltip("Console the seat faces (set by Setup/9). Empty = the chair's forward.")]
        public CockpitHelm console;
        [Tooltip("Camera height above the floor while seated (m).")]
        public float eyeHeight = 1.3f;
        [Tooltip("Shift of the seated position from the chair centre, along the facing (negative = back into the seat).")]
        public float depthOffset = -0.05f;
        public float transitionSeconds = 0.65f;
        [Range(20f, 170f)] public float yawRange = 80f;
        [Range(10f, 80f)] public float pitchDown = 55f;
        [Range(10f, 80f)] public float pitchUp = 60f;

        public static CockpitSeat Occupied { get; private set; }
        public bool IsOccupied => Occupied == this;
        public bool Transitioning { get; private set; }

        FirstPersonController _player;
        Interactor _interactor;
        IInteractionInput _input;
        readonly List<(Renderer r, bool on)> _hidden = new List<(Renderer, bool)>();
        float _seatedAt;
        ConsolePower _power;

        /// <summary>Console power controller for this seat (added at runtime to the faced console; nothing saved).</summary>
        public ConsolePower Power => _power;

        void Start()
        {
            var c = console;
            if (c == null)
            { // no console assigned: the nearest helm console
                float best = 6f;
                foreach (var h in FindObjectsByType<CockpitHelm>())
                { float d = Vector3.Distance(h.transform.position, transform.position); if (d < best) { best = d; c = h; } }
            }
            if (c == null) return;
            _power = c.GetComponent<ConsolePower>();
            if (_power == null) _power = c.gameObject.AddComponent<ConsolePower>(); // starts dark
        }

        /// <summary>Seated: reach a hand to the console and press (click), then the displays blink on.</summary>
        void PowerUpConsole()
        {
            if (_power == null || _player == null) return;
            var arms = _player.GetComponentInChildren<FirstPersonArms>();
            var helm = _power.GetComponent<CockpitHelm>();
            Vector3 target = helm != null && _player.ViewCamera != null ? helm.PressPointFor(_player.ViewCamera.transform.position) : _power.transform.position;
            if (arms != null) arms.Reach(target, () => { if (IsOccupied) _power.PowerOn(); });
            else _power.PowerOn();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => Occupied = null;

        public Transform InteractionTransform => transform;

        public float GetPriority(in InteractionContext c)
        {
            if (Occupied != null || Transitioning || !(c.Rig is FirstPersonController)) return -1f;
            return 1f;
        }

        public string GetPrompt(in InteractionContext c) => "Sit";
        public float GetHoldDuration(in InteractionContext c) => 0f;

        public bool TryInteract(in InteractionContext c) => c.Rig is FirstPersonController f && Sit(f);

        /// <summary>Sit down (smooth transition). Optional callback when seated.</summary>
        public bool Sit(FirstPersonController player, System.Action onSeated = null)
        {
            if (player == null || Occupied != null || Transitioning || player.Seated) return false;
            Occupied = this;
            _player = player;
            _interactor = player.GetComponentInChildren<Interactor>();
            _input = player.GetComponentInChildren<IInteractionInput>();
            StartCoroutine(SitRoutine(onSeated));
            return true;
        }

        public void Stand()
        {
            if (!IsOccupied || Transitioning || _player == null) return;
            StartCoroutine(StandRoutine());
        }

        /// <summary>Seated root position and yaw (world). Computed, never written to the chair.</summary>
        public void GetSeatPose(float floorY, out Vector3 pos, out float yaw)
        {
            if (seatAnchor != null)
            {
                pos = seatAnchor.position; pos.y = floorY;
                yaw = seatAnchor.eulerAngles.y;
                return;
            }
            var b = ChairBounds();
            Vector3 face = transform.forward;
            if (console != null)
            {
                var cb = Bounds(console.transform);
                face = cb.center - b.center;
            }
            face.y = 0f;
            if (face.sqrMagnitude < 1e-4f) face = Vector3.forward;
            face.Normalize();
            pos = new Vector3(b.center.x, floorY, b.center.z) + face * depthOffset;
            yaw = Quaternion.LookRotation(face, Vector3.up).eulerAngles.y;
        }

        public Bounds ChairBounds() => Bounds(transform);

        static Bounds Bounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.one * 0.5f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        IEnumerator SitRoutine(System.Action onSeated)
        {
            Transitioning = true;
            var p = _player;
            Vector3 startPos = p.Root.position;
            float startYaw = p.Root.eulerAngles.y, startPitch = p.Pitch, startEye = p.StandingEyeHeight;
            p.BeginSeat(startPos);
            GetSeatPose(startPos.y, out var seatPos, out var seatYaw);
            HideBody(p, true);
            for (float t = 0f; t < transitionSeconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / transitionSeconds);
                p.SetSeatPose(Vector3.Lerp(startPos, seatPos, k), Mathf.LerpAngle(startYaw, seatYaw, k), Mathf.Lerp(startPitch, 8f, k), Mathf.Lerp(startEye, eyeHeight, k));
                yield return null;
            }
            p.SetSeatPose(seatPos, seatYaw, 8f, eyeHeight);
            p.EnableSeatLook(seatYaw, yawRange, pitchDown, pitchUp);
            Transitioning = false;
            _seatedAt = Time.unscaledTime;
            InteractionPromptHud.IdlePrompt = "Stand up";
            PowerUpConsole();
            onSeated?.Invoke();
        }

        IEnumerator StandRoutine()
        {
            Transitioning = true;
            var p = _player;
            InteractionPromptHud.IdlePrompt = null;
            if (_power != null) _power.PowerOff();
            Vector3 from = p.Root.position, to = SafeExit(p);
            float yaw = p.Root.eulerAngles.y, pitch = p.Pitch;
            for (float t = 0f; t < transitionSeconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / transitionSeconds);
                p.SetSeatPose(Vector3.Lerp(from, to, k), yaw, Mathf.Lerp(pitch, 0f, k), Mathf.Lerp(eyeHeight, p.StandingEyeHeight, k));
                yield return null;
            }
            p.SetSeatPose(to, yaw, 0f, p.StandingEyeHeight);
            p.EndSeat();
            Release();
        }

        /// <summary>Where you sat down from, if the capsule fits there; else beside the chair.</summary>
        Vector3 SafeExit(FirstPersonController p)
        {
            var cc = p.GetComponent<CharacterController>();
            float r = cc != null ? cc.radius : 0.3f, h = cc != null ? cc.height : 1.8f;
            bool Fits(Vector3 pos)
            {
                Vector3 a = pos + Vector3.up * (r + 0.05f), b = pos + Vector3.up * (h - r);
                foreach (var col in Physics.OverlapCapsule(a, b, r * 0.95f, ~0, QueryTriggerInteraction.Ignore))
                    if (!col.transform.IsChildOf(p.Root)) return false;
                return true;
            }
            var exit = p.SeatExitPosition;
            if (Fits(exit)) return exit;
            var bnd = ChairBounds();
            var c = new Vector3(bnd.center.x, exit.y, bnd.center.z);
            float d = Mathf.Max(bnd.extents.x, bnd.extents.z) + r + 0.15f;
            foreach (var dir in new[] { -p.Root.forward, p.Root.right, -p.Root.right, p.Root.forward })
                if (Fits(c + dir * d)) return c + dir * d;
            return exit;
        }

        void Update()
        {
            if (!IsOccupied || _player == null) return;
            if (!Transitioning && !_player.Seated) { Release(); return; } // stood up by a teleport/load
            if (Transitioning || !_player.InputEnabled || UIState.BlocksGameplay || Time.unscaledTime - _seatedAt < 0.35f) return;
            bool space = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
            bool e = _input != null && _input.InteractPressed && (_interactor == null || _interactor.Target == null);
            if (space || e) Stand();
        }

        void OnDisable() { if (IsOccupied && _player != null && _player.Seated) { _player.EndSeat(); } if (IsOccupied) Release(); }

        void Release()
        {
            if (_power != null) _power.PowerOff();
            HideBody(_player, false);
            if (Occupied == this) Occupied = null;
            InteractionPromptHud.IdlePrompt = null;
            Transitioning = false;
            _player = null;
        }

        void HideBody(FirstPersonController p, bool hide)
        {
            if (hide)
            {
                _hidden.Clear();
                var body = p != null ? p.GetComponentInChildren<PlayerBody>(true) : null;
                if (body == null) return;
                foreach (var r in body.GetComponentsInChildren<Renderer>(true)) { _hidden.Add((r, r.enabled)); r.enabled = false; }
            }
            else
            {
                foreach (var (r, on) in _hidden) if (r != null) r.enabled = on;
                _hidden.Clear();
            }
        }
    }
}
