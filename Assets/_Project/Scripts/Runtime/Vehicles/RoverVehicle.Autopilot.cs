using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// Emergency Rover Call autopilot (t251u): drives a deployed, unoccupied rover to the player through
    /// RoverVehicle physics, then parks about 6 m away facing them. Player entry cancels it.
    /// </summary>
    public sealed partial class RoverVehicle
    {
        public const float AutoParkDistance = 6f;
        public const float AutoMaxDistance = 600f;
        const float AutoSafeSpeed = 5f;
        const float AutoAvoidRange = 6f;
        const float AutoSlopeY = 0.78f; // ~38 degrees
        const float AutoStuckSeconds = 2.2f;
        const float AutoFailStuck = 8f;
        const float AutoReverseSeconds = 0.85f;

        bool _autoPilot;
        float _autoStuck;
        float _autoStuckTotal;
        float _autoReverseUntil;
        float _autoBestDist = float.MaxValue;
        float _autoNoProgress;
        int _autoAvoidSign = 1;

        public bool AutopilotActive => _autoPilot;

        /// <summary>Wake the rover and start driving to a point ~6 m from the player. State stays Parked so the player can board and cancel.</summary>
        public bool StartAutopilot(out string why)
        {
            why = null;
            if (!Live || _rb == null) { why = "The rover is not ready."; return false; }
            if (_state != State.Parked) { why = "The rover is occupied."; return false; }
            var player = FindAnyObjectByType<FirstPersonController>();
            if (player == null) { why = "Can't find your position."; return false; }
            float dist = PlanarDistance(player.transform.position);
            if (dist > AutoMaxDistance) { why = $"Rover is {dist:0} m away (max {AutoMaxDistance:0} m)."; return false; }
            if (dist <= AutoParkDistance + 1f)
            {
                FaceToward(player.transform.position);
                why = $"Rover is already nearby ({dist:0} m).";
                return false;
            }
            _autoPilot = true;
            _autoStuck = 0f;
            _autoStuckTotal = 0f;
            _autoReverseUntil = 0f;
            _autoBestDist = dist;
            _autoNoProgress = 0f;
            _autoAvoidSign = Random.value < 0.5f ? -1 : 1;
            StartDrivingPhysics();
            ApplyPresence(true);
            return true;
        }

        public void CancelAutopilot(bool park)
        {
            if (!_autoPilot) return;
            _autoPilot = false;
            _autoReverseUntil = 0f;
            _throttleSmooth = 0f;
            if (park && _state == State.Parked)
            {
                StartParkedPhysics(false);
                ApplyPresence(false);
            }
        }

        void TickAutopilot()
        {
            var player = FindAnyObjectByType<FirstPersonController>();
            if (player == null) { FailAutopilot("Lost the player position."); return; }
            if (player.Seated) { CancelAutopilot(true); return; }

            var dir = RoverDirector.Instance;
            if (dir != null && dir.Fuel <= 0.01f) { FailAutopilot("Rover ran out of fuel."); return; }

            Vector3 playerPos = player.transform.position;
            float dist = PlanarDistance(playerPos);
            if (dist > AutoMaxDistance) { FailAutopilot($"Rover is too far away ({dist:0} m)."); return; }

            Vector3 toPlayer = playerPos - transform.position; toPlayer.y = 0f;
            Vector3 target = playerPos;
            if (toPlayer.sqrMagnitude > 0.01f) target = playerPos - toPlayer.normalized * AutoParkDistance;
            float toTarget = PlanarDistance(target);
            var planarVel = _rb.linearVelocity; planarVel.y = 0f;
            float speed = planarVel.magnitude;

            if (dist <= AutoParkDistance + 0.75f && speed < 0.6f)
            {
                FaceToward(playerPos);
                CancelAutopilot(true);
                Toast.Show($"Rover arrived, {dist:0} m away. Rear door: E to open, E to enter.", 4f);
                return;
            }

            if (dist + 0.75f < _autoBestDist)
            {
                _autoBestDist = dist;
                _autoNoProgress = 0f;
            }
            else
            {
                _autoNoProgress += Time.fixedDeltaTime;
                if (_autoNoProgress > AutoFailStuck) { FailAutopilot("Couldn't find a way through."); return; }
            }

            float throttle, steer; bool hand = false;
            bool reversing = Time.time < _autoReverseUntil;
            float avoid = AvoidSteer(out bool blocked);
            Vector3 toT = target - transform.position; toT.y = 0f;
            float ang = 0f;
            if (toT.sqrMagnitude > 0.05f)
            {
                var want = toT.normalized;
                ang = Vector3.SignedAngle(Fwd, want, Vector3.up);
            }
            float turn = Mathf.Clamp(ang / 35f, -1f, 1f);

            if (reversing)
            {
                throttle = -0.45f;
                steer = _autoAvoidSign * 0.8f;
            }
            else
            {
                steer = Mathf.Clamp(turn + avoid, -1f, 1f);
                float aim = 1f - Mathf.Clamp01(Mathf.Abs(ang) / 90f);
                if (toTarget < 8f) throttle = Mathf.Lerp(0.15f, 0.35f, toTarget / 8f) * Mathf.Max(0.25f, aim);
                else if (toTarget < 25f) throttle = Mathf.Lerp(0.35f, 0.55f, (toTarget - 8f) / 17f) * Mathf.Max(0.4f, aim);
                else throttle = 0.55f * Mathf.Max(0.35f, aim);
                if (blocked && Mathf.Abs(avoid) > 0.4f) throttle = Mathf.Min(throttle, 0.3f);
                if (toTarget < 3f && speed > 1.2f) { throttle = 0f; hand = true; }
            }

            if (!reversing && (blocked || speed < 0.35f) && throttle > 0.15f)
            {
                _autoStuck += Time.fixedDeltaTime;
                _autoStuckTotal += Time.fixedDeltaTime;
                if (_autoStuckTotal > AutoFailStuck) { FailAutopilot("Rover is stuck."); return; }
                if (_autoStuck > AutoStuckSeconds)
                {
                    _autoAvoidSign = -_autoAvoidSign;
                    _autoReverseUntil = Time.time + AutoReverseSeconds;
                    _autoStuck = 0f;
                }
            }
            else if (!reversing) _autoStuck = 0f;

            ApplyDriveInput(throttle, steer, hand, false, AutoSafeSpeed);
        }

        void FailAutopilot(string reason)
        {
            CancelAutopilot(true);
            if (RoverDirector.Instance != null) RoverDirector.Instance.OnAutopilotFailed(this, reason);
            else Toast.Show("Couldn't reach you: " + reason, 4f);
        }

        float AvoidSteer(out bool blocked)
        {
            blocked = false;
            Vector3 origin = transform.position + Vector3.up * 0.9f + Fwd * 1.2f;
            float left = Probe(origin, Quaternion.Euler(0f, -28f, 0f) * Fwd);
            float center = Probe(origin, Fwd);
            float right = Probe(origin, Quaternion.Euler(0f, 28f, 0f) * Fwd);
            if (SlopeBlocked(Fwd * 4.5f)) { center = Mathf.Min(center, 1.5f); blocked = true; }
            const float close = 3.2f;
            if (center < close || left < close * 0.85f || right < close * 0.85f) blocked = true;
            float steer = 0f;
            if (left < right) steer = Mathf.Clamp((close - left) / close, 0f, 1f);
            else steer = -Mathf.Clamp((close - right) / close, 0f, 1f);
            if (center < close) steer += (steer >= 0f ? 1f : -1f) * Mathf.Clamp((close - center) / close, 0.3f, 1f);
            if (steer == 0f && blocked) steer = _autoAvoidSign;
            return Mathf.Clamp(steer, -1f, 1f);
        }

        float Probe(Vector3 origin, Vector3 dir)
        {
            var hits = Physics.RaycastAll(origin, dir, AutoAvoidRange, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                var player = FindAnyObjectByType<FirstPersonController>();
                if (player != null && h.collider.transform.IsChildOf(player.transform)) continue;
                if (h.collider is TerrainCollider && h.normal.y >= AutoSlopeY) continue;
                if (h.normal.y >= AutoSlopeY && Vector3.Angle(h.normal, Vector3.up) < 25f) continue;
                return h.distance;
            }
            return AutoAvoidRange;
        }

        bool SlopeBlocked(Vector3 ahead)
        {
            var p = transform.position + ahead + Vector3.up * 4f;
            var hits = Physics.RaycastAll(p, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
                return h.normal.y < AutoSlopeY;
            }
            return true; // no ground ahead
        }

        float PlanarDistance(Vector3 world)
        {
            var d = world - transform.position; d.y = 0f;
            return d.magnitude;
        }

        void FaceToward(Vector3 world)
        {
            var dir = world - transform.position; dir.y = 0f;
            if (dir.sqrMagnitude < 0.05f) return;
            transform.rotation = Quaternion.FromToRotation(Fwd, dir.normalized) * transform.rotation;
            var e = transform.eulerAngles;
            transform.rotation = Quaternion.Euler(0f, e.y, 0f);
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
