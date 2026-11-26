using System.Collections;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// t187u-t190u: drivable rover built at runtime on the Grasshopper prefab. Nothing in the prefab is moved or saved:
    /// wheel colliders go at the P_Wheel_Big_* wheels, the rear door (Door_Back) swings 60 degrees on its own pivot
    /// (closed = its authored rotation), and the player enters through the back: E opens the door, E again ("Enter Rover")
    /// glides the player in to the driver's place behind the steering wheel. W/S drive and brake/reverse, A/D steer,
    /// Space handbrake, E (looking at nothing usable) gets out the same way, door opening and closing behind you.
    /// Parked: kinematic, the prefab's own mesh colliders (walkable). Driving: dynamic rigidbody with a box chassis and
    /// four wheel colliders (concave mesh colliders off), gravity from the surface environment.
    /// </summary>
    public sealed class RoverVehicle : MonoBehaviour
    {
        [Header("Drive")]
        public float mass = 2800f;
        public float motorTorque = 2600f;
        public float brakeTorque = 7000f;
        [Tooltip("Top speed forward (m/s); reverse is a third of it.")] public float maxSpeed = 14f;
        public float maxSteer = 28f;
        public float highSpeedSteer = 10f;
        public float suspension = 0.35f;
        [Header("Door / seat")]
        public string doorName = "Door_Back";
        public float doorOpenDegrees = 60f;
        public float doorSeconds = 0.9f;
        public float glideSeconds = 0.75f;
        public float seatedEyeHeight = 1.2f;
        [Tooltip("Driver position behind the steering wheel (m).")] public float seatBehindWheel = 0.75f;
        [Tooltip("Max speed (m/s) at which you can get out.")] public float exitSpeed = 1.5f;

        enum State { Parked, Entering, Driving, Exiting }
        State _state;
        public bool Occupied => _state != State.Parked;

        Rigidbody _rb;
        BoxCollider _chassis;
        readonly List<MeshCollider> _concave = new List<MeshCollider>();
        readonly List<(WheelCollider wc, Transform visual, bool front)> _wheels = new List<(WheelCollider, Transform, bool)>();
        Transform _door; Quaternion _doorClosed, _doorOpen; float _door01, _doorTarget; float _doorOpenedAt;
        Vector3 _fwdLocal = Vector3.forward, _seatLocal, _insideLocal, _outsideLocal, _doorLocal;
        float _floorLocalY, _lowestWheelLocalY;
        bool _inited;

        FirstPersonController _player; Interactor _interactor; IInteractionInput _input;
        readonly List<(Renderer r, bool on)> _hidden = new List<(Renderer, bool)>();
        float _seatedAt;

        public bool DoorOpen => _doorTarget > 0.5f;
        Vector3 Fwd => transform.TransformDirection(_fwdLocal);

        void Awake() { if (!_inited) Init(); }

        /// <summary>Measures the prefab (in its authored, unrotated pose) and adds the runtime parts. Idempotent.</summary>
        public void Init()
        {
            if (_inited) return;
            _inited = true;
            var rot = transform.rotation;
            transform.rotation = Quaternion.identity;
            try { Measure(); }
            finally { transform.rotation = rot; }

            _rb = GetComponent<Rigidbody>() ?? gameObject.AddComponent<Rigidbody>();
            _rb.mass = mass; _rb.useGravity = false; _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.linearDamping = 0.05f; _rb.angularDamping = 0.6f;
            _rb.isKinematic = true;
            SetDynamic(false);
        }

        void Measure()
        {
            Vector3 L(Vector3 world) => transform.InverseTransformPoint(world);
            var all = GetComponentsInChildren<Renderer>(true);
            var body = Bounds(all);
            Vector3 bodyMin = L(body.min), bodyMax = L(body.max);

            _door = FindDeep(transform, doorName);
            var steer = new List<Vector3>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("P_Steering_Wheel")) steer.Add(L(Bounds(t.GetComponentsInChildren<Renderer>(true)).center));
            Vector3 doorC = _door != null ? L(Bounds(_door.GetComponentsInChildren<Renderer>(true)).center) : new Vector3(0f, 0f, bodyMin.z);
            Vector3 steerC = Vector3.zero; foreach (var s in steer) steerC += s; steerC = steer.Count > 0 ? steerC / steer.Count : new Vector3(0f, 0f, bodyMax.z);
            var f = steerC - doorC; f.y = 0f;
            _fwdLocal = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, _fwdLocal);

            // Floor of the cabin.
            var floor = FindDeep(transform, "Floor");
            _floorLocalY = floor != null && floor.GetComponentInChildren<Renderer>() != null ? L(Bounds(floor.GetComponentsInChildren<Renderer>(true)).max).y
                : (steer.Count > 0 ? steerC.y - 0.85f : bodyMin.y + 1f);

            // Driver place: behind the left-hand steering wheel.
            Vector3 wheel = steerC; float best = float.MaxValue;
            foreach (var s in steer) { float d = Vector3.Dot(s, right); if (d < best) { best = d; wheel = s; } }
            _seatLocal = wheel - _fwdLocal * seatBehindWheel; _seatLocal.y = _floorLocalY;

            // Door: inside / outside points and the open rotation (the swing that moves the door out, away from the cabin).
            _doorLocal = doorC;
            _insideLocal = doorC + _fwdLocal * 1.6f; _insideLocal.y = _floorLocalY;
            if (_door != null)
            {
                _doorClosed = _door.localRotation;
                var plus = _doorClosed * Quaternion.Euler(doorOpenDegrees, 0f, 0f);
                var minus = _doorClosed * Quaternion.Euler(-doorOpenDegrees, 0f, 0f);
                _door.localRotation = plus; float aP = Vector3.Dot(L(Bounds(_door.GetComponentsInChildren<Renderer>(true)).center), _fwdLocal);
                _door.localRotation = minus; float aM = Vector3.Dot(L(Bounds(_door.GetComponentsInChildren<Renderer>(true)).center), _fwdLocal);
                _doorOpen = aP < aM ? plus : minus;
                _door.localRotation = _doorOpen;
                var openB = Bounds(_door.GetComponentsInChildren<Renderer>(true));
                float reach = Mathf.Max(0f, Vector3.Dot(doorC - L(openB.center), _fwdLocal)) * 2f;
                _door.localRotation = _doorClosed;
                _outsideLocal = doorC - _fwdLocal * (reach + 1.6f);

                // Interaction trigger fitted to the door (rotates with it).
                var dr = _door.GetComponentsInChildren<Renderer>(true);
                if (dr.Length > 0)
                {
                    var db = Bounds(dr);
                    var box = _door.gameObject.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.center = _door.InverseTransformPoint(db.center);
                    var sz = _door.InverseTransformVector(db.size);
                    box.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z)) + Vector3.one * 0.4f;
                }
                _door.gameObject.AddComponent<RoverDoor>().rover = this;
            }
            else _outsideLocal = doorC - _fwdLocal * 3f;
            _outsideLocal.y = bodyMin.y;

            // Wheels.
            _lowestWheelLocalY = float.MaxValue;
            var wheels = new List<(Transform t, Vector3 c, float r)>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("P_Wheel_Big"))
                {
                    var rs = t.GetComponentsInChildren<Renderer>(true);
                    if (rs.Length == 0) continue;
                    var b = Bounds(rs);
                    float r = Mathf.Max(0.2f, b.extents.y);
                    var c = L(b.center);
                    wheels.Add((rs[0].transform, c, r));
                    _lowestWheelLocalY = Mathf.Min(_lowestWheelLocalY, c.y - r);
                }
            if (wheels.Count == 0) _lowestWheelLocalY = bodyMin.y;
            float midF = 0f; foreach (var w in wheels) midF += Vector3.Dot(w.c, _fwdLocal); midF = wheels.Count > 0 ? midF / wheels.Count : 0f;
            float wheelY = 0f;
            var springRoot = new GameObject("[Rover Wheels]").transform;
            springRoot.SetParent(transform, false);
            springRoot.localRotation = Quaternion.LookRotation(_fwdLocal, Vector3.up);
            foreach (var w in wheels)
            {
                var go = new GameObject("WheelCollider " + w.t.name);
                go.transform.SetParent(springRoot, false);
                go.transform.position = transform.TransformPoint(w.c + Vector3.up * suspension * 0.5f);
                var wc = go.AddComponent<WheelCollider>();
                wc.radius = w.r; wc.mass = 60f; wc.suspensionDistance = suspension; wc.forceAppPointDistance = 0.2f;
                float sprung = mass / Mathf.Max(1, wheels.Count);
                wc.suspensionSpring = new JointSpring { spring = sprung * 90f, damper = 0.6f * Mathf.Sqrt(sprung * 90f * sprung), targetPosition = 0.5f };
                var side = wc.sidewaysFriction; side.stiffness = 1.3f; wc.sidewaysFriction = side;
                var fw = wc.forwardFriction; fw.stiffness = 1.2f; wc.forwardFriction = fw;
                _wheels.Add((wc, w.t, Vector3.Dot(w.c, _fwdLocal) > midF));
                wheelY += w.c.y;
            }
            wheelY = wheels.Count > 0 ? wheelY / wheels.Count : bodyMin.y;

            // Chassis box (driving only): the body above the wheel centres.
            _chassis = gameObject.AddComponent<BoxCollider>();
            float bottom = Mathf.Max(bodyMin.y, wheelY + 0.15f);
            var bc = (bodyMin + bodyMax) * 0.5f;
            _chassis.center = new Vector3(bc.x, (bottom + bodyMax.y) * 0.5f, bc.z);
            _chassis.size = new Vector3(bodyMax.x - bodyMin.x, Mathf.Max(0.5f, bodyMax.y - bottom), bodyMax.z - bodyMin.z);

            foreach (var mc in GetComponentsInChildren<MeshCollider>(true)) if (!mc.convex) _concave.Add(mc);
            _comLocal = new Vector3(bc.x, wheelY, bc.z);
        }
        Vector3 _comLocal;

        static Transform FindDeep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        static Bounds Bounds(Renderer[] rs)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in rs)
            {
                if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b : new Bounds(rs.Length > 0 ? rs[0].transform.position : Vector3.zero, Vector3.one * 0.5f);
        }

        /// <summary>Sets the rover down on flat ground in front of the player, rear door toward the player.</summary>
        public bool PlaceNear(Transform player, float distance)
        {
            var fwd = player.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
            for (int i = 0; i < 12; i++)
            {
                float ang = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 30f;
                var dir = Quaternion.Euler(0f, ang, 0f) * fwd;
                var p = player.position + dir * distance;
                if (!Ground(p, player, out var hit) || hit.normal.y < 0.88f) continue;
                // the vehicle faces away from the player, so the rear door is toward them
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(_fwdLocal, Vector3.up));
                var off = transform.rotation * new Vector3(_comLocal.x, 0f, _comLocal.z);   // body centre over the picked spot
                transform.position = hit.point - off - Vector3.up * (_lowestWheelLocalY - 0.03f);
                Physics.SyncTransforms();
                return true;
            }
            return false;
        }

        bool Ground(Vector3 p, Transform ignore, out RaycastHit best)
        {
            best = default; bool found = false;
            var hits = Physics.RaycastAll(p + Vector3.up * 60f, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform) || (ignore != null && h.collider.transform.IsChildOf(ignore))) continue;
                best = h; found = true; break;
            }
            return found;
        }

        void SetDynamic(bool dynamic)
        {
            if (_rb == null) return;
            if (dynamic)
            {
                foreach (var mc in _concave) if (mc != null) mc.enabled = false;
                if (_chassis != null) _chassis.enabled = true;
                _rb.isKinematic = false;
                _rb.centerOfMass = _comLocal;
                _rb.WakeUp();
            }
            else
            {
                if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
                _rb.isKinematic = true;
                if (_chassis != null) _chassis.enabled = false;
                foreach (var mc in _concave) if (mc != null) mc.enabled = true;
            }
            foreach (var w in _wheels) { w.wc.motorTorque = 0f; w.wc.brakeTorque = dynamic ? 0f : brakeTorque; w.wc.steerAngle = 0f; }
        }

        // ------------------------------------------------------------------ door

        public void SetDoor(bool open) { _doorTarget = open ? 1f : 0f; if (open) _doorOpenedAt = Time.time; }

        void Update()
        {
            if (_door != null && !Mathf.Approximately(_door01, _doorTarget))
            {
                _door01 = Mathf.MoveTowards(_door01, _doorTarget, Time.deltaTime / Mathf.Max(0.05f, doorSeconds));
                _door.localRotation = Quaternion.Slerp(_doorClosed, _doorOpen, Mathf.SmoothStep(0f, 1f, _door01));
            }
            // an open door with nobody coming in closes by itself
            if (_state == State.Parked && DoorOpen && Time.time - _doorOpenedAt > 12f) SetDoor(false);

            if (_state == State.Driving)
            {
                if (_player == null || !_player.Seated) { Release(); return; } // stood up by a teleport / load
                if (!_player.InputEnabled || UIState.BlocksGameplay || Time.unscaledTime - _seatedAt < 0.4f) return;
                bool e = _input != null && _input.InteractPressed && (_interactor == null || _interactor.Target == null);
                if (e)
                {
                    if (_rb.linearVelocity.magnitude > exitSpeed) Toast.Show("Stop the rover to get out.", 2f);
                    else StartCoroutine(ExitRoutine());
                }
            }
        }

        // ------------------------------------------------------------------ enter / exit

        public bool CanEnter(FirstPersonController p) => p != null && !p.Seated && _state == State.Parked;

        public bool Enter(FirstPersonController p)
        {
            if (!CanEnter(p)) return false;
            _player = p;
            _interactor = p.GetComponentInChildren<Interactor>();
            _input = p.GetComponentInChildren<IInteractionInput>();
            StartCoroutine(EnterRoutine());
            return true;
        }

        IEnumerator EnterRoutine()
        {
            _state = State.Entering;
            var p = _player;
            Vector3 start = p.transform.position;
            float yaw0 = p.transform.eulerAngles.y, pitch0 = p.Pitch, eye0 = p.StandingEyeHeight;
            p.BeginSeat(start);
            HideBody(p, true);
            if (!DoorOpen) { SetDoor(true); yield return new WaitForSeconds(doorSeconds * 0.8f); }
            float fwdYaw = Quaternion.LookRotation(Fwd, Vector3.up).eulerAngles.y;
            yield return Glide(p, start, W(_insideLocal), yaw0, fwdYaw, pitch0, 0f, eye0, eye0);
            SetDoor(false);
            yield return Glide(p, W(_insideLocal), W(_seatLocal), fwdYaw, fwdYaw, 0f, 8f, eye0, seatedEyeHeight);
            p.EnableSeatLook(fwdYaw, 110f, 55f, 50f);
            SetDynamic(true);
            _state = State.Driving;
            _seatedAt = Time.unscaledTime;
            InteractionPromptHud.IdlePrompt = "Exit Rover";
            Toast.Show("W/S drive, A/D steer, Space brake. E to get out.", 5f);
        }

        IEnumerator ExitRoutine()
        {
            _state = State.Exiting;
            InteractionPromptHud.IdlePrompt = null;
            SetDynamic(false);
            var p = _player;
            float fwdYaw = Quaternion.LookRotation(Fwd, Vector3.up).eulerAngles.y, backYaw = fwdYaw + 180f;
            float pitch = p.Pitch;
            yield return Glide(p, p.transform.position, W(_insideLocal), p.transform.eulerAngles.y, backYaw, pitch, 0f, seatedEyeHeight, p.StandingEyeHeight);
            SetDoor(true);
            yield return new WaitForSeconds(doorSeconds);
            var outside = W(_outsideLocal);
            if (Ground(outside, p.transform, out var hit)) outside = hit.point;
            yield return Glide(p, W(_insideLocal), outside, backYaw, backYaw, 0f, 0f, p.StandingEyeHeight, p.StandingEyeHeight);
            p.SetSeatPose(outside, backYaw, 0f, p.StandingEyeHeight);
            p.EndSeat();
            HideBody(p, false);
            _player = null;
            _state = State.Parked;
            yield return new WaitForSeconds(1.2f);
            if (_state == State.Parked) SetDoor(false);
        }

        IEnumerator Glide(FirstPersonController p, Vector3 from, Vector3 to, float yawA, float yawB, float pitchA, float pitchB, float eyeA, float eyeB)
        {
            for (float t = 0f; t < glideSeconds; t += Time.deltaTime)
            {
                if (p == null || !p.Seated) yield break;
                float k = Mathf.SmoothStep(0f, 1f, t / glideSeconds);
                p.SetSeatPose(Vector3.Lerp(from, to, k), Mathf.LerpAngle(yawA, yawB, k), Mathf.Lerp(pitchA, pitchB, k), Mathf.Lerp(eyeA, eyeB, k));
                yield return null;
            }
            if (p != null && p.Seated) p.SetSeatPose(to, yawB, pitchB, eyeB);
        }

        Vector3 W(Vector3 local) => transform.TransformPoint(local);

        void Release()
        {
            SetDynamic(false);
            HideBody(_player, false);
            InteractionPromptHud.IdlePrompt = null;
            _player = null;
            _state = State.Parked;
        }

        void OnDisable() { if (_state != State.Parked && _player != null) { if (_player.Seated) _player.EndSeat(); Release(); } }

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

        // ------------------------------------------------------------------ driving

        void FixedUpdate()
        {
            if (_rb == null || _rb.isKinematic) return;
            float g = _player != null ? _player.gravity : 9.81f;
            _rb.AddForce(Vector3.down * g, ForceMode.Acceleration);

            float throttle = 0f, steer = 0f; bool hand = false;
            if (_state == State.Driving && _player != null && _player.InputEnabled && !UIState.BlocksGameplay)
            {
                var kb = Keyboard.current; var gp = Gamepad.current;
                if (kb != null)
                {
                    throttle += (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                    steer += (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                    hand = kb.spaceKey.isPressed;
                }
                if (gp != null)
                {
                    throttle += gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue();
                    steer += gp.leftStick.ReadValue().x;
                    hand |= gp.buttonEast.isPressed;
                }
                throttle = Mathf.Clamp(throttle, -1f, 1f); steer = Mathf.Clamp(steer, -1f, 1f);
            }

            float v = Vector3.Dot(_rb.linearVelocity, Fwd);
            bool braking = Mathf.Abs(throttle) > 0.05f && Mathf.Abs(v) > 0.5f && Mathf.Sign(throttle) != Mathf.Sign(v);
            float limit = throttle >= 0f ? maxSpeed : maxSpeed / 3f;
            float motor = braking || Mathf.Abs(v) > limit ? 0f : throttle * motorTorque;
            float brake = hand ? brakeTorque * 1.3f : braking ? brakeTorque * Mathf.Abs(throttle) : Mathf.Abs(throttle) < 0.05f ? 250f : 0f;
            float steerDeg = steer * Mathf.Lerp(maxSteer, highSpeedSteer, Mathf.Clamp01(Mathf.Abs(v) / maxSpeed));
            foreach (var w in _wheels)
            {
                w.wc.motorTorque = motor;
                w.wc.brakeTorque = brake;
                if (w.front) w.wc.steerAngle = steerDeg;
            }
        }

        void LateUpdate()
        {
            // wheels roll with the ground speed (visual only)
            if (_rb != null && !_rb.isKinematic)
            {
                var right = transform.TransformDirection(Vector3.Cross(Vector3.up, _fwdLocal));
                foreach (var w in _wheels)
                {
                    if (w.visual == null) continue;
                    var r = w.visual.GetComponent<Renderer>();
                    var c = r != null ? r.bounds.center : w.visual.position;
                    w.visual.RotateAround(c, right, w.wc.rpm * 6f * Time.deltaTime);
                }
            }
            // the driver rides along (look stays free within the seat range)
            if (_state == State.Driving && _player != null && _player.Seated)
                _player.SetSeatFrame(W(_seatLocal), Quaternion.LookRotation(Fwd, Vector3.up).eulerAngles.y);
        }
    }
}
