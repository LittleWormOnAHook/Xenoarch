#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// DEV / TESTING only: rover inclinometer. Samples pitch / roll (deg, + = nose up / right side up, also relative to the
    /// ground slope under the rover), body rates, vertical and forward speed and every wheel's grounded flag, suspension
    /// compression and normal force at ~20 Hz into Logs/xenoarch_rover_inclinometer.csv, with [RoverDiag] summary lines.
    /// Never attached unless <see cref="Enabled"/> (default off; the dev rover test or the Xenoarch/Dev menu turns it on).
    /// F10 toggles a small debug-only IMGUI readout while attached (off by default; it is not part of the game UI).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoverInclinometer : MonoBehaviour
    {
        public const string EditorKey = "Xenoarch.RoverDiag";
        public static bool ForceEnabled;
        public static bool ShowHud;
        public static RoverInclinometer Active { get; private set; }
        public static bool Enabled
        {
            get
            {
                if (ForceEnabled) return true;
#if UNITY_EDITOR
                return UnityEditor.SessionState.GetBool(EditorKey, false);
#else
                return false;
#endif
            }
        }

        [Tooltip("Samples per second written to the CSV.")] public float hz = 20f;

        public sealed class Stats
        {
            public string name; public int n;
            double sP, sP2, sR, sR2, sRelP, sRelP2, sRelR, sRelR2, sVy2;
            public float maxP = -999f, minP = 999f, maxR = -999f, minR = 999f, maxRelP = -999f, minRelP = 999f, maxRelR = -999f, minRelR = 999f;
            public float maxAbsPitchRate, maxAbsRollRate, maxAbsVy, maxSpeed, minSpeed, minGrounded = 4f, maxY = -1e9f, minY = 1e9f, maxRearLiftDeg;
            public int liftSamples; public int vySignFlips; float _lastVy;
            public float duration, startT; public bool endedSleeping; public float maxYawRate;
            public string firstLift = ""; public string contacts = "";
            public void Add(float t, float pitch, float roll, float relP, float relR, float pitchRate, float rollRate, float yawRate, float vy, float speed, int grounded, float y, string liftWheels)
            {
                n++; sP += pitch; sP2 += pitch * pitch; sR += roll; sR2 += roll * roll;
                sRelP += relP; sRelP2 += relP * relP; sRelR += relR; sRelR2 += relR * relR; sVy2 += vy * vy;
                maxP = Mathf.Max(maxP, pitch); minP = Mathf.Min(minP, pitch); maxR = Mathf.Max(maxR, roll); minR = Mathf.Min(minR, roll);
                maxRelP = Mathf.Max(maxRelP, relP); minRelP = Mathf.Min(minRelP, relP); maxRelR = Mathf.Max(maxRelR, relR); minRelR = Mathf.Min(minRelR, relR);
                maxAbsPitchRate = Mathf.Max(maxAbsPitchRate, Mathf.Abs(pitchRate)); maxAbsRollRate = Mathf.Max(maxAbsRollRate, Mathf.Abs(rollRate));
                maxYawRate = Mathf.Max(maxYawRate, Mathf.Abs(yawRate));
                maxAbsVy = Mathf.Max(maxAbsVy, Mathf.Abs(vy)); maxSpeed = Mathf.Max(maxSpeed, speed); minSpeed = Mathf.Min(minSpeed, speed);
                minGrounded = Mathf.Min(minGrounded, grounded); maxY = Mathf.Max(maxY, y); minY = Mathf.Min(minY, y);
                if (grounded < 4) { liftSamples++; if (firstLift.Length == 0) firstLift = liftWheels + "@" + (t - startT).ToString("0.0") + "s"; }
                if (Mathf.Abs(vy) > 0.05f) { if (_lastVy != 0f && Mathf.Sign(vy) != Mathf.Sign(_lastVy)) vySignFlips++; _lastVy = vy; }
            }
            static float Sd(double s, double s2, int n) { if (n < 2) return 0f; double m = s / n; return Mathf.Sqrt((float)Mathf.Max(0f, (float)(s2 / n - m * m))); }
            public float MeanP => n > 0 ? (float)(sP / n) : 0f;
            public float MeanR => n > 0 ? (float)(sR / n) : 0f;
            public float SdP => Sd(sP, sP2, n);          // pitch oscillation (RMS about the mean)
            public float SdR => Sd(sR, sR2, n);
            public float SdRelP => Sd(sRelP, sRelP2, n);
            public float SdRelR => Sd(sRelR, sRelR2, n);
            public float RmsVy => n > 0 ? Mathf.Sqrt((float)(sVy2 / n)) : 0f;
            public string Line()
            {
                return $"phase={name} dur={duration:0.0}s n={n} | pitch mean={MeanP:0.00} rms-osc={SdP:0.000} min={minP:0.00} max={maxP:0.00} p2p={maxP - minP:0.00} | roll mean={MeanR:0.00} rms-osc={SdR:0.000} min={minR:0.00} max={maxR:0.00} p2p={maxR - minR:0.00}"
                     + $" | slope-rel pitch rms={SdRelP:0.000} [{minRelP:0.00},{maxRelP:0.00}] roll rms={SdRelR:0.000} [{minRelR:0.00},{maxRelR:0.00}]"
                     + $" | maxPitchRate={maxAbsPitchRate:0.0}d/s maxRollRate={maxAbsRollRate:0.0}d/s maxYawRate={maxYawRate:0.0}d/s | vy rms={RmsVy:0.000} max|vy|={maxAbsVy:0.00} vyFlips={vySignFlips} yRange={maxY - minY:0.000}m"
                     + $" | speed min={minSpeed:0.00} max={maxSpeed:0.00} | wheels minGrounded={minGrounded:0} liftSamples={liftSamples}/{n}" + (firstLift.Length > 0 ? $" first={firstLift}" : "") + (endedSleeping ? " | asleep at end" : "") + (contacts.Length > 0 ? " | body contacts: " + contacts : "");
            }
        }

        RoverVehicle _rover; Rigidbody _rb; Transform _rig;
        double _next; float _nextSummary;
        Stats _phase, _all = new Stats { name = "all" };
        public string PhaseName => _phase != null ? _phase.name : "";
        readonly List<Stats> _done = new List<Stats>();
        public IReadOnlyList<Stats> Completed => _done;
        string[] _label;
        static StreamWriter _w; static bool _headerDone; static int _rows;
        float _pitch, _roll, _speed, _vy; int _grounded; string _hud = "";
        public static string CsvPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "xenoarch_rover_inclinometer.csv");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = null; ForceEnabled = false; ShowHud = false; _w = null; _headerDone = false; _rows = 0; }

        void Awake() { _rover = GetComponent<RoverVehicle>(); Active = this; }
        void OnDestroy() { if (Active == this) Active = null; if (_w != null) { try { _w.Flush(); } catch { } } }

        public void BeginPhase(string name)
        {
            if (_phase != null) EndPhase();
            _phase = new Stats { name = name, startT = Time.time };
            Debug.Log($"[RoverDiag] begin phase {name}");
        }

        public Stats EndPhase()
        {
            if (_phase == null) return null;
            var s = _phase; _phase = null;
            s.duration = Time.time - s.startT;
            s.endedSleeping = _rb != null && _rb.IsSleeping();
            _done.Add(s);
            s.contacts = ContactsSummary(); ClearContacts();
            Debug.Log("[RoverDiag] " + s.Line());
            return s;
        }

        readonly Dictionary<string, int> _contacts = new Dictionary<string, int>();
        public string ContactsSummary()
        {
            var sb = new StringBuilder();
            foreach (var kv in _contacts) sb.Append(kv.Key).Append('x').Append(kv.Value).Append(' ');
            return sb.Length > 0 ? sb.ToString() : "none";
        }
        public void ClearContacts() => _contacts.Clear();

        void OnCollisionStay(Collision c)
        {
            // which of the rover's own colliders touch something (wheel colliders do not report here; this is hull / body / chassis contact)
            for (int i = 0; i < c.contactCount && i < 4; i++)
            {
                var cp = c.GetContact(i);
                string k = (cp.thisCollider != null ? cp.thisCollider.name : "?") + ">" + (cp.otherCollider != null ? cp.otherCollider.name : "?");
                _contacts.TryGetValue(k, out int n); _contacts[k] = n + 1;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[Key.F10].wasPressedThisFrame) ShowHud = !ShowHud;
        }

        bool Bind()
        {
            if (_rover == null || !_rover.Live) return false;
            _rb = _rover.DiagBody; _rig = _rover.DiagRig;
            if (_rb == null || _rig == null || _rover.wheels == null || _rover.wheels.Count == 0) return false;
            _label = new string[_rover.wheels.Count];
            for (int i = 0; i < _label.Length; i++)
            {
                var w = _rover.wheels[i];
                if (w == null || w.collider == null) { _label[i] = "W" + i; continue; }
                var lp = _rig.InverseTransformPoint(w.collider.transform.position);
                _label[i] = (lp.z >= 0f ? "F" : "R") + (lp.x >= 0f ? "R" : "L");
            }
            return true;
        }

        void OpenCsv()
        {
            if (_w != null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CsvPath));
                _w = new StreamWriter(CsvPath, false, new UTF8Encoding(false));
                _headerDone = false; _rows = 0;
            }
            catch (System.Exception e) { Debug.LogWarning("[RoverDiag] CSV open failed: " + e.Message); }
        }

        void FixedUpdate()
        {
            if (_rb == null && !Bind()) return;
            double now = Time.fixedTimeAsDouble;
            if (now < _next) return;
            _next = now + 1.0 / Mathf.Max(1f, hz);
            Sample();
        }

        void Sample()
        {
            var fwd = _rig.forward; var right = _rig.right;
            var flatF = fwd; flatF.y = 0f; if (flatF.sqrMagnitude < 1e-4f) flatF = Vector3.forward; flatF.Normalize();
            var flatR = right; flatR.y = 0f; if (flatR.sqrMagnitude < 1e-4f) flatR = Vector3.right; flatR.Normalize();
            float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;      // + nose up
            float roll = Mathf.Asin(Mathf.Clamp(right.y, -1f, 1f)) * Mathf.Rad2Deg;     // + right side up
            // ground slope under the body, along the heading (+ = ground rises ahead / to the right)
            float slopeP = 0f, slopeR = 0f;
            var origin = _rb.worldCenterOfMass + Vector3.up * 1.0f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Vector3 nrm = Vector3.up;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(_rover.transform) || h.collider.isTrigger) continue;
                if (h.distance < best) { best = h.distance; nrm = h.normal; }
            }
            if (nrm.y > 0.05f)
            {
                slopeP = Mathf.Atan(-Vector3.Dot(nrm, flatF) / nrm.y) * Mathf.Rad2Deg;
                slopeR = Mathf.Atan(-Vector3.Dot(nrm, flatR) / nrm.y) * Mathf.Rad2Deg;   // + right side up on a slope that rises to the right
            }
            var av = _rb.angularVelocity;
            float pitchRate = -Vector3.Dot(av, right) * Mathf.Rad2Deg;
            float rollRate = Vector3.Dot(av, fwd) * Mathf.Rad2Deg;
            float yawRate = Vector3.Dot(av, Vector3.up) * Mathf.Rad2Deg;
            var vel = _rb.linearVelocity;
            float speed = Vector3.Dot(vel, flatF);
            int grounded = 0; var lifted = "";
            string wheelCols = "";
            for (int i = 0; i < _rover.wheels.Count; i++)
            {
                var w = _rover.wheels[i];
                if (w == null || w.collider == null) { wheelCols += ",0,0,0"; continue; }
                var wc = w.collider;
                bool g = wc.GetGroundHit(out var hit);
                if (g) grounded++; else lifted += _label[i];
                wc.GetWorldPose(out var wp, out _);
                float sc = Mathf.Max(0.0001f, wc.transform.lossyScale.y);
                float maxExt = Mathf.Max(0.0001f, wc.suspensionDistance * sc);
                float ext = Vector3.Dot(wc.transform.position - wp, wc.transform.up);
                float comp = 1f - Mathf.Clamp(ext / maxExt, -0.2f, 1.2f);
                wheelCols += $",{(g ? 1 : 0)},{comp:0.000},{(g ? hit.force : 0f):0}";
            }
            float t = Time.time;
            string ph = _phase != null ? _phase.name : "";
            _phase?.Add(t, pitch, roll, pitch - slopeP, roll - slopeR, pitchRate, rollRate, yawRate, vel.y, speed, grounded, _rb.position.y, lifted);
            _all.Add(t, pitch, roll, pitch - slopeP, roll - slopeR, pitchRate, rollRate, yawRate, vel.y, speed, grounded, _rb.position.y, lifted);
            _pitch = pitch; _roll = roll; _speed = speed; _vy = vel.y; _grounded = grounded;
            _hud = $"pitch {pitch:+0.0;-0.0} (slope {slopeP:+0.0;-0.0})  roll {roll:+0.0;-0.0} (slope {slopeR:+0.0;-0.0})\nspeed {speed:0.0} m/s  vy {vel.y:+0.00;-0.00}  wheels {grounded}/{_rover.wheels.Count} {(lifted.Length > 0 ? "LIFT " + lifted : "")}\npitchRate {pitchRate:+0;-0} d/s  phase {ph}";

            OpenCsv();
            if (_w != null)
            {
                try
                {
                    if (!_headerDone)
                    {
                        var h = new StringBuilder("t,phase,state,speed,vy,posY,pitch,roll,slopePitch,slopeRoll,pitchRate,rollRate,yawRate,grounded,throttle,steer");
                        for (int i = 0; i < _label.Length; i++) h.Append($",{_label[i]}_g,{_label[i]}_comp,{_label[i]}_N");
                        _w.WriteLine(h.ToString()); _headerDone = true;
                    }
                    string st = _rover.DiagDriving ? "drive" : _rover.DiagParked ? "park" : "other";
                    _w.WriteLine($"{t:0.000},{ph},{st},{speed:0.000},{vel.y:0.000},{_rb.position.y:0.000},{pitch:0.000},{roll:0.000},{slopeP:0.000},{slopeR:0.000},{pitchRate:0.00},{rollRate:0.00},{yawRate:0.00},{grounded},{(RoverDevInput.Active ? RoverDevInput.Throttle : 0f):0.0},{(RoverDevInput.Active ? RoverDevInput.Steer : 0f):0.0}{wheelCols}");
                    if (++_rows % 20 == 0) _w.Flush();
                }
                catch { }
            }
            if (t >= _nextSummary)
            {
                _nextSummary = t + 2f;
                Debug.Log($"[RoverDiag] t={t:0.0} phase={ph} pitch={pitch:0.00} roll={roll:0.00} pitchRate={pitchRate:0.0} vy={vel.y:0.00} speed={speed:0.00} wheels={grounded}/{_rover.wheels.Count} {lifted}");
            }
        }

        void OnGUI()
        {
            if (!ShowHud || _rover == null) return;
            GUI.color = Color.white;
            GUI.Box(new Rect(12, 140, 360, 64), "Rover inclinometer (debug, F10)");
            GUI.Label(new Rect(18, 158, 350, 48), _hud);
        }
    }
}
#endif

