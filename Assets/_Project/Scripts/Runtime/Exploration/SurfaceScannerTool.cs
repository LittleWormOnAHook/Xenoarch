using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>
    /// Handheld surface scanner (on the player). Equip with 1 / D-pad up when the scanner item is in the inventory,
    /// pulse with LMB / RT. A pulse reads the strongest archaeological signal within range and depth (stats from
    /// <see cref="SurfaceScannerDefinition"/>): strength, approx. distance and bearing, depth; accuracy adds error.
    /// Detected sites reach "surface signal"; within the pinpoint radius they are pinpointed and marked (persisted).
    /// </summary>
    public sealed class SurfaceScannerTool : MonoBehaviour
    {
        [SerializeField] private FirstPersonArms arms;
        [SerializeField] private Hand hand = Hand.Right;

        public bool Equipped { get; private set; }
        public string LastReading { get; private set; }

        float _cooldownUntil, _pulseT = -1f;
        UIDocument _doc;
        VisualElement _panel, _pulse;
        Label _text;
        AudioSource _audio;
        AudioClip _beep;
        int _pulseCount;

        static SurfaceScannerDefinition Def => XenoarchRuntime.Session?.Config.surfaceScanner;

        bool HasItem
        {
            get
            {
                var s = XenoarchRuntime.Session; var d = Def;
                return s != null && d != null && s.Inventory.Count(d.itemId) > 0;
            }
        }

        void Awake()
        {
            if (arms == null) arms = GetComponentInChildren<FirstPersonArms>(true);
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.volume = 0.35f;
            _beep = MakeBeep();
        }

        void Start() => BuildUi();

        void OnDisable() { if (Equipped) SetEquipped(false); }

        public void SetEquipped(bool on)
        {
            Equipped = on && HasItem;
            if (arms != null) arms.SetHeld(hand, Equipped);
            XenoarchUI.Show(_panel, Equipped);
            if (Equipped && string.IsNullOrEmpty(LastReading)) SetText("Ready. Pulse to scan (LMB / RT).", false);
        }

        void Update()
        {
            if (Equipped && !HasItem) SetEquipped(false);
            if (_pulseT >= 0f) AnimatePulse();
            if (_clearAt > 0f && Time.time >= _clearAt) SetText("Ready. Pulse to scan (LMB / RT).", false);
            if (UIState.BlocksGameplay) return;
            var kb = Keyboard.current; var gp = Gamepad.current; var mouse = Mouse.current;
            if ((kb != null && kb.digit1Key.wasPressedThisFrame) || (gp != null && gp.dpad.up.wasPressedThisFrame))
            {
                if (!HasItem && !Equipped) Debug.Log("[Xenoarch] No handheld scanner in inventory");
                SetEquipped(!Equipped);
            }
            if (!Equipped) return;
            bool fire = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (gp != null && gp.rightTrigger.wasPressedThisFrame);
            if (fire && Time.time >= _cooldownUntil) Pulse();
        }

        public void Pulse()
        {
            var s = XenoarchRuntime.Session; var d = Def;
            if (s == null || d == null) return;
            _cooldownUntil = Time.time + d.cooldown;
            _pulseT = 0f; _pulseCount++;
            if (arms != null) arms.RaiseTemporarily(hand, 0.5f);

            var x = s.Exploration;
            string scene = SceneManager.GetActiveScene().name;
            Vector3 me = transform.position;
            ArchaeologicalSiteDefinition best = null; float bestScore = 0f, bestDist = 0f;
            foreach (var site in x.SitesInScene(scene))
            {
                Vector3 flat = site.position - me; flat.y = 0f;
                float dist = flat.magnitude;
                if (dist > d.range + site.radius || site.depth > d.maxDepth) continue;
                float score = site.signalStrength * (1f - Mathf.Clamp01(dist / (d.range + site.radius))) * (1f - 0.5f * site.depth / Mathf.Max(0.1f, d.maxDepth));
                if (score > bestScore) { best = site; bestScore = score; bestDist = dist; }
            }

            if (best == null)
            {
                SetText($"No signal within {d.range:0} m (max depth {d.maxDepth:0.#} m).");
                Beep(0.6f, 1);
                return;
            }

            var rng = new System.Random(best.seed ^ (_pulseCount * 7919));
            float err = (1f - d.accuracy);
            float distShown = Mathf.Max(0f, bestDist * (1f + err * ((float)rng.NextDouble() * 2f - 1f) * 0.5f));
            Vector3 to = best.position - me; to.y = 0f;
            float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + err * ((float)rng.NextDouble() * 2f - 1f) * 40f;
            float rel = Mathf.DeltaAngle(transform.eulerAngles.y, bearing);
            float depthShown = best.depth * (1f + err * ((float)rng.NextDouble() * 2f - 1f) * 0.4f);

            bool pin = bestDist <= d.pinpointRadius + best.radius * 0.25f;
            x.Reveal(best.Id, pin ? (int)SiteRevealLevel.Pinpointed : (int)SiteRevealLevel.SurfaceSignal);
            if (pin) { SiteMarkerDirector.Instance?.Refresh(); SiteMarkerDirector.Instance?.Ping(best.Id); }

            SetText(pin
                ? $"PINPOINTED: {best.displayName}\nDepth ~{depthShown:0.0} m. Site marked."
                : $"Signal {Mathf.RoundToInt(bestScore * 100f)}%   ~{distShown:0} m {Direction(rel)}\nDepth ~{depthShown:0.0} m");
            Beep(1f + bestScore * 1.5f, pin ? 3 : 1 + Mathf.RoundToInt(bestScore * 3f));
        }

        static string Direction(float rel)
        {
            float a = Mathf.Abs(rel);
            if (a < 20f) return "ahead";
            if (a > 150f) return "behind";
            return (rel > 0 ? "right" : "left") + (a < 70f ? ", ahead" : a > 110f ? ", behind" : "");
        }

        /// <summary>Scan readings clear after this many seconds (the pinpointed site keeps its square border).</summary>
        public const float ReadingSeconds = 5f;
        float _clearAt = -1f;

        void SetText(string t, bool timed = true)
        {
            LastReading = t; if (_text != null) _text.text = t;
            _clearAt = timed ? Time.time + ReadingSeconds : -1f;
        }

        void Beep(float pitch, int count)
        {
            if (_audio == null || _beep == null) return;
            _audio.pitch = pitch;
            for (int i = 0; i < count; i++) _audio.PlayOneShot(_beep, 1f / (1 + i * 0.3f));
        }

        void AnimatePulse()
        {
            _pulseT += Time.deltaTime;
            float t = _pulseT / 0.9f;
            if (_pulse == null) return;
            if (t >= 1f) { _pulseT = -1f; XenoarchUI.Show(_pulse, false); return; }
            XenoarchUI.Show(_pulse, true);
            float size = Mathf.Lerp(40f, 900f, t);
            _pulse.style.width = size; _pulse.style.height = size;
            _pulse.style.marginLeft = -size / 2; _pulse.style.marginTop = -size / 2;
            _pulse.style.opacity = 1f - t;
        }

        void BuildUi()
        {
            var host = new GameObject("Surface Scanner UI");
            host.transform.SetParent(transform, false);
            _doc = host.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 5;
            var root = _doc.rootVisualElement;
            if (root == null) return;
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch"); if (uss != null) root.styleSheets.Add(uss);
            root.pickingMode = PickingMode.Ignore;
            var fill = new VisualElement(); fill.AddToClassList("xa-fill"); fill.pickingMode = PickingMode.Ignore; root.Add(fill);
            _pulse = new VisualElement(); _pulse.AddToClassList("xa-scan__pulse"); _pulse.pickingMode = PickingMode.Ignore; fill.Add(_pulse);
            XenoarchUI.Show(_pulse, false);
            _panel = new VisualElement(); _panel.AddToClassList("xa-scan"); _panel.pickingMode = PickingMode.Ignore; fill.Add(_panel);
            var title = new Label("SURFACE SCANNER"); title.AddToClassList("xa-scan__title"); _panel.Add(title);
            _text = new Label(); _text.AddToClassList("xa-scan__text"); _panel.Add(_text);
            var hint = new Label("LMB/RT pulse   1/D-pad up holster"); hint.AddToClassList("xa-hint"); _panel.Add(hint);
            XenoarchUI.Show(_panel, false);
        }

        static AudioClip MakeBeep()
        {
            const int rate = 22050; int n = rate / 12;
            var data = new float[n];
            for (int i = 0; i < n; i++) { float t = i / (float)rate; data[i] = Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 40f) * 0.6f; }
            var clip = AudioClip.Create("ScannerBeep", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
