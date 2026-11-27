using System.Collections.Generic;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// Runtime physics, suspension (P_Rover_Setup_* pivots), parked settle, and chase-cam zoom (t213u-t217u).
    /// Does not move authored pivot positions or write the Grasshopper prefab.
    /// </summary>
    public sealed partial class RoverVehicle
    {
        public static bool AnySeated { get; private set; }
        public static RoverVehicle SeatedRover { get; private set; }
        public static bool ChaseActive { get; private set; }
        public static bool ThirdPersonView => ThirdPerson;

        const string ZoomPref = "xa.rover.zoom";
        RoverPhysicsProfile _profile;
        int _zoomStep = 1;
        float _settleTimer;
        float _throttleSmooth;
        float _prevFwdVel;
        bool _pivotsBound;
        readonly List<Corner> _corners = new List<Corner>();

        sealed class Corner
        {
            public WheelSlot slot;
            public Transform pivot;          // P_Rover_Setup_FLeft_01 / FRight / LRear / RRear (or legacy Left/Right)
            public Quaternion pivotRestLocal;
            public Transform wheel;          // visual wheel
            public Quaternion wheelRestLocal;
            public float spin;
        }

        void InitPhysicsRuntime()
        {
            _profile = RoverPhysicsProfile.LoadOrDefault();
            _zoomStep = Mathf.Clamp(PlayerPrefs.GetInt(ZoomPref, 1), 0, 2);
            ApplyProfile();
            AlignWheelCollidersToVisuals();
            BindPivots();
            StartParkedPhysics(true);
            ApplyPresence(false);
        }

        void ApplyProfile()
        {
            var p = _profile != null ? _profile : RoverPhysicsProfile.LoadOrDefault();
            _profile = p;
            mass = p.mass;
            acceleration = p.acceleration;
            brakeDecel = p.brakeDecel;
            maxSpeed = p.MaxSpeedMs;
            boostSpeed = p.BoostSpeedMs;
            boostSeconds = p.boostSeconds;
            boostRechargeSeconds = p.boostRechargeSeconds;
            maxSteer = p.maxSteer;
            highSpeedSteer = p.highSpeedSteer;
            suspension = p.travel;
            p.DistanceForZoom(_zoomStep, out chaseDistance, out chaseHeight);
            if (_rb != null)
            {
                _rb.mass = mass;
                _rb.angularDamping = p.angularDamping;
                ApplyPitchInertia();
                _rb.maxAngularVelocity = 3f;
            }
            float r = AverageWorldRadius();
            int n = Mathf.Max(1, WheelCount());
            _motorTorque = mass * acceleration * r / n;
            _brakeTorque = mass * brakeDecel * r / n;
            ApplyHandlingFromProfile();
            ApplySuspensionSprings();
        }

        int WheelCount()
        {
            int n = 0;
            foreach (var w in wheels) if (w != null && w.collider != null) n++;
            return n;
        }

        static float WorldRadius(WheelCollider wc)
        {
            if (wc == null) return 0.3f;
            var s = wc.transform.lossyScale;
            float sc = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z), 1e-4f);
            return wc.radius * sc;
        }

        float AverageWorldRadius()
        {
            float r = 0f; int n = 0;
            foreach (var w in wheels)
                if (w != null && w.collider != null) { r += WorldRadius(w.collider); n++; }
            return n > 0 ? r / n : 0.35f;
        }

        float ScaleOf(WheelCollider wc)
        {
            var s = wc.transform.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z), 1e-4f);
        }

        void AlignWheelCollidersToVisuals()
        {
            var p = _profile;
            float travel = p != null ? p.travel : suspension;
            float target = p != null ? p.targetPosition : 0.5f;
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                var vis = w.visual;
                if (vis == null) continue;
                var rend = vis.GetComponentInChildren<Renderer>();
                var b = rend != null ? rend.bounds : new Bounds(vis.position, Vector3.one * 0.4f);
                float worldR = Mathf.Max(0.08f, b.extents.y);
                float sc = ScaleOf(w.collider);
                w.collider.radius = worldR / sc;
                w.collider.suspensionDistance = travel / sc;
                w.collider.forceAppPointDistance = (p != null ? p.forceAppPointDistance : 0f) / sc;
                w.collider.mass = 25f;
                // Attachment sits above the visual centre so mid-travel matches the authored wheel.
                w.collider.transform.position = b.center + Vector3.up * (travel * target);
                w.collider.transform.rotation = Quaternion.LookRotation(Fwd.sqrMagnitude > 0.01f ? Fwd : transform.forward, Vector3.up);
            }
        }

        void ApplySuspensionSprings()
        {
            var p = _profile;
            if (p == null) return;
            int n = Mathf.Max(1, WheelCount());
            float sprung = mass / n;
            float spring = p.spring > 0f ? p.spring : sprung * 90f;
            float damper = p.damper > 0f ? p.damper : 0.4f * 2f * Mathf.Sqrt(spring * sprung);
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                w.collider.suspensionSpring = new JointSpring
                {
                    spring = spring,
                    damper = damper,
                    targetPosition = Mathf.Clamp01(p.targetPosition)
                };
            }
        }

        void ApplyHandlingFromProfile()
        {
            var p = _profile;
            if (p == null) { ApplyHandling(); return; }
            if (_rb != null) { _rb.mass = mass; _rb.angularDamping = p != null ? p.angularDamping : 2.2f; _rb.maxAngularVelocity = 3f; ApplyPitchInertia(); }
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                var side = w.collider.sidewaysFriction;
                side.stiffness = p.sidewaysStiffness; side.extremumSlip = 0.4f; side.asymptoteSlip = 0.8f;
                side.extremumValue = 1f; side.asymptoteValue = 0.75f;
                w.collider.sidewaysFriction = side;
                var fw = w.collider.forwardFriction;
                fw.stiffness = p.forwardStiffness; fw.extremumSlip = 0.4f; fw.asymptoteSlip = 0.8f;
                fw.extremumValue = 1f; fw.asymptoteValue = 0.75f;
                w.collider.forwardFriction = fw;
            }
        }

        /// <summary>
        /// Bind each wheel to its authored P_Rover_Setup_* pivot (t217u: FLeft, FRight, LRear, RRear,
        /// with a fallback to older Left/Right names). Pivot positions are never written.
        /// </summary>
        void BindPivots()
        {
            _corners.Clear();
            _pivotsBound = true;
            foreach (var w in wheels)
            {
                if (w == null || w.visual == null) continue;
                var c = new Corner { slot = w, wheel = w.visual, wheelRestLocal = w.visual.localRotation };
                Transform t = w.visual;
                while (t != null && t != transform)
                {
                    if (IsSetupPivot(t.name)) { c.pivot = t; c.pivotRestLocal = t.localRotation; break; }
                    t = t.parent;
                }
                _corners.Add(c);
            }
        }

        static bool IsSetupPivot(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("P_Rover_Setup_")) return false;
            // Preferred (t217u) and legacy baked names.
            return name.IndexOf("FLeft", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("FRight", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("LRear", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("RRear", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Live parked: stay non-kinematic, chassis on, concave meshes off, handbrake, optional snap to ground.</summary>
        void StartParkedPhysics(bool snap)
        {
            if (_rb == null) return;
            foreach (var mc in _concave) if (mc != null) mc.enabled = false;
            if (chassis != null) chassis.enabled = true;
            if (_rb.isKinematic) _rb.isKinematic = false;
            ApplyCom();
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                w.collider.motorTorque = 0f;
                w.collider.brakeTorque = _brakeTorque * 1.4f;
                w.collider.steerAngle = 0f;
            }
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            if (snap) SnapToGround();
            _rb.WakeUp();
            _settleTimer = 0f;
        }

        void StartDrivingPhysics()
        {
            if (_rb == null) return;
            foreach (var mc in _concave) if (mc != null) mc.enabled = false;
            if (chassis != null) chassis.enabled = true;
            _rb.isKinematic = false;
            ApplyCom();
            ApplyHandlingFromProfile();
            ApplySuspensionSprings();
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                w.collider.motorTorque = 0f;
                w.collider.brakeTorque = 0f;
            }
            _rb.WakeUp();
        }

        void ApplyCom()
        {
            if (_rb == null) return;
            // Midway between the axles, then lowered and optionally shifted fore/aft (t224u).
            Vector3 mid = Vector3.zero; int n = 0;
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                mid += w.collider.transform.position;
                n++;
            }
            if (n > 0) mid /= n;
            else mid = com != null ? com.position : transform.position;
            float down = _profile != null ? _profile.comDown : 0.55f;
            float fore = _profile != null ? _profile.comForeAft : 0f;
            mid += Vector3.down * down;
            mid += Fwd * fore;
            _rb.centerOfMass = transform.InverseTransformPoint(mid);
        }

        void ApplyPitchInertia()
        {
            if (_rb == null) return;
            float mul = _profile != null ? _profile.pitchInertia : 1.6f;
            _rb.ResetInertiaTensor();
            if (mul > 1.01f)
            {
                var it = _rb.inertiaTensor;
                it.x *= mul;
                _rb.inertiaTensor = it;
            }
        }

        void ApplyDriveState(bool driving)
        {
            if (driving) StartDrivingPhysics();
            else StartParkedPhysics(true);
        }

        /// <summary>
        /// Raycast each wheel down and move the root so contacts sit at rest-suspension height.
        /// Does not move P_Rover_Setup_* pivot local positions.
        /// </summary>
        public void SnapToGround()
        {
            if (wheels == null || wheels.Count == 0) return;
            float travel = _profile != null ? _profile.travel : suspension;
            float target = _profile != null ? _profile.targetPosition : 0.45f;
            // At rest the contact is roughly radius + travel * (1 - target) below the attachment,
            // which we aligned so the visual centre sits at travel * target below the attachment.
            float sum = 0f; int n = 0;
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                var vis = w.visual;
                Vector3 origin = vis != null ? vis.position + Vector3.up * 4f : w.collider.transform.position + Vector3.up * 4f;
                var hits = Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                RaycastHit hit = default; bool ok = false;
                foreach (var h in hits)
                {
                    if (h.collider.transform.IsChildOf(transform)) continue;
                    hit = h; ok = true; break;
                }
                if (!ok) continue;
                float worldR = WorldRadius(w.collider);
                float wantContactY = hit.point.y;
                Vector3 wheelPos = vis != null ? vis.position : w.collider.transform.position;
                // Desired visual-centre height at rest: ground + radius (wheel sitting on terrain).
                float wantCentreY = wantContactY + worldR;
                sum += wantCentreY - wheelPos.y;
                n++;
            }
            if (n == 0) return;
            var tp = transform.position;
            tp.y += sum / n;
            transform.position = tp;
            Physics.SyncTransforms();
            if (_rb != null && !_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        float PlanetGravity()
        {
            float mul = _profile != null ? _profile.gravityMultiplier : 1f;
            var env = FindFirstObjectByType<EnvironmentDirector>();
            if (env != null && env.Current != null && env.Current.gravity > 0.05f)
                return env.Current.gravity * mul;
            if (_player != null && _player.gravity > 0.05f) return _player.gravity * mul;
            return 9.81f * mul;
        }

        bool AnyWheelGrounded()
        {
            foreach (var w in wheels)
                if (w != null && w.collider != null && w.collider.isGrounded) return true;
            return false;
        }

        void PhysicsTick()
        {
            if (!Live || _rb == null) return;
            if (_rb.isKinematic)
            {
                // Inert scene copies stay kinematic. Live rovers should not.
                return;
            }

            float g = PlanetGravity();
            bool grounded = AnyWheelGrounded();
            var p = _profile;
            _rb.AddForce(Vector3.down * g, ForceMode.Acceleration);
            if (grounded && p != null && p.extraDownforceG > 0f)
                _rb.AddForce(Vector3.down * (g * p.extraDownforceG), ForceMode.Acceleration);

            var vel = _rb.linearVelocity;
            var planar = vel; planar.y = 0f;
            float downforce = (p != null ? p.downforceCoeff : 0.12f) * planar.sqrMagnitude;
            _rb.AddForce(Vector3.down * downforce, ForceMode.Acceleration);

            // Anti-flip
            var up = transform.up;
            if (up.y < 0.98f && up.y > 0.2f)
            {
                var correct = Vector3.Cross(up, Vector3.up);
                float tip = 1f - Mathf.Clamp01(up.y);
                float k = p != null ? p.antiFlip : 16f;
                _rb.AddTorque(correct * (tip * k), ForceMode.Acceleration);
            }

            // Air control / launch clamp
            float maxUp = p != null ? p.maxAirUpSpeed : 5.5f;
            float maxDown = p != null ? p.maxAirDownSpeed : 14f;
            if (!grounded)
            {
                float air = p != null ? p.airborneDamping : 1.6f;
                vel = _rb.linearVelocity;
                float damp = Mathf.Exp(-air * Time.fixedDeltaTime);
                vel.x *= damp;
                vel.z *= damp;
                _rb.linearVelocity = vel;
            }
            vel = _rb.linearVelocity;
            if (vel.y > maxUp) vel.y = maxUp;
            if (vel.y < -maxDown) vel.y = -maxDown;
            _rb.linearVelocity = vel;

            ApplyAntiPitch(grounded);

            bool parked = _state != State.Driving || !_powered;
            if (parked)
            {
                foreach (var w in wheels)
                {
                    if (w == null || w.collider == null) continue;
                    w.collider.motorTorque = 0f;
                    w.collider.brakeTorque = _brakeTorque * 1.4f;
                    w.collider.steerAngle = 0f;
                }
                vel = _rb.linearVelocity;
                if (grounded && vel.magnitude < 0.15f && _rb.angularVelocity.magnitude < 0.15f)
                {
                    _settleTimer += Time.fixedDeltaTime;
                    if (_settleTimer > 0.4f)
                    {
                        _rb.linearVelocity = Vector3.zero;
                        _rb.angularVelocity = Vector3.zero;
                        _rb.Sleep();
                    }
                }
                else _settleTimer = 0f;
            }
        }

        void ClampPlanarSpeed(float cap)
        {
            if (_rb == null || _rb.isKinematic) return;
            var vel = _rb.linearVelocity;
            var planar = new Vector3(vel.x, 0f, vel.z);
            if (planar.magnitude > cap)
            {
                planar = planar.normalized * cap;
                vel.x = planar.x;
                vel.z = planar.z;
                _rb.linearVelocity = vel;
            }
        }

        void SyncSuspensionAndWheels()
        {
            if (!_pivotsBound || _corners.Count == 0) BindPivots();
            float dt = Time.deltaTime;
            foreach (var c in _corners)
            {
                if (c.slot == null || c.slot.collider == null || c.wheel == null) continue;
                var wc = c.slot.collider;
                wc.GetWorldPose(out var posePos, out var poseRot);

                // Rotate the authored setup pivot so the wheel tracks the collider, without moving the pivot.
                if (c.pivot != null)
                {
                    c.pivot.localRotation = c.pivotRestLocal;
                    Vector3 pivotPos = c.pivot.position;
                    Vector3 restWheel = c.wheel.position;
                    Vector3 axis = c.pivot.right;
                    if (axis.sqrMagnitude < 1e-6f) axis = transform.right;
                    Vector3 from = Vector3.ProjectOnPlane(restWheel - pivotPos, axis);
                    Vector3 to = Vector3.ProjectOnPlane(posePos - pivotPos, axis);
                    if (from.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f)
                    {
                        float ang = Vector3.SignedAngle(from, to, axis);
                        ang = Mathf.Clamp(ang, -35f, 35f);
                        c.pivot.rotation = Quaternion.AngleAxis(ang, axis) * c.pivot.rotation;
                    }
                }

                // Spin and (front) steer on the wheel mesh itself, around its own axle — not RotateAround of bounds (wobble).
                if (Mathf.Abs(wc.rpm) > 0.01f) c.spin += wc.rpm * 6f * dt; // rpm * 360 * dt / 60
                var spin = Quaternion.AngleAxis(c.spin, Vector3.right);
                if (c.slot.front)
                {
                    var steer = Quaternion.AngleAxis(wc.steerAngle, Vector3.up);
                    c.wheel.localRotation = c.wheelRestLocal * steer * spin;
                }
                else c.wheel.localRotation = c.wheelRestLocal * spin;
            }
        }

        void ApplyAntiPitch(bool grounded)
        {
            if (!grounded || _rb == null || _state != State.Driving || !_powered) { _prevFwdVel = _rb != null ? Vector3.Dot(_rb.linearVelocity, Fwd) : 0f; return; }
            var p = _profile;
            var right = transform.right;
            float pitchRate = Vector3.Dot(_rb.angularVelocity, right); // + = nose up
            float damp = p != null ? p.antiPitch : 18f;
            if (damp > 0f) _rb.AddTorque(-right * pitchRate * damp, ForceMode.Acceleration);

            float dt = Time.fixedDeltaTime;
            float vNow = Vector3.Dot(_rb.linearVelocity, Fwd);
            float longAcc = dt > 1e-5f ? (vNow - _prevFwdVel) / dt : 0f;
            _prevFwdVel = vNow;
            float accMag = Mathf.Min(Mathf.Abs(longAcc), 25f);
            bool braking = (vNow > 0.2f && longAcc < -0.4f) || (vNow < -0.2f && longAcc > 0.4f);
            bool accel = !braking && accMag > 0.4f && Mathf.Abs(vNow) > 0.05f;
            float noseDown = 0f;
            if (accel) noseDown += (p != null ? p.antiSquat : 10f) * accMag;
            if (braking)
            {
                float dive = p != null ? p.antiDive : 16f;
                // Always a little nose-down on the brakes (real dive). If the nose is rising, fight it hard.
                noseDown += dive * 0.35f * accMag;
                if (pitchRate > 0f) noseDown += dive * pitchRate;
            }
            if (noseDown > 0f) _rb.AddTorque(-right * noseDown, ForceMode.Acceleration);
        }

        void HideDriverArms()
        {
            var arms = _player != null ? _player.GetComponentInChildren<FirstPersonArms>(true) : null;
            if (arms == null) return;
            bool tool = false;
            var held = _player.GetComponentInChildren<HeldToolVisuals>(true);
            if (held != null) tool = held.HasTool;
            // 3rd person: always hide arms and the held tool. 1st person: show them only while a tool is equipped.
            arms.ForcedHidden = _state == State.Driving && (ThirdPerson || !tool);
        }

        void ClearDriverArms()
        {
            var arms = _player != null ? _player.GetComponentInChildren<FirstPersonArms>(true) : null;
            if (arms != null) arms.ForcedHidden = false;
        }

        void TickCameraZoom()
        {
            if (_state != State.Driving || !ThirdPerson || _player == null) return;
            if (!_player.InputEnabled || UIState.BlocksGameplay) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;
            int next = Mathf.Clamp(_zoomStep + (scroll > 0f ? -1 : 1), 0, 2);
            if (next == _zoomStep) return;
            _zoomStep = next;
            PlayerPrefs.SetInt(ZoomPref, _zoomStep);
            if (_profile != null) _profile.DistanceForZoom(_zoomStep, out chaseDistance, out chaseHeight);
            else { chaseDistance = _zoomStep == 0 ? 8f : _zoomStep == 2 ? 22f : 14f; chaseHeight = _zoomStep == 0 ? 3.2f : _zoomStep == 2 ? 6.8f : 4.8f; }
        }

        void ApplySeatLookForCamera()
        {
            if (_player == null) return;
            // Symmetric around rover forward (LookYaw). 1st person stays ±80; follow cam gets a wider, still-symmetric orbit.
            if (ThirdPerson) _player.EnableSeatLook(LookYaw, 160f, 55f, 60f);
            else _player.EnableSeatLook(LookYaw, 80f, 55f, 60f);
        }

        float WheelContactBelowRoot()
        {
            float low = float.MaxValue;
            foreach (var w in wheels)
            {
                if (w == null || w.collider == null) continue;
                float y = w.collider.transform.position.y - WorldRadius(w.collider) - (_profile != null ? _profile.travel * (1f - _profile.targetPosition) : suspension * 0.5f);
                if (y < low) low = y;
            }
            return low == float.MaxValue ? 0f : transform.position.y - low;
        }

        void RefreshSeatedFlags()
        {
            AnySeated = Live && _state == State.Driving;
            SeatedRover = AnySeated ? this : (SeatedRover == this ? null : SeatedRover);
            ChaseActive = AnySeated && ThirdPerson;
        }
    }
}
