using System;
using System.Collections;
using System.Collections.Generic;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Ship;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// The Grasshopper rover (t187u-t195u). Set up once in the editor (Xenoarch setup bakes it onto the scene instance and
    /// applies it to the prefab): a "[Rover Rig]" child, unscaled in world terms, holds the wheel colliders, the driving
    /// chassis box, the rear entry zone and editable markers (Seat, DoorInside, DoorOutside, COM). Nothing of the model is
    /// moved. Only a rover deployed from the inventory is live; copies placed in scenes stay inert.
    /// Entry: look at the rover, E opens Door_Back (60 degrees on its own pivot), E "Enter Rover" walks you round to the
    /// back and in to the driver-side P_Chair_05. Seated, the hand reaches out to P_Cockpit_Tech_01, which powers on like
    /// the ship's console; driving is enabled after power-on. W/S drive and brake/reverse, A/D steer, Space handbrake,
    /// Shift boosts (short meter, extra fuel), V switches 3rd person (behind and above) / 1st person (the seat),
    /// remembered; R opens the refuel menu; E (looking at nothing usable) gets out. Fuel is held by RoverDirector.
    /// Parked (t214u): stays simulated with the handbrake on, chassis + wheels, then sleeps after settling.
    /// Driving: chassis box + wheel colliders. Suspension arms rotate on P_Rover_Setup_* pivots (t217u).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class RoverVehicle : MonoBehaviour
    {
        [Serializable] public sealed class WheelSlot { public WheelCollider collider; public Transform visual; public bool front; }

        [Header("Drive")]
        public float mass = 2800f;
        [Tooltip("Drive acceleration on flat ground (m/s^2); wheel torque comes from it, the mass and the wheel radius.")] public float acceleration = 16f;
        [Tooltip("Braking deceleration (m/s^2).")] public float brakeDecel = 12f;
        [Tooltip("Top speed forward (m/s); 50 km/h. Reverse is a third of it.")] public float maxSpeed = 13.8889f;
        [Tooltip("Boost top speed (m/s); 100 km/h, Shift, short-term.")] public float boostSpeed = 27.7778f;
        [Tooltip("Seconds of continuous boost from a full meter.")] public float boostSeconds = 3.5f;
        [Tooltip("Seconds to recharge an empty boost meter while not boosting.")] public float boostRechargeSeconds = 7f;
        [Tooltip("Fuel units spent per second at full throttle (before boost multiplier).")] public float fuelPerSecond = 0.12f;
        [Tooltip("Extra fuel units spent per metre travelled.")] public float fuelPerMetre = 0.008f;
        [Tooltip("Fuel use multiplier while boosting.")] public float boostFuelMul = 2.5f;
        public float maxSteer = 30f;
        public float highSpeedSteer = 8f;
        public float suspension = 0.2f;
        [Header("Door / seat")]
        public float doorOpenDegrees = 60f;
        public float doorSeconds = 0.9f;
        public float glideSeconds = 0.75f;
        public float seatedEyeHeight = 1.15f;
        [Tooltip("Max speed (m/s) at which you can get out.")] public float exitSpeed = 1.5f;
        [Header("Cameras (V toggles)")]
        public Key cameraKey = Key.V;
        public float chaseDistance = 14f;
        public float chaseHeight = 4.8f;
        public float chaseSmooth = 0.18f;

        [Header("Baked setup (Xenoarch > rover setup; markers can be moved)")]
        public bool baked;
        public Transform door, driverChair, console, rig, seat, doorInside, doorOutside, com;
        public Quaternion doorClosed = Quaternion.identity, doorOpen = Quaternion.identity;
        public BoxCollider chassis;
        public List<WheelSlot> wheels = new List<WheelSlot>();

        /// <summary>Set by <see cref="RoverDirector"/> around Instantiate: only deployed rovers are live.</summary>
        internal static bool SpawningDeployed;
        public bool Live { get; private set; }

        enum State { Parked, Entering, Driving, Exiting }
        State _state;
        public bool Occupied => _state != State.Parked;
        bool _powered;

        Rigidbody _rb;
        float _motorTorque, _brakeTorque;
        readonly List<MeshCollider> _concave = new List<MeshCollider>();
        float _door01, _doorTarget, _doorOpenedAt;
        ConsolePower _power;

        FirstPersonController _player; Interactor _interactor; IInteractionInput _input;
        readonly List<(Renderer r, bool on)> _hidden = new List<(Renderer, bool)>();
        float _seatedAt;

        const string CamPref = "xenoarch.rover.camera3rd";
        const float CruiseKph = 40f, BoostKph = 80f;
        float _boost01 = 1f;
        bool _warnedLow, _warnedEmpty;
        Vector3 _headXZ; Transform _head; bool _headCentered;
        public float Boost01 => _boost01;
        public float SpeedKph
        {
            get
            {
                if (_rb == null || _rb.isKinematic) return 0f;
                var vel = _rb.linearVelocity; vel.y = 0f;
                return vel.magnitude * 3.6f;
            }
        }
        float LookYaw => Quaternion.LookRotation(Fwd, Vector3.up).eulerAngles.y;
        Vector3 SeatRoot
        {
            get
            {
                if (driverChair != null)
                {
                    var b = Bounds(driverChair.GetComponentsInChildren<Renderer>(true));
                    float y = seat != null ? seat.position.y : b.min.y;
                    return new Vector3(b.center.x, y, b.center.z);
                }
                return seat != null ? seat.position : transform.position;
            }
        }
        public static bool ThirdPerson { get => PlayerPrefs.GetInt(CamPref, 1) == 1; set { PlayerPrefs.SetInt(CamPref, value ? 1 : 0); PlayerPrefs.Save(); } }
        Transform _cam; Vector3 _camLocalPos; Quaternion _camLocalRot; bool _camSaved; Vector3 _camVel; bool _chaseInit;

        public bool DoorOpen => _doorTarget > 0.5f;
        Vector3 Fwd { get { var f = rig != null ? rig.forward : transform.forward; f.y = 0f; return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward; } }

        void Awake()
        {
            Live = SpawningDeployed;
            _rb = GetComponent<Rigidbody>();
            if (!Live) { if (_rb != null) _rb.isKinematic = true; return; }   // a copy placed in a scene: inert
            if (!baked) Bake();
            _rb = GetComponent<Rigidbody>(); if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.useGravity = false; _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.linearDamping = 0.08f; _rb.angularDamping = 1.2f; _rb.maxAngularVelocity = 3f;
            foreach (var mc in GetComponentsInChildren<MeshCollider>(true)) if (!mc.convex) _concave.Add(mc);
            if (console != null) _power = ConsolePower.Attach(console.gameObject, null);   // starts dark
            InitPhysicsRuntime();
        }

        // ================================================================== bake (editor setup, or runtime fallback)

        /// <summary>Measures the model and builds the rig. Model transforms are only read (the root rotation and the door
        /// are turned for the measurement and restored exactly).</summary>
        public void Bake()
        {
            var rot = transform.rotation;
            transform.rotation = Quaternion.identity;
            try { BakeInner(); }
            finally { transform.rotation = rot; }
            baked = true;
        }

        void BakeInner()
        {
            var old = transform.Find("[Rover Rig]");
            if (old != null) DestroyImmediate(old.gameObject);
            wheels.Clear();

            var renderers = new List<Renderer>(GetComponentsInChildren<Renderer>(true));
            var body = Bounds(renderers.ToArray());
            door = FindDeep(transform, "Door_Back");
            console = FindDeep(transform, "P_Cockpit_Tech_01");
            Vector3 doorC = door != null ? Bounds(door.GetComponentsInChildren<Renderer>(true)).center : new Vector3(body.center.x, body.center.y, body.min.z);
            Vector3 consC = console != null ? Bounds(console.GetComponentsInChildren<Renderer>(true)).center : new Vector3(body.center.x, body.center.y, body.max.z);
            var fwd = consC - doorC; fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);

            // Rig: world-unit child (undoes the root scale), facing the rover's forward.
            var rigGo = new GameObject("[Rover Rig]");
            rig = rigGo.transform;
            rig.SetParent(transform, false);
            var ls = transform.lossyScale;
            rig.localScale = new Vector3(1f / Mathf.Max(1e-4f, ls.x), 1f / Mathf.Max(1e-4f, ls.y), 1f / Mathf.Max(1e-4f, ls.z));
            rig.position = body.center;
            rig.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            Transform Marker(string name, Vector3 pos, Vector3 face)
            {
                var t = new GameObject(name).transform; t.SetParent(rig, false);
                t.position = pos; t.rotation = Quaternion.LookRotation(face, Vector3.up); return t;
            }

            // Driver seat: the P_Chair_05 nearest the console, driver (left) side on a tie.
            driverChair = null; float best = float.MaxValue;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.Equals("P_Chair_05", StringComparison.OrdinalIgnoreCase)) continue;
                var cb = Bounds(t.GetComponentsInChildren<Renderer>(true));
                float score = Vector3.Distance(cb.center, consC) + Vector3.Dot(cb.center - body.center, right) * 0.05f;
                if (score < best) { best = score; driverChair = t; }
            }
            Bounds chairB = driverChair != null ? Bounds(driverChair.GetComponentsInChildren<Renderer>(true)) : new Bounds(consC - fwd * 0.8f, Vector3.one * 0.5f);
            float floorY = chairB.min.y;
            var seatFace = consC - chairB.center; seatFace.y = 0f; if (seatFace.sqrMagnitude < 1e-4f) seatFace = fwd;
            seat = Marker("Seat", new Vector3(chairB.center.x, floorY, chairB.center.z) - seatFace.normalized * 0.05f, seatFace.normalized);
            seatedEyeHeight = Mathf.Clamp(Mathf.Min(1.15f, (body.max.y - floorY) * 0.75f), 0.6f, 1.15f);

            // Door: open rotation = the 60 degree swing that moves the door out, away from the cabin.
            var diXZ = new Vector3(doorC.x, floorY, doorC.z) + fwd * 0.8f;
            doorInside = Marker("DoorInside", new Vector3(diXZ.x, FloorBelow(diXZ + Vector3.up * 0.9f, floorY), diXZ.z), fwd);
            float reach = 0f;
            if (door != null)
            {
                doorClosed = door.localRotation;
                var plus = doorClosed * Quaternion.Euler(doorOpenDegrees, 0f, 0f);
                var minus = doorClosed * Quaternion.Euler(-doorOpenDegrees, 0f, 0f);
                door.localRotation = plus; var cP = Bounds(door.GetComponentsInChildren<Renderer>(true)).center;
                door.localRotation = minus; var cM = Bounds(door.GetComponentsInChildren<Renderer>(true)).center;
                door.localRotation = doorClosed;
                bool usePlus = Vector3.Dot(cP, fwd) < Vector3.Dot(cM, fwd);
                doorOpen = usePlus ? plus : minus;
                reach = Mathf.Max(0f, Vector3.Dot(doorC - (usePlus ? cP : cM), fwd)) * 2f;
            }
            doorOutside = Marker("DoorOutside", new Vector3(doorC.x, body.min.y, doorC.z) - fwd * (reach + 1.1f), fwd);

            // Wheels: colliders at the P_Wheel_Big_* wheels, radius from the wheel mesh (world metres).
            float wheelY = 0f, midF = 0f;
            var found = new List<(Transform vis, Vector3 c, float r)>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("P_Wheel_Big")) continue;
                var rs = t.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                var b = Bounds(rs);
                found.Add((rs[0].transform, b.center, Mathf.Max(0.1f, b.extents.y)));
                midF += Vector3.Dot(b.center, fwd); wheelY += b.center.y;
            }
            if (found.Count > 0) { midF /= found.Count; wheelY /= found.Count; } else wheelY = body.min.y;
            var wroot = new GameObject("Wheels").transform; wroot.SetParent(rig, false);
            float sprung = mass / Mathf.Max(1, found.Count);
            foreach (var w in found)
            {
                var go = new GameObject("WC " + w.vis.parent.name);
                go.transform.SetParent(wroot, false);
                go.transform.position = w.c + Vector3.up * suspension * 0.5f;
                var wc = go.AddComponent<WheelCollider>();
                wc.radius = w.r; wc.mass = 25f; wc.suspensionDistance = suspension; wc.forceAppPointDistance = 0.1f;
                float k = sprung * 90f;
                wc.suspensionSpring = new JointSpring { spring = k, damper = 0.6f * Mathf.Sqrt(k * sprung), targetPosition = 0.5f };
                var side = wc.sidewaysFriction; side.stiffness = 1.3f; wc.sidewaysFriction = side;
                var fw = wc.forwardFriction; fw.stiffness = 1.2f; wc.forwardFriction = fw;
                wheels.Add(new WheelSlot { collider = wc, visual = w.vis, front = Vector3.Dot(w.c, fwd) > midF });
            }

            // Driving chassis (off while parked): the hull only (no antenna / props / wheels), above the wheel centres.
            var chGo = new GameObject("Chassis"); chGo.transform.SetParent(rig, false);
            chGo.transform.position = body.center; chGo.transform.rotation = Quaternion.identity;
            chassis = chGo.AddComponent<BoxCollider>();
            FitChassisInner();
            chassis.enabled = false;

            // Wide entry zone behind the door (looking at the ground / door area there also gets the prompt).
            var zone = Marker("RearEntryZone", new Vector3(doorC.x, body.center.y, doorC.z) - fwd * 1.25f, fwd);
            var zc = zone.gameObject.AddComponent<BoxCollider>();
            zc.isTrigger = true;
            zc.size = new Vector3(Mathf.Max(2.5f, Mathf.Abs(Vector3.Dot(body.size, right)) + 1f), Mathf.Max(2.5f, body.size.y + 1f), 2.5f);

            com = Marker("COM", new Vector3(body.center.x, wheelY, body.center.z), fwd);

            if (GetComponent<RoverDoor>() == null) gameObject.AddComponent<RoverDoor>().rover = this;
            else GetComponent<RoverDoor>().rover = this;
            var rb = GetComponent<Rigidbody>(); if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false; rb.mass = mass;
        }

        /// <summary>Bounds of the hull mesh only: Rover_Body_01 (door included); falls back to every renderer outside
        /// _Props, the wheel setups and the rig.</summary>
        public Bounds HullBounds()
        {
            var hull = FindDeep(transform, "Rover_Body_01");
            var list = new List<Renderer>();
            if (hull != null) list.AddRange(hull.GetComponentsInChildren<Renderer>(true));
            if (list.Count == 0)
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                {
                    bool skip = false;
                    for (var t = r.transform; t != null && t != transform; t = t.parent)
                        if (t.name == "_Props" || t.name.StartsWith("P_Wheel") || t.name.StartsWith("P_Rover_Setup") || t == rig) { skip = true; break; }
                    if (!skip) list.Add(r);
                }
            return Bounds(list.ToArray());
        }

        /// <summary>Refits the baked chassis box to the hull (editor fix-up; the root must be upright).</summary>
        public void FitChassis()
        {
            if (chassis == null) return;
            var rot = transform.rotation;
            transform.rotation = Quaternion.identity;
            try { chassis.transform.rotation = Quaternion.identity; FitChassisInner(); }
            finally { transform.rotation = rot; }
        }

        void FitChassisInner()
        {
            var hull = HullBounds();
            float wheelY = hull.min.y; int n = 0; float sum = 0f;
            foreach (var w in wheels) if (w.collider != null) { sum += w.collider.transform.position.y - suspension * 0.5f; n++; }
            if (n > 0) wheelY = sum / n;
            float bottom = Mathf.Max(hull.min.y, wheelY + 0.1f);
            var ct = chassis.transform;
            ct.position = hull.center;
            chassis.center = ct.InverseTransformPoint(new Vector3(hull.center.x, (bottom + hull.max.y) * 0.5f, hull.center.z));
            var ls = ct.lossyScale;
            chassis.size = new Vector3(hull.size.x / ls.x, Mathf.Max(0.4f, hull.max.y - bottom) / ls.y, hull.size.z / ls.z);
        }

        /// <summary>Highest solid rover surface (not the rig, not triggers) under a point; fallback when nothing is hit.</summary>
        public float FloorBelow(Vector3 from, float fallback, float max = 3f)
        {
            Physics.SyncTransforms();
            float best = float.MaxValue, y = fallback;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, max, ~0, QueryTriggerInteraction.Ignore))
            {
                var c = h.collider;
                if (c == null || c.isTrigger || !c.transform.IsChildOf(transform) || (rig != null && c.transform.IsChildOf(rig))) continue;
                if (door != null && c.transform.IsChildOf(door)) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; }
            }
            return y;
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return c;
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

        /// <summary>Lowest wheel contact below the root (current pose).</summary>
        float LowestWheelBelowRoot()
        {
            float low = float.MaxValue;
            foreach (var w in wheels) if (w.collider != null) low = Mathf.Min(low, w.collider.transform.position.y - suspension * 0.5f - w.collider.radius);
            return low == float.MaxValue ? 0f : transform.position.y - low;
        }

        /// <summary>Sets the rover down on flat ground in front of the player, rear door toward the player.</summary>
        public bool PlaceNear(Transform player, float distance)
        {
            var pf = player.forward; pf.y = 0f; if (pf.sqrMagnitude < 0.01f) pf = Vector3.forward; pf.Normalize();
            for (int i = 0; i < 12; i++)
            {
                float ang = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 30f;
                var dir = Quaternion.Euler(0f, ang, 0f) * pf;
                var p = player.position + dir * distance;
                if (!Ground(p, player, out var hit) || hit.normal.y < 0.88f) continue;
                transform.rotation = Quaternion.FromToRotation(Fwd, dir) * transform.rotation;   // facing away: door toward the player
                var e = transform.eulerAngles; transform.rotation = Quaternion.Euler(0f, e.y, 0f);
                var c = com != null ? com.position : transform.position;
                var off = c - transform.position; off.y = 0f;
                transform.position = new Vector3(p.x, transform.position.y, p.z) - off;
                Physics.SyncTransforms();
                transform.position = new Vector3(transform.position.x, hit.point.y + WheelContactBelowRoot() + 0.03f, transform.position.z);
                Physics.SyncTransforms();
                SnapToGround();
                return true;
            }
            return false;
        }

        bool Ground(Vector3 p, Transform ignore, out RaycastHit best)
        {
            best = default;
            var hits = Physics.RaycastAll(p + Vector3.up * 60f, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform) || (ignore != null && h.collider.transform.IsChildOf(ignore))) continue;
                best = h; return true;
            }
            return false;
        }

        void SetDynamic(bool dynamic) => ApplyDriveState(dynamic);

        // ================================================================== door

        public void SetDoor(bool open) { _doorTarget = open ? 1f : 0f; if (open) _doorOpenedAt = Time.time; }

        void Update()
        {
            if (!Live) return;
            if (door != null && !Mathf.Approximately(_door01, _doorTarget))
            {
                _door01 = Mathf.MoveTowards(_door01, _doorTarget, Time.deltaTime / Mathf.Max(0.05f, doorSeconds));
                door.localRotation = Quaternion.Slerp(doorClosed, doorOpen, Mathf.SmoothStep(0f, 1f, _door01));
            }
            if (_state == State.Parked && DoorOpen && Time.time - _doorOpenedAt > 12f) SetDoor(false);

            RefreshSeatedFlags();
            TickCameraZoom();
            if (_state == State.Driving)
            {
                if (_player == null || !_player.Seated) { Release(); return; }   // stood up by a teleport / load
                if (!_player.InputEnabled || UIState.BlocksGameplay || Time.unscaledTime - _seatedAt < 0.4f) return;
                var kbd = Keyboard.current;
                if ((kbd != null && kbd[cameraKey].wasPressedThisFrame) || (Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame))
                {
                    ThirdPerson = !ThirdPerson;
                    if (!ThirdPerson) RestoreCamera();
                    _chaseInit = false;
                    ApplySeatLookForCamera();
                }
                bool e = _input != null && _input.InteractPressed && (_interactor == null || _interactor.Target == null);
                if (e)
                {
                    if (_rb.linearVelocity.magnitude > exitSpeed) Toast.Show("Stop the rover to get out.", 2f);
                    else StartCoroutine(ExitRoutine());
                }
            }
        }

        // ================================================================== enter / exit

        public bool CanEnter(FirstPersonController p) => Live && p != null && !p.Seated && _state == State.Parked;

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
            ApplyPresence(true);
            var p = _player;
            Vector3 start = p.transform.position;
            float yaw0 = p.transform.eulerAngles.y, pitch0 = p.Pitch, eye0 = p.StandingEyeHeight;
            p.BeginSeat(start);
            if (!DoorOpen) SetDoor(true);
            float fwdYaw = Quaternion.LookRotation(Fwd, Vector3.up).eulerAngles.y, backYaw = fwdYaw + 180f;
            var outside = doorOutside != null ? doorOutside.position : transform.position - Fwd * 4f;
            if (Ground(outside, p.transform, out var gh)) outside = gh.point;
            // from the front half: along the side first (never through the hull)
            var centre = com != null ? com.position : transform.position;
            if (Vector3.Dot(start - centre, Fwd) > 0f)
            {
                var right = Vector3.Cross(Vector3.up, Fwd);
                float side = Vector3.Dot(start - centre, right) >= 0f ? 1f : -1f;
                float halfW = chassis != null ? Mathf.Abs(Vector3.Dot(chassis.transform.TransformVector(chassis.size), right)) * 0.5f : 1.5f;
                var via = centre + right * side * (halfW + 1f);
                if (Ground(via, p.transform, out var vh)) via = vh.point; else via.y = start.y;
                var d0 = via - start; d0.y = 0f;
                yield return Glide(p, start, via, yaw0, backYaw, pitch0, 0f, eye0, eye0, Mathf.Clamp(d0.magnitude / 3.5f, 0.3f, 2f));
                start = via; yaw0 = backYaw; pitch0 = 0f;
            }
            var flat = start - outside; flat.y = 0f;
            if (flat.magnitude > 1.2f)
            {
                yield return Glide(p, start, outside, yaw0, fwdYaw, pitch0, 0f, eye0, eye0, Mathf.Clamp(flat.magnitude / 3.5f, 0.4f, 2f));
                start = outside; yaw0 = fwdYaw; pitch0 = 0f;
            }
            while (door != null && _door01 < 0.8f) yield return null;
            HideBody(p, true);
            var inside = doorInside != null ? doorInside.position : centre;
            yield return Glide(p, start, inside, yaw0, fwdYaw, pitch0, 0f, eye0, eye0);
            SetDoor(false);
            float seatYaw = LookYaw;
            yield return Glide(p, inside, SeatRoot, fwdYaw, seatYaw, 0f, 8f, eye0, seatedEyeHeight);
            CenterHead(p, true);
            _state = State.Driving;
            ApplySeatLookForCamera();
            RefreshSeatedFlags();
            _seatedAt = Time.unscaledTime;
            InteractionPromptHud.IdlePrompt = null; // hint bar already has E to exit; no world/idle popup (t222u)
            PowerUp();
        }

        /// <summary>Seated: the hand reaches out to the console and presses; the displays blink on (ship console sequence),
        /// then the drive is live.</summary>
        void PowerUp()
        {
            _powered = false;
            void On()
            {
                if (_state != State.Driving) return;
                if (_power != null) _power.PowerOn();
                _powered = true;
                SetDynamic(true);
                Toast.Show("Rover powered. W drive, S reverse, A/D steer, Space brake, Shift boost, V camera, R refuel. E to get out.", 6f);
            }
            var arms = _player != null ? _player.GetComponentInChildren<FirstPersonArms>() : null;
            var eye = _player != null && _player.ViewCamera != null ? _player.ViewCamera.transform.position : transform.position;
            if (arms != null) arms.Reach(PressPoint(eye), On); else On();
        }

        Vector3 PressPoint(Vector3 eye)
        {
            if (console == null) return eye + Fwd;
            var b = Bounds(console.GetComponentsInChildren<Renderer>());
            var aim = b.ClosestPoint(eye) * 0.5f + b.center * 0.5f;
            if (Physics.Raycast(eye, aim - eye, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.transform.IsChildOf(transform))
                return hit.point;
            return b.ClosestPoint(eye);
        }

        IEnumerator ExitRoutine()
        {
            _state = State.Exiting;
            InteractionPromptHud.IdlePrompt = null;
            ApplyPresence(false);
            RestoreCamera();
            SetDynamic(false);
            _powered = false;
            if (_power != null) _power.PowerOff();
            var p = _player;
            float fwdYaw = Quaternion.LookRotation(Fwd, Vector3.up).eulerAngles.y, backYaw = fwdYaw + 180f;
            var inside = doorInside != null ? doorInside.position : transform.position;
            yield return Glide(p, p.transform.position, inside, p.transform.eulerAngles.y, backYaw, p.Pitch, 0f, seatedEyeHeight, p.StandingEyeHeight);
            SetDoor(true);
            while (door != null && _door01 < 0.95f) yield return null;
            var outside = doorOutside != null ? doorOutside.position : transform.position - Fwd * 4f;
            if (Ground(outside, p.transform, out var hit)) outside = hit.point;
            yield return Glide(p, inside, outside, backYaw, backYaw, 0f, 0f, p.StandingEyeHeight, p.StandingEyeHeight);
            p.SetSeatPose(outside, backYaw, 0f, p.StandingEyeHeight);
            p.EndSeat();
            ClearDriverArms();
            HideBody(p, false);
            _player = null;
            _state = State.Parked;
            RefreshSeatedFlags();
            yield return new WaitForSeconds(1.2f);
            if (_state == State.Parked) SetDoor(false);
        }

        IEnumerator Glide(FirstPersonController p, Vector3 from, Vector3 to, float yawA, float yawB, float pitchA, float pitchB, float eyeA, float eyeB, float seconds = -1f)
        {
            float dur = seconds > 0f ? seconds : glideSeconds;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                if (p == null || !p.Seated) yield break;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                p.SetSeatPose(Vector3.Lerp(from, to, k), Mathf.LerpAngle(yawA, yawB, k), Mathf.Lerp(pitchA, pitchB, k), Mathf.Lerp(eyeA, eyeB, k));
                yield return null;
            }
            if (p != null && p.Seated) p.SetSeatPose(to, yawB, pitchB, eyeB);
        }

        void Release()
        {
            CenterHead(_player, false);
            RestoreCamera();
            SetDynamic(false);
            _powered = false;
            if (_power != null) _power.PowerOff();
            ApplyPresence(false);
            ClearDriverArms();
            HideBody(_player, false);
            InteractionPromptHud.IdlePrompt = null;
            _player = null;
            _state = State.Parked;
            RefreshSeatedFlags();
        }

        void OnDisable() { if (Live && _state != State.Parked && _player != null) { ClearDriverArms(); if (_player.Seated) _player.EndSeat(); Release(); } if (SeatedRover == this) SeatedRover = null; AnySeated = false; ChaseActive = false; if (Live) ApplyPresence(false); }

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

        // ================================================================== driving

        void FixedUpdate()
        {
            if (!Live || _rb == null) return;
            PhysicsTick();
            if (_rb.isKinematic || _state != State.Driving || !_powered) return;

            float throttle = 0f, steer = 0f; bool hand = false, wantBoost = false;
            if (_player != null && _player.InputEnabled && !UIState.BlocksGameplay)
            {
                var kb = Keyboard.current; var gp = Gamepad.current;
                if (kb != null)
                {
                    throttle += (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                    steer += (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                    hand = kb.spaceKey.isPressed;
                    wantBoost = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                }
                if (gp != null)
                {
                    throttle += gp.rightTrigger.ReadValue() - gp.leftTrigger.ReadValue();
                    steer += gp.leftStick.ReadValue().x;
                    hand |= gp.buttonEast.isPressed;
                    wantBoost |= gp.leftStickButton.isPressed || gp.rightShoulder.isPressed;
                }
                throttle = Mathf.Clamp(throttle, -1f, 1f); steer = Mathf.Clamp(steer, -1f, 1f);
            }

            float dt = Time.fixedDeltaTime;
            var dir = RoverDirector.Instance;
            float fuel = dir != null ? dir.Fuel : 0f;
            bool boosting = wantBoost && fuel > 0.01f && _boost01 > 0.02f && throttle > 0.05f;
            if (boosting) _boost01 = Mathf.Max(0f, _boost01 - dt / Mathf.Max(0.1f, boostSeconds));
            else _boost01 = Mathf.Min(1f, _boost01 + dt / Mathf.Max(0.1f, boostRechargeSeconds));
            boosting = boosting && _boost01 > 0.001f;

            float v = Vector3.Dot(_rb.linearVelocity, Fwd);
            bool dry = fuel <= 0.01f;
            if (dry) throttle = 0f;
            float cap = boosting ? boostSpeed : maxSpeed;
            float limit = throttle >= 0f ? cap : maxSpeed / 3f;
            float rise = _profile != null ? Mathf.Max(0.05f, _profile.throttleRise) : 0.45f;
            float fall = _profile != null ? Mathf.Max(0.05f, _profile.throttleFall) : 0.22f;
            float rate = Mathf.Abs(throttle) >= Mathf.Abs(_throttleSmooth) ? 1f / rise : 1f / fall;
            _throttleSmooth = Mathf.MoveTowards(_throttleSmooth, throttle, rate * dt);
            float engage = _profile != null ? Mathf.Max(0.05f, _profile.reverseEngageSpeed) : 0.35f;
            bool almostStopped = Mathf.Abs(v) <= engage;
            // Opposite throttle, or the handbrake, only applies brake torque until nearly stopped — never reverse drive (t225u).
            bool opposing = !almostStopped && Mathf.Abs(throttle) > 0.05f && Mathf.Sign(throttle) != Mathf.Sign(v);
            bool coast = Mathf.Abs(throttle) < 0.05f;
            float motor = 0f, brake = 0f;
            if (hand || opposing)
            {
                motor = 0f;
                brake = _brakeTorque * (hand ? 1.3f : Mathf.Abs(throttle));
                _throttleSmooth = 0f;
            }
            else if (coast)
            {
                motor = 0f;
                brake = _brakeTorque * 0.05f;
            }
            else if (dry || Mathf.Abs(v) > limit)
            {
                motor = 0f;
            }
            else
            {
                // Motor sign is flipped so W (positive throttle) drives along visual / console forward.
                motor = -_throttleSmooth * _motorTorque * (boosting ? 1.35f : 1f);
            }
            float steerDeg = steer * Mathf.Lerp(maxSteer, highSpeedSteer, Mathf.Clamp01(Mathf.Abs(v) / Mathf.Max(0.01f, cap)));
            foreach (var w in wheels)
            {
                if (w.collider == null) continue;
                w.collider.motorTorque = motor;
                w.collider.brakeTorque = brake;
                if (w.front) w.collider.steerAngle = steerDeg;
            }

            ClampPlanarSpeed(limit);
            var planar = _rb.linearVelocity; planar.y = 0f;
            float use = (Mathf.Abs(throttle) * fuelPerSecond + planar.magnitude * fuelPerMetre) * (boosting ? boostFuelMul : 1f) * dt;
            if (use > 0f && dir != null) dir.SpendFuel(use);
            float fuel01 = dir != null ? dir.Fuel01 : 0f;
            if (dir != null && dir.Fuel <= 0.01f)
            {
                if (!_warnedEmpty) { _warnedEmpty = true; Toast.Show("Rover out of fuel. Press R to refuel, or use the ship refuelling terminal.", 5f); }
            }
            else if (fuel01 <= 0.2f)
            {
                if (!_warnedLow) { _warnedLow = true; Toast.Show("Rover fuel low.", 3.5f); }
            }
            else { _warnedLow = false; _warnedEmpty = false; }
        }

        void LateUpdate()
        {
            if (!Live) return;
            SyncSuspensionAndWheels();
            HideDriverArms();
            TickPresence();
            RefreshSeatedFlags();
            if (_state == State.Driving && _player != null && _player.Seated && seat != null)
            {
                _player.SetSeatFrame(SeatRoot, LookYaw);
                if (ThirdPerson && _powered) ChaseCamera();
            }
        }

        void ApplyPlaytestTune() => ApplyProfile();

        void ApplyHandling()
        {
            if (_rb != null) { _rb.mass = mass; _rb.angularDamping = 1.2f; _rb.maxAngularVelocity = 3f; }
            foreach (var w in wheels)
            {
                if (w.collider == null) continue;
                var side = w.collider.sidewaysFriction;
                side.stiffness = 2.2f; side.extremumSlip = 0.4f; side.asymptoteSlip = 0.8f;
                side.extremumValue = 1f; side.asymptoteValue = 0.75f;
                w.collider.sidewaysFriction = side;
                var fw = w.collider.forwardFriction;
                fw.stiffness = 1.8f; fw.extremumSlip = 0.4f; fw.asymptoteSlip = 0.8f;
                fw.extremumValue = 1f; fw.asymptoteValue = 0.75f;
                w.collider.forwardFriction = fw;
            }
        }

        void Stabilize()
        {
            var up = transform.up;
            var planar = _rb.linearVelocity; planar.y = 0f;
            _rb.AddForce(Vector3.down * (0.08f * planar.sqrMagnitude), ForceMode.Acceleration);
            if (up.y < 0.98f && up.y > 0.2f)
            {
                var correct = Vector3.Cross(up, Vector3.up);
                float tip = 1f - Mathf.Clamp01(up.y);
                _rb.AddTorque(correct * (tip * 14f), ForceMode.Acceleration);
            }
        }

        void CenterHead(FirstPersonController p, bool on)
        {
            var head = p != null && p.ViewCamera != null ? p.ViewCamera.transform : _head;
            if (head == null) return;
            if (on && !_headCentered)
            {
                var lp = head.localPosition;
                _headXZ = new Vector3(lp.x, 0f, lp.z);
                lp.x = 0f; lp.z = 0f;
                head.localPosition = lp;
                _head = head;
                _headCentered = true;
            }
            else if (!on && _headCentered)
            {
                var lp = head.localPosition;
                lp.x = _headXZ.x; lp.z = _headXZ.z;
                head.localPosition = lp;
                _headCentered = false; _head = null;
            }
        }

        /// <summary>3rd person: behind and above the rover, smoothed; the seat's free look orbits it.</summary>
        void ChaseCamera()
        {
            var cam = _player.ViewCamera;
            if (cam == null) return;
            if (!_camSaved || _cam != cam.transform) { _cam = cam.transform; _camLocalPos = _cam.localPosition; _camLocalRot = _cam.localRotation; _camSaved = true; _chaseInit = false; }
            float roverYaw = LookYaw;
            float orbit = Mathf.DeltaAngle(roverYaw, _player.transform.eulerAngles.y);
            var dir = Quaternion.Euler(0f, roverYaw + orbit, 0f) * Vector3.forward;
            var centre = (com != null ? com.position : transform.position) + Vector3.up * 1.2f;
            float lift = Mathf.Clamp(_player.Pitch, -30f, 50f) / 50f;
            var want = centre - dir * chaseDistance + Vector3.up * (chaseHeight + lift * 2.5f);
            var to = want - centre;
            foreach (var h in Physics.SphereCastAll(centre, 0.3f, to.normalized, to.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(transform) && !h.collider.transform.IsChildOf(_player.transform) && h.distance < to.magnitude)
                    want = centre + to.normalized * Mathf.Max(1.5f, h.distance - 0.2f);
            if (!_chaseInit) { _cam.position = want; _camVel = Vector3.zero; _chaseInit = true; }
            else _cam.position = Vector3.SmoothDamp(_cam.position, want, ref _camVel, chaseSmooth);
            _cam.rotation = Quaternion.LookRotation(centre + dir * 2f - _cam.position, Vector3.up);
        }

        void RestoreCamera()
        {
            if (_camSaved && _cam != null) { _cam.localPosition = _camLocalPos; _cam.localRotation = _camLocalRot; }
            _camSaved = false; _chaseInit = false;
        }
    }
}
