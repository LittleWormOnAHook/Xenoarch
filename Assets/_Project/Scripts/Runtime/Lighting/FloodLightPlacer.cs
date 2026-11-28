using DMI.Xenoarch.Core;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Scenes;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace DMI.Xenoarch.Lighting
{
    /// <summary>
    /// Flood light placement mode. A translucent ghost of the stand follows the ground under the crosshair (raycast, aligned to
    /// the surface normal; teal = can place, red = too steep). Mouse wheel turns its yaw, left-click places it, right-click or
    /// Esc cancels. While active, <see cref="Active"/> is true so other wheel users (sonic tool dials) leave the wheel alone.
    /// Tunables (range, max slope, yaw step) live on <see cref="PlayerLighting"/>. No UI is built: only the ghost and a toast.
    /// </summary>
    public sealed class FloodLightPlacer : MonoBehaviour
    {
        public static bool Active { get; private set; }
        static FloodLightPlacer _inst;

        GameObject _ghost;
        Material _mat;
        float _yaw, _startedAt;
        bool _lastValid;
        bool _validKnown;
        IPlayerRig _rig;

        /// <summary>Starts placement. False (with a reason) if the player has no flood light or can't place now.</summary>
        public static bool Begin(out string why)
        {
            why = null;
            var s = XenoarchRuntime.Session;
            if (s == null) { why = "No game session."; return false; }
            if (s.Inventory.Count(PlayerLighting.FloodLightId) <= 0)
            {
                why = DigTool.NotOwnedText("flood light");
                return false;
            }
            var rig = PlayerRigLocator.Find();
            if (rig == null || rig.Root == null || rig.ViewCamera == null) { why = "No player."; return false; }
            if (rig is IPlayerSeat seat && seat.Seated) { why = "Stand up to place a flood light."; return false; }

            var pl = PlayerLighting.Current ?? PlayerLighting.Ensure(rig.Root);
            if (pl == null) { why = "Can't place a flood light here."; return false; }
            _inst = pl.GetComponent<FloodLightPlacer>() ?? pl.gameObject.AddComponent<FloodLightPlacer>();
            pl.HolsterTools();
            _inst._rig = rig;
            _inst._yaw = rig.ViewCamera.transform.eulerAngles.y;
            _inst._startedAt = Time.unscaledTime;
            _inst._validKnown = false;
            Active = true;
            Toast.Show("Placing flood light: mouse wheel rotates, left-click places, right-click or Esc cancels.", 4f);
            return true;
        }

        public static void Cancel()
        {
            Active = false;
            if (_inst != null) _inst.DestroyGhost();
        }

        /// <summary>Esc while placing cancels the placement (and is not passed on to the pause menu).</summary>
        public static bool TryHandleEscape()
        {
            if (!Active || UIState.BlocksGameplay) return false;
            Cancel();
            Toast.Show("Placement cancelled.", 2f);
            return true;
        }

        void Awake() { _inst = this; }

        void OnDisable() { if (Active && _inst == this) Active = false; DestroyGhost(); }

        void OnDestroy() { if (_inst == this) { Active = false; _inst = null; } DestroyGhost(); }

        void Update()
        {
            if (!Active || _inst != this) return;
            var s = XenoarchRuntime.Session;
            if (s == null || s.Inventory.Count(PlayerLighting.FloodLightId) <= 0) { Cancel(); return; }
            if (_rig == null) _rig = PlayerRigLocator.Find();
            var cam = _rig != null && _rig.ViewCamera != null ? _rig.ViewCamera.transform : null;
            if (cam == null || (_rig is IPlayerSeat seat && seat.Seated)) { Cancel(); return; }
            var pl = PlayerLighting.Current;
            if (pl != null && pl.AnyToolOut) { Cancel(); return; }

            bool suspended = UIState.BlocksGameplay || SceneLoader.Busy || Time.timeScale == 0f || !_rig.InputEnabled;
            if (suspended) { if (_ghost != null) _ghost.SetActive(false); return; }

            float range = pl != null ? pl.placeRange : 8f;
            float maxSlope = pl != null ? pl.placeMaxSlope : 40f;
            float step = pl != null ? pl.placeYawStep : 15f;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float sy = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(sy) > 0.01f) _yaw = Mathf.Repeat(_yaw + (sy > 0f ? step : -step), 360f);
                if (mouse.rightButton.wasPressedThisFrame) { Cancel(); Toast.Show("Placement cancelled.", 2f); return; }
            }

            if (!FindGround(cam, range, out Vector3 point, out Vector3 normal)) { if (_ghost != null) _ghost.SetActive(false); return; }
            bool valid = Vector3.Angle(normal, Vector3.up) <= maxSlope;
            var rot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, _yaw, 0f);

            if (_ghost == null) MakeGhost(point, rot);
            _ghost.SetActive(true);
            _ghost.transform.SetPositionAndRotation(point, rot);
            if (!_validKnown || valid != _lastValid) { _validKnown = true; _lastValid = valid; Tint(valid); }

            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Time.unscaledTime - _startedAt > 0.2f)
            {
                if (!valid) { Toast.Show("Too steep to stand a flood light here.", 2f); return; }
                if (PlacedFloodLight.PlaceAt(point, rot, out string why))
                {
                    Cancel();
                    Toast.Show("Flood light placed. E to pick it up.");
                }
                else { Cancel(); if (!string.IsNullOrEmpty(why)) Toast.Show(why); }
            }
        }

        /// <summary>Ground under the crosshair: first hit along the view ray that is not the player; if the ray finds nothing in
        /// range (looking at the horizon), the ground below the end of the ray.</summary>
        bool FindGround(Transform cam, float range, out Vector3 point, out Vector3 normal)
        {
            point = default; normal = Vector3.up;
            var ray = new Ray(cam.position, cam.forward);
            var hits = Physics.RaycastAll(ray, range, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var root = _rig != null ? _rig.Root : null;
            foreach (var h in hits)
            {
                if (root != null && h.transform.IsChildOf(root)) continue;
                if (_ghost != null && h.transform.IsChildOf(_ghost.transform)) continue;
                point = h.point; normal = h.normal;
                return true;
            }
            Vector3 end = ray.GetPoint(range) + Vector3.up * 2f;
            var down = Physics.RaycastAll(end, Vector3.down, range + 6f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(down, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in down)
            {
                if (root != null && h.transform.IsChildOf(root)) continue;
                point = h.point; normal = h.normal;
                return true;
            }
            return false;
        }

        void MakeGhost(Vector3 pos, Quaternion rot)
        {
            _ghost = PlacedFloodLight.Build(pos, rot);
            _ghost.name = "Flood Light Ghost";
            var comp = _ghost.GetComponent<PlacedFloodLight>();
            if (comp != null) DestroyImmediate(comp);
            var col = _ghost.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            foreach (var l in _ghost.GetComponentsInChildren<Light>(true)) l.enabled = false;

            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave, name = "FloodGhost" };
            if (_mat.HasProperty("_Surface"))
            {
                _mat.SetFloat("_Surface", 1f);
                _mat.SetFloat("_Blend", 0f);
                _mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                _mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                _mat.SetFloat("_ZWrite", 0f);
                _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _mat.SetOverrideTag("RenderType", "Transparent");
                _mat.renderQueue = (int)RenderQueue.Transparent;
            }
            foreach (var r in _ghost.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.sharedMaterial = _mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            _validKnown = false;
        }

        void Tint(bool valid)
        {
            if (_mat == null) return;
            var c = XenoarchPalette.A(valid ? XenoarchPalette.Highlight : XenoarchPalette.Warn, 0.5f);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c); else _mat.color = c;
        }

        void DestroyGhost()
        {
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            if (_mat != null) Destroy(_mat);
            _mat = null;
        }
    }
}