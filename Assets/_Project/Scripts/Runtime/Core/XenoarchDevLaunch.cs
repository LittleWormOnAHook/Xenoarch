#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Scenes;
using DMI.Xenoarch.Vehicles;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Core
{
    /// <summary>
    /// DEV / TESTING (editor only, default off): skips the main menu, the ship and the shuttle and drops a fresh Test All
    /// session straight on a planet/moon surface, then calls the rover through the normal Emergency Rover Call path.
    /// Armed only by the editor menu "Xenoarch/Dev/..." or Temp/xenoarch_devlaunch.trigger (see XenoarchDevLaunchMenu),
    /// which set the SessionState key <see cref="Key"/> to "planet[:bodyId]" or "rovertest[:bodyId]".
    /// "planet" stops there so you can walk/drive yourself. "rovertest" also lets the rover rest, boards it through the
    /// normal Enter path and drives scripted input (RoverDevInput), logging with RoverInclinometer to
    /// Logs/xenoarch_rover_inclinometer.csv and a report to Logs/xenoarch_rover_test.txt, then stops Play.
    /// Saves are suppressed for the session (XenoarchRuntime.SuppressSaves). Normal play never reaches this code.
    /// </summary>
    public sealed class XenoarchDevLaunch : MonoBehaviour
    {
        public const string Key = "Xenoarch.DevLaunch";
        public const string ReportPath = "Logs/xenoarch_rover_test.txt";
        readonly StringBuilder _report = new StringBuilder();
        RoverVehicle _rover; RoverInclinometer _inc; bool _abort; string _variant = ""; float _wheelMass; bool _resetWheels;

        /// <summary>Optional "field=value,field=value" overrides of RoverPhysicsProfile floats, applied to a throw-away clone
        /// (never the asset) for the rover deployed by this run, so a tuning idea can be A/B tested without touching assets.</summary>
        void ApplyVariant()
        {
            if (string.IsNullOrEmpty(_variant)) return;
            var clone = Instantiate(RoverPhysicsProfile.LoadOrDefault());
            clone.hideFlags = HideFlags.HideAndDontSave;
            foreach (var kv in _variant.Split(','))
            {
                var parts = kv.Split('=');
                if (parts.Length != 2) continue;
                if (parts[0].Trim() == "wheelMass") { float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _wheelMass); Say("variant: wheelMass -> " + _wheelMass); continue; }
                if (parts[0].Trim() == "resetWheels") { _resetWheels = parts[1].Trim() == "1"; Say("variant: resetWheels -> " + _resetWheels); continue; }
                var fi = typeof(RoverPhysicsProfile).GetField(parts[0].Trim());
                if (fi == null || fi.FieldType != typeof(float) || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) { Say("variant: ignored '" + kv + "'"); continue; }
                Say($"variant: {parts[0].Trim()} {fi.GetValue(clone)} -> {v}");
                fi.SetValue(clone, v);
            }
            RoverVehicle.DiagProfileOverride = clone;
        }

        void ApplyWheelVariants()
        {
            foreach (var w in _rover.wheels)
            {
                if (w == null || w.collider == null) continue;
                if (_wheelMass > 0f) w.collider.mass = _wheelMass;
                if (_resetWheels) { w.collider.enabled = false; w.collider.enabled = true; }
            }
            if (_resetWheels) { _rover.DiagBody.linearVelocity = Vector3.zero; _rover.DiagBody.angularVelocity = Vector3.zero; }
        }

        float GroundY(Vector3 p)
        {
            float y = float.NaN;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(_rover.transform)) continue;
                if (float.IsNaN(y) || h.point.y > y && h.point.y < p.y + 29f) y = Mathf.Max(float.IsNaN(y) ? -1e9f : y, h.point.y);
            }
            return y;
        }

        /// <summary>Immediately after the deploy (no physics step yet): which rover colliders already overlap other colliders?</summary>
        void OverlapReport()
        {
            int reported = 0, total = 0;
            var seen = new HashSet<string>();
            foreach (var c in _rover.GetComponentsInChildren<Collider>(false))
            {
                if (c is WheelCollider || c.isTrigger || !c.enabled) continue;
                var b = c.bounds;
                foreach (var o in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (o == null || o.transform.IsChildOf(_rover.transform)) continue;
                    total++;
                    bool pen = false; float dist = 0f; Vector3 dir = Vector3.zero;
                    try { pen = Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, o, o.transform.position, o.transform.rotation, out dir, out dist); } catch { }
                    string key = c.name + ">" + o.name;
                    if (pen && seen.Add(key) && reported++ < 20) Say($"OVERLAP rover '{c.name}' ({c.GetType().Name}) penetrates '{o.name}' ({o.GetType().Name}, root '{o.transform.root.name}') depth={dist:0.000} dir={dir:F2}");
                }
            }
            Say($"overlap check at deploy: aabb overlaps={total}, true penetrations reported={reported}");
        }
        void Geometry(string tag)
        {
            var rig = _rover.DiagRig; var rb = _rover.DiagBody;
            Say($"--- geometry [{tag}] root={_rover.transform.position:F2} rootEuler={_rover.transform.eulerAngles:F0} rigEuler={rig.eulerAngles:F0} rigScale={rig.lossyScale.x:0.00} groundUnderRoot={GroundY(_rover.transform.position):0.00}");
            Say($"COM rig-local={rig.InverseTransformPoint(rb.worldCenterOfMass):F2} inertia={rb.inertiaTensor:F0} mass={rb.mass:0} sleeping={rb.IsSleeping()}");
            float lowestWheel = 1e9f;
            int i = 0;
            foreach (var w in _rover.wheels)
            {
                if (w == null || w.collider == null) { i++; continue; }
                var wc = w.collider; var vis = w.visual;
                var rend = vis != null ? vis.GetComponentInChildren<Renderer>() : null;
                Vector3 bc = rend != null ? rend.bounds.center : Vector3.zero, be = rend != null ? rend.bounds.extents : Vector3.zero;
                float bottom = wc.transform.position.y - wc.radius * wc.transform.lossyScale.y - 0f;
                float g = GroundY(wc.transform.position);
                lowestWheel = Mathf.Min(lowestWheel, bottom);
                Say($"wheel[{i}] front={w.front} colliderRigLocal={rig.InverseTransformPoint(wc.transform.position):F2} radius={wc.radius:0.000} bottomAtExtend0={bottom:0.00} groundBelow={g:0.00} | visual='{(vis != null ? vis.name : "null")}' visPos={(vis != null ? vis.position : Vector3.zero):F2} rendererFirst='{(rend != null ? rend.name : "none")}' boundsCenter={bc:F2} extents={be:F2}");
                i++;
            }
            Say($"lowest wheel-collider bottom (suspension at attachment height) y={lowestWheel:0.00}");
            foreach (var w in _rover.wheels)
            {
                if (w == null || w.collider == null) continue;
                var wc = w.collider;
                bool g = wc.GetGroundHit(out var hit);
                wc.GetWorldPose(out var wp, out _);
                var ls = wc.transform.lossyScale;
                Say($"wheelhit front={w.front} grounded={g} lossyScale={ls:F3} localScale={wc.transform.localScale:F3} centerY={wp.y:0.00} attachY={wc.transform.position.y:0.00} hitCollider='{(g && hit.collider != null ? hit.collider.name + "/" + hit.collider.GetType().Name : "-")}' hitY={(g ? hit.point.y : float.NaN):0.00} centerMinusHit={(g ? wp.y - hit.point.y : float.NaN):0.00} force={(g ? hit.force : 0f):0}");
            }
            if (_rover.chassis != null) { var cb = _rover.chassis.bounds; Say($"chassis box enabled={_rover.chassis.enabled} minY={cb.min.y:0.00} maxY={cb.max.y:0.00} size={cb.size:F2}"); }
            var cols = new List<Collider>();
            foreach (var c in _rover.GetComponentsInChildren<Collider>(false)) if (!(c is WheelCollider) && !c.isTrigger && c.enabled) cols.Add(c);
            cols.Sort((x, y) => x.bounds.min.y.CompareTo(y.bounds.min.y));
            for (int k = 0; k < Mathf.Min(5, cols.Count); k++) { var c = cols[k]; var bb = c.bounds; Say($"lowest collider #{k} '{c.name}' {c.GetType().Name} minY={bb.min.y:0.00} size={bb.size:F2}"); }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            string cfg = UnityEditor.SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(cfg)) return;
            UnityEditor.SessionState.SetString(Key, "");   // one shot
            var go = new GameObject("[XenoarchDevLaunch]");
            DontDestroyOnLoad(go);
            var d = go.AddComponent<XenoarchDevLaunch>();
            string variant = "";
            int bar = cfg.IndexOf('|'); if (bar >= 0) { variant = cfg.Substring(bar + 1); cfg = cfg.Substring(0, bar); }
            string mode = cfg, body = "";
            int i = cfg.IndexOf(':'); if (i >= 0) { mode = cfg.Substring(0, i); body = cfg.Substring(i + 1); }
            d._variant = variant;
            d.StartCoroutine(d.Run(mode, body));
        }
        void Say(string s)
        {
            Debug.Log("[DevLaunch] " + s);
            _report.AppendLine(System.DateTime.Now.ToString("HH:mm:ss") + " " + s);
        }

        void Flush()
        {
            try { Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, _report.ToString()); } catch { }
        }

        void Stop(string why)
        {
            Say("STOP: " + why);
            RoverDevInput.Clear();
            RoverVehicle.DiagProfileOverride = null;
            Flush();
            try { File.WriteAllText("Temp/xenoarch_stopplay.trigger", "dev launch done"); } catch { UnityEditor.EditorApplication.isPlaying = false; }
        }

        IEnumerator Run(string mode, string bodyId)
        {
            bool test = mode == "rovertest";
            Say($"mode={mode} body='{bodyId}' variant='{_variant}'");
            // 1. runtime up, menu showing (nothing is clicked: we call the game flow directly)
            float t0 = Time.realtimeSinceStartup;
            while ((XenoarchRuntime.Instance == null || XenoarchRuntime.Session == null || XenoarchRuntime.Scenes == null || SceneLoader.Busy
                    || SceneManager.GetActiveScene().name != SceneNames.MainMenu) && Time.realtimeSinceStartup - t0 < 90f)
                yield return null;
            var rt = XenoarchRuntime.Instance;
            if (rt == null) { Stop("no XenoarchRuntime"); yield break; }
            RoverInclinometer.ForceEnabled = true;

            // 2. straight to the surface
            if (!rt.DevStartOnPlanet(bodyId, out var info)) { Stop("DevStartOnPlanet failed: " + info); yield break; }
            Say("start: " + info);
            string scene = XenoarchRuntime.Session.Location.sceneName;
            t0 = Time.realtimeSinceStartup;
            while ((SceneManager.GetActiveScene().name != scene || SceneLoader.Busy || FindAnyObjectByType<FirstPersonController>() == null) && Time.realtimeSinceStartup - t0 < 300f)
                yield return null;
            if (SceneManager.GetActiveScene().name != scene || SceneLoader.Busy) { Stop("surface scene did not finish loading in 300 s"); yield break; }
            yield return new WaitForSeconds(2f);
            var terr = Terrain.activeTerrain;
            Say($"on surface '{scene}' after {Time.realtimeSinceStartup - t0:0.0}s; terrain={(terr != null ? terr.name + " size " + terr.terrainData.size : "none (non-Terrain ground)")}");

            // 3. call the rover (normal emergency call path); retry facing other ways if there is no flat ground ahead
            var dir = RoverDirector.Instance;
            var player = FindAnyObjectByType<FirstPersonController>();
            if (dir == null || player == null) { Stop("no RoverDirector/player"); yield break; }
            ApplyVariant();
            string msg = null; bool called = false;
            for (int i = 0; i < 12 && !called; i++)
            {
                called = dir.CallEmergency(out msg);
                Say($"CallEmergency #{i + 1}: {called} '{msg}'");
                if (!called) { player.transform.Rotate(0f, 30f, 0f); yield return new WaitForSeconds(0.5f); }
            }
            if (!called || dir.Current == null) { Stop("rover call failed: " + msg); yield break; }
            _rover = dir.Current;
            ApplyWheelVariants();
            OverlapReport();
            Say($"rover deployed at {_rover.transform.position:F2} player at {player.transform.position:F2}");
            if (!test) { Say("planet mode: done; the rover is next to you. Inclinometer CSV: Logs/xenoarch_rover_inclinometer.csv (F10 = debug readout)"); Flush(); yield break; }

            // 4. rover test
            yield return null;
            _inc = _rover.GetComponent<RoverInclinometer>();
            if (_inc == null) { Stop("inclinometer was not attached"); yield break; }
            DescribeSetup(scene);
            Geometry("at deploy (+1 frame)");
            yield return Phase("rest10", 10f, 0f, 0f, false, null);
            Geometry("after rest10");
            Say($"rest: rb sleeping={_rover.DiagBody.IsSleeping()}");

            // board through the normal Enter path
            _inc.BeginPhase("enter");
            bool entered = _rover.Enter(player);
            Say($"Enter(player) -> {entered}");
            t0 = Time.time;
            while (entered && !_rover.DiagDriving && Time.time - t0 < 45f) yield return null;
            _inc.EndPhase();
            if (!_rover.DiagDriving) { Say("could not board / power on the rover within 45 s; skipping the drive part"); Finish(); yield break; }
            Say($"boarded and powered after {Time.time - t0:0.0}s");

            RoverDevInput.Active = true;
            yield return Phase("seated_idle", 3f, 0f, 0f, false, null);
            yield return Phase("fwd5", 5f, 1f, 0f, false, null);
            yield return Phase("brake_from_speed", 8f, -1f, 0f, false, () => Mathf.Abs(Speed()) < 0.4f);
            yield return Phase("hold", 1.5f, 0f, 0f, true, null);
            Geometry("after brake hold");
            yield return Phase("rev3", 3f, -1f, 0f, false, null);
            yield return Phase("brake_from_reverse", 6f, 1f, 0f, false, () => Mathf.Abs(Speed()) < 0.4f);
            yield return Phase("hold2", 1.5f, 0f, 0f, true, null);
            yield return Phase("turn_right", 5f, 1f, 1f, false, null);
            yield return Phase("turn_left", 4f, 1f, -1f, false, null);
            yield return Phase("stop", 8f, -1f, 0f, false, () => Mathf.Abs(Speed()) < 0.4f);
            yield return Phase("park", 2f, 0f, 0f, true, null);
            Finish();
        }

        float Speed()
        {
            var rb = _rover.DiagBody; var f = _rover.DiagRig.forward; f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? Vector3.Dot(rb.linearVelocity, f.normalized) : 0f;
        }

        IEnumerator Phase(string name, float seconds, float throttle, float steer, bool hand, System.Func<bool> until)
        {
            if (_abort) yield break;
            _inc.BeginPhase(name);
            float t = 0f;
            while (t < seconds)
            {
                if (RoverDevInput.Active) { RoverDevInput.Throttle = throttle; RoverDevInput.Steer = steer; RoverDevInput.Hand = hand; }
                if (t > 0.5f && until != null && until()) break;
                if (_rover == null || _rover.transform.up.y < 0.25f) { _abort = true; Say($"ABORT in {name}: rover flipped/destroyed (up.y={(_rover != null ? _rover.transform.up.y : 0f):0.00})"); break; }
                t += Time.deltaTime; yield return null;
            }
            _inc.EndPhase();
            if (_abort) Finish();
        }
        void DescribeSetup(string scene)
        {
            var p = RoverPhysicsProfile.LoadOrDefault();
            var env = FindAnyObjectByType<EnvironmentDirector>();
            Say($"scene={scene} gravity={(env != null && env.Current != null ? env.Current.gravity : -1f):0.00} fixedDt={Time.fixedDeltaTime:0.000} rootScale={_rover.transform.localScale.x:0.00} mass={_rover.DiagBody.mass:0}");
            if (p != null)
                Say($"profile: spring={p.spring:0} damper={p.damper:0} travel={p.travel:0.000} targetPosition={p.targetPosition:0.00} antiPitch={p.antiPitch:0.0} antiSquat={p.antiSquat:0.0} antiDive={p.antiDive:0.0} "
                    + $"airborneDamping={p.airborneDamping:0.00} maxAirUpSpeed={p.maxAirUpSpeed:0.00} comDown={p.comDown:0.00} pitchInertia={p.pitchInertia:0.00} angularDamping={p.angularDamping:0.00} gravityMul={p.gravityMultiplier:0.00} extraDownforceG={p.extraDownforceG:0.00}");
            int i = 0;
            foreach (var w in _rover.wheels)
            {
                if (w == null || w.collider == null) continue;
                var s = w.collider.suspensionSpring;
                Say($"wheel[{i++}] front={w.front} radius={w.collider.radius:0.000} susp={w.collider.suspensionDistance:0.000} spring={s.spring:0} damper={s.damper:0} target={s.targetPosition:0.00} scale={w.collider.transform.lossyScale.y:0.00} mass={w.collider.mass:0}");
            }
        }

        void Finish()
        {
            RoverDevInput.Clear();
            Say("=== PHASE SUMMARY (pitch/roll deg, + = nose up / right side up) ===");
            if (_inc != null) foreach (var s in _inc.Completed) Say(s.Line());
            Stop("rover test complete");
        }
    }
}
#endif
