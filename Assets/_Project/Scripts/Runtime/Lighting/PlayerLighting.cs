using DMI.Xenoarch.Core;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Scenes;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMI.Xenoarch.Lighting
{
    /// <summary>
    /// Helmet lamps (F) plus the handheld flashlight inventory tool. Two spots sit at the top-left and
    /// top-right corners of the view (the helmet / mask), parented to the current view camera. Only the
    /// right lamp casts shadows, to keep it cheap. The handheld item uses the normal hotbar / tool-wheel
    /// equip flow and a narrower, longer beam. Starts with the helmet lamps off. Added at runtime by
    /// <see cref="PlayerToolsBootstrap"/> so placed prefabs are not edited.
    /// </summary>
    public sealed class PlayerLighting : MonoBehaviour
    {
        public const string FlashlightId = "item.tool.handheld_flashlight";
        public const string FloodLightId = "item.equipment.flood_light";

        static readonly Key[] SlotKeys =
        {
            Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
        };

        [Header("Helmet lamps (F)")]
        [Tooltip("Range of each helmet lamp (m).")]
        [Range(1f, 200f)] public float helmetRange = 30f;
        [Tooltip("Outer spot angle (degrees).")]
        [Range(5f, 179f)] public float helmetSpotAngle = 55f;
        [Tooltip("Inner (full-intensity) angle (degrees). Keep below the outer angle for a soft falloff.")]
        [Range(0f, 179f)] public float helmetInnerAngle = 32f;
        [Tooltip("Intensity in the project's light units (URP). Wide range for testing on dark planets.")]
        [Range(0.01f, 5000f)] public float helmetIntensity = 6.5f;
        public Color helmetColor = new Color(1f, 0.93f, 0.78f);
        [Tooltip("Horizontal offset of each lamp from the camera centre (m), toward the mask corners.")]
        public float helmetCornerX = 0.16f;
        [Tooltip("Vertical offset above the camera (m).")]
        public float helmetCornerY = 0.11f;
        [Tooltip("Forward offset from the camera (m).")]
        public float helmetForward = 0.08f;
        [Tooltip("Yaw toward the view centre (degrees).")]
        public float helmetInwardYaw = 8f;
        [Tooltip("Downward pitch (degrees).")]
        public float helmetPitch = 4f;

        [Header("Handheld flashlight")]
        [Tooltip("Range of the handheld beam (m).")]
        [Range(1f, 200f)] public float handheldRange = 40f;
        [Range(5f, 179f)] public float handheldSpotAngle = 32f;
        [Range(0f, 179f)] public float handheldInnerAngle = 16f;
        [Range(0.01f, 5000f)] public float handheldIntensity = 9f;
        public Color handheldColor = new Color(1f, 0.94f, 0.8f);
        [Tooltip("Camera-space offset used when the held mesh is hidden (m).")]
        public Vector3 handheldCameraOffset = new Vector3(0.12f, -0.08f, 0.18f);

        [Header("Battery (suit power: the pool is 100, a Power Cell adds 60; X recharges)")]
        [Tooltip("Suit power drained per second while the helmet lamps are on (both lamps together). 0.2 = about 8 minutes from full.")]
        [Range(0f, 100f)] public float helmetDrainPerSec = 0.2f;
        [Tooltip("Suit power drained per second while the handheld flashlight is lit. 0.15 = about 11 minutes from full.")]
        [Range(0f, 100f)] public float handheldDrainPerSec = 0.15f;
        [Tooltip("Power a placed flood light stand holds (units; 60 = one Power Cell). It runs on its own charge, not the suit.")]
        [Range(1f, 10000f)] public float floodCapacity = 60f;
        [Tooltip("Power a placed flood light uses per second. 0.2 on 60 = 5 minutes per charge.")]
        [Range(0f, 100f)] public float floodDrainPerSec = 0.2f;
        [Tooltip("Warn when suit power falls to this fraction while a light is on.")]
        [Range(0f, 1f)] public float lowPowerWarn = 0.15f;

        [Header("Flood light placement")]
        [Tooltip("How far from the camera the ghost can sit (m).")]
        [Range(1f, 60f)] public float placeRange = 8f;
        [Tooltip("Steepest ground the stand can be placed on (degrees from flat).")]
        [Range(0f, 89f)] public float placeMaxSlope = 40f;
        [Tooltip("Yaw per mouse-wheel notch (degrees).")]
        [Range(1f, 90f)] public float placeYawStep = 15f;

        public static PlayerLighting Current { get; private set; }
        public bool HelmetOn { get; private set; }
        public bool FlashlightEquipped { get; private set; }

        Light _helmL, _helmR, _hand;
        Transform _camBind;
        IPlayerRig _rig;
        FirstPersonArms _arms;

        public static PlayerLighting Ensure(Transform root)
        {
            if (root == null) return null;
            var pl = root.GetComponent<PlayerLighting>();
            return pl != null ? pl : root.gameObject.AddComponent<PlayerLighting>();
        }

        bool _lowWarned;

        void Awake()
        {
            Current = this;
            if (GetComponent<FloodLightPlacer>() == null) gameObject.AddComponent<FloodLightPlacer>();
            _rig = GetComponent<IPlayerRig>() ?? GetComponentInParent<IPlayerRig>();
            _arms = GetComponentInChildren<FirstPersonArms>(true);
            _helmL = MakeSpot("HelmetLamp_L", shadows: false);
            _helmR = MakeSpot("HelmetLamp_R", shadows: true);
            _hand = MakeSpot("HandheldFlashlight", shadows: false);
            ApplyLightSettings();
            SetLightOn(_helmL, false);
            SetLightOn(_helmR, false);
            SetLightOn(_hand, false);
        }

        Light MakeSpot(string name, bool shadows)
        {
            var t = transform.Find(name);
            var go = t != null ? t.gameObject : new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.GetComponent<Light>();
            if (l == null) l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.shadowStrength = 0.55f;
            l.bounceIntensity = 0f;
            l.renderMode = LightRenderMode.Auto;
            return l;
        }

        static void SetLightOn(Light l, bool on)
        {
            if (l == null) return;
            l.enabled = on;
            if (l.gameObject.activeSelf != on) l.gameObject.SetActive(on);
        }

        void ApplyLightSettings()
        {
            Configure(_helmL, helmetRange, helmetSpotAngle, helmetInnerAngle, helmetIntensity, helmetColor);
            Configure(_helmR, helmetRange, helmetSpotAngle, helmetInnerAngle, helmetIntensity, helmetColor);
            Configure(_hand, handheldRange, handheldSpotAngle, handheldInnerAngle, handheldIntensity, handheldColor);
        }

        static void Configure(Light l, float range, float angle, float inner, float intensity, Color color)
        {
            if (l == null) return;
            l.type = LightType.Spot;
            l.range = Mathf.Max(0.1f, range);
            l.spotAngle = Mathf.Clamp(angle, 1f, 179f);
            l.innerSpotAngle = Mathf.Clamp(inner, 0f, l.spotAngle);
            l.intensity = Mathf.Max(0f, intensity);
            l.color = color;
        }

        bool Blocked
        {
            get
            {
                if (UIState.BlocksGameplay || SceneLoader.Busy || Time.timeScale == 0f) return true;
                if (_rig != null && !_rig.InputEnabled) return true;
                return false;
            }
        }

        void Update()
        {
            if (_rig == null) _rig = GetComponent<IPlayerRig>() ?? GetComponentInParent<IPlayerRig>() ?? PlayerRigLocator.Find();
            if (_arms == null) _arms = GetComponentInChildren<FirstPersonArms>(true);
            BindCamera();
            ApplyLightSettings();
            if (!Blocked)
            {
                var kb = Keyboard.current;
                if (kb != null && kb.fKey.wasPressedThisFrame) SetHelmetOn(!HelmetOn);
                HandleHotbar(kb);
                // X recharges the suit from a Power Cell when no sonic tool is out (with one out, the dig tool handles X).
                if (kb != null && kb.xKey.wasPressedThisFrame && !AnyToolOut)
                {
                    if (SonicTools.UseCell()) Toast.Show($"Power Cell used: suit power {SonicTools.Energy01:P0}. {SonicTools.Cells} left.", 2.5f);
                    else Toast.Show(SonicTools.Cells < 1 ? "No Power Cells. Craft them aboard the ship." : "Suit power is already full.", 2.5f);
                }
            }
            DrainBattery();
            if (FlashlightEquipped)
            {
                if (!Has(FlashlightId) || OtherToolOut()) SetFlashlightEquipped(false);
            }
            ApplyActiveLights();
        }

        /// <summary>The lamps and flashlight draw from the suit pool; they go dark when it is empty.</summary>
        void DrainBattery()
        {
            float rate = (HelmetOn ? helmetDrainPerSec : 0f) + (FlashlightEquipped ? handheldDrainPerSec : 0f);
            if (rate <= 0f) return;
            if (Time.deltaTime > 0f && !SonicTools.DrainEnergy(rate * Time.deltaTime))
            {
                HelmetOn = false;
                FlashlightEquipped = false;
                _lowWarned = false;
                Toast.Show("Suit power is empty: lights off. A Power Cell (X) recharges it.", 4f);
                Debug.Log("[FlashlightDiag] suit power empty, lights off");
                return;
            }
            float e = SonicTools.Energy01;
            if (e <= lowPowerWarn && !_lowWarned) { _lowWarned = true; Toast.Show($"Suit power low ({e:P0}). A Power Cell (X) recharges it.", 3.5f); }
            else if (e > lowPowerWarn + 0.1f) _lowWarned = false;
        }

        void HandleHotbar(Keyboard kb)
        {
            if (kb == null || XenoarchRuntime.Session == null) return;
            var bar = XenoarchRuntime.Session.Hotbar;
            if (bar == null) return;
            for (int k = 0; k < SlotKeys.Length && k + 1 < DMI.Xenoarch.Inventory.Hotbar.Size; k++)
            {
                if (!kb[SlotKeys[k]].wasPressedThisFrame) continue;
                string id = bar.At(k + 1);
                if (id == FlashlightId) { SetFlashlightEquipped(!FlashlightEquipped); return; }
                if (id == FloodLightId)
                {
                    if (FloodLightPlacer.Active) FloodLightPlacer.Cancel();
                    else if (!PlacedFloodLight.BeginPlacement(out var why) && !string.IsNullOrEmpty(why)) Toast.Show(why);
                    return;
                }
            }
        }

        public void SetHelmetOn(bool on)
        {
            if (on && !SonicTools.HasPower) { Toast.Show("Suit power is empty. A Power Cell (X) recharges it.", 3f); on = false; }
            HelmetOn = on;
            Debug.Log($"[FlashlightDiag] helmet lamps {(on ? "ON" : "OFF")} range={helmetRange:0.#} angle={helmetSpotAngle:0.#} intensity={helmetIntensity:0.##}");
        }

        public void SetFlashlightEquipped(bool on)
        {
            if (on)
            {
                if (!Has(FlashlightId))
                {
                    Toast.Show(DigTool.NotOwnedText("handheld flashlight"), 3f);
                    on = false;
                }
                else if (!SonicTools.HasPower)
                {
                    Toast.Show("Suit power is empty. A Power Cell (X) recharges it.", 3f);
                    on = false;
                }
                else HolsterOthers();
            }
            if (FlashlightEquipped == on) return;
            FlashlightEquipped = on;
            Debug.Log($"[FlashlightDiag] handheld flashlight {(on ? "EQUIPPED" : "HOLSTERED")} range={handheldRange:0.#} angle={handheldSpotAngle:0.#} intensity={handheldIntensity:0.##}");
        }

        /// <summary>Another tool (dig tools, scanner, depth probe) is currently out.</summary>
        public bool AnyToolOut => OtherToolOut();

        /// <summary>Puts away every held tool except the flashlight (used when placing a flood light).</summary>
        public void HolsterTools() => HolsterOthers();

        void HolsterOthers()
        {
            var dig = GetComponent<DigTool>();
            if (dig != null && dig.Equipped >= 0) dig.Holster();
            var scanner = GetComponentInChildren<SurfaceScannerTool>(true);
            if (scanner != null && scanner.Equipped) scanner.SetEquipped(false);
            var probe = GetComponent<DepthProbeTool>();
            if (probe != null && probe.Equipped) probe.SetEquipped(false);
        }

        bool OtherToolOut()
        {
            var dig = GetComponent<DigTool>();
            if (dig != null && dig.Equipped >= 0) return true;
            var scanner = GetComponentInChildren<SurfaceScannerTool>(true);
            if (scanner != null && scanner.Equipped) return true;
            var probe = GetComponent<DepthProbeTool>();
            return probe != null && probe.Equipped;
        }

        static bool Has(string itemId) =>
            XenoarchRuntime.Session != null && XenoarchRuntime.Session.Inventory.Count(itemId) > 0;

        void BindCamera()
        {
            var cam = _rig != null && _rig.ViewCamera != null ? _rig.ViewCamera.transform : (Camera.main != null ? Camera.main.transform : null);
            if (cam == _camBind) return;
            _camBind = cam;
            if (cam == null) return;
            ParentLocal(_helmL, cam, new Vector3(-helmetCornerX, helmetCornerY, helmetForward),
                Quaternion.Euler(helmetPitch, helmetInwardYaw, 0f));
            ParentLocal(_helmR, cam, new Vector3(helmetCornerX, helmetCornerY, helmetForward),
                Quaternion.Euler(helmetPitch, -helmetInwardYaw, 0f));
        }

        void ApplyActiveLights()
        {
            if (_camBind != null)
            {
                // Keep the corner offsets live so inspector tweaks apply while playing a session later.
                if (_helmL != null && _helmL.transform.parent == _camBind)
                    ParentLocal(_helmL, _camBind, new Vector3(-helmetCornerX, helmetCornerY, helmetForward),
                        Quaternion.Euler(helmetPitch, helmetInwardYaw, 0f));
                if (_helmR != null && _helmR.transform.parent == _camBind)
                    ParentLocal(_helmR, _camBind, new Vector3(helmetCornerX, helmetCornerY, helmetForward),
                        Quaternion.Euler(helmetPitch, -helmetInwardYaw, 0f));
            }
            SetLightOn(_helmL, HelmetOn);
            SetLightOn(_helmR, HelmetOn);

            bool handOn = FlashlightEquipped;
            if (_hand != null)
            {
                var lens = FindHeldLens();
                if (lens != null) ParentLocal(_hand, lens, Vector3.zero, Quaternion.identity);
                else if (_camBind != null) ParentLocal(_hand, _camBind, handheldCameraOffset, Quaternion.identity);
            }
            SetLightOn(_hand, handOn);
        }

        Transform FindHeldLens()
        {
            if (_arms == null || _arms.ForcedHidden) return null;
            var slot = _arms.RightSlot;
            if (slot == null) return null;
            var lens = slot.Find("Held_FlashlightPlaceholder/Lens");
            if (lens != null) return lens;
            var root = slot.Find("Held_FlashlightPlaceholder");
            return root;
        }

        static void ParentLocal(Light l, Transform parent, Vector3 localPos, Quaternion localRot)
        {
            if (l == null || parent == null) return;
            var t = l.transform;
            if (t.parent != parent) t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = localRot;
            t.localScale = Vector3.one;
        }

        void OnDisable()
        {
            SetLightOn(_helmL, false);
            SetLightOn(_helmR, false);
            SetLightOn(_hand, false);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            DestroyLight(_helmL); _helmL = null;
            DestroyLight(_helmR); _helmR = null;
            DestroyLight(_hand); _hand = null;
        }

        static void DestroyLight(Light l)
        {
            if (l != null) Destroy(l.gameObject);
        }
    }
}
