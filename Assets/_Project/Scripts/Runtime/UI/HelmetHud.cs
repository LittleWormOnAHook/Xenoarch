using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Space-helmet visor overlay (t108u). Base art: Resources/UI/Helmet_Visor.png (from the reference image Anthony
    /// supplied; its baked bars were removed so live bars sit in the same places). Edges only: the centre is clear.
    /// Pods: top-left OXYGEN + NITROGEN, top-right ENVIRONMENT + CO2, bottom-centre SUIT INTEGRITY.
    /// On by default outdoors where the air is not breathable, off aboard the ship; [H] toggles it.
    /// While visible it pushes the top-right HUD column and the hotbar clear of the pods (HudLayout insets).
    /// Readings are display-only for now (no suffocation/damage): O2/N2 drain slowly outside and refill aboard the
    /// ship, CO2 rises with exertion, suit integrity stays 100% until a damage system exists.
    /// </summary>
    public sealed class HelmetHud : MonoBehaviour
    {
        public const string TextureResource = "UI/Helmet_Visor";
        const float W = 1672f, H = 941f; // texture size: pod rectangles below are in its pixels

        [Tooltip("Minutes of suit O2 outdoors at a walk (display only).")] public float o2Minutes = 45f;
        [Tooltip("Helmet visor on when outdoors on airless/hostile bodies (toggle [H]).")] public bool helmetOn = true;

        public static float O2 { get; private set; } = 1f;
        public static float N2 { get; private set; } = 1f;
        public static float CO2 { get; private set; } = 0.1f;
        public static float SuitIntegrity { get; private set; } = 1f;
        public static bool Visible { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { O2 = 1f; N2 = 1f; CO2 = 0.1f; SuitIntegrity = 1f; Visible = false; }

        UIDocument _doc;
        VisualElement _root;
        Bar _o2, _n2, _co2, _suit;
        Label _env;
        bool _built;
        FirstPersonController _fpc;

        sealed class Bar
        {
            public VisualElement track, fill; public Label value; public bool reverse;
            public void Set(float v01, string text, bool warnHigh)
            {
                v01 = Mathf.Clamp01(v01);
                fill.style.width = Length.Percent(v01 * 100f);
                bool warn = warnHigh ? v01 > 0.6f : v01 < 0.25f;
                fill.style.backgroundColor = warn ? XenoarchPalette.WarnText : (warnHigh ? new Color(0.95f, 0.68f, 0.25f) : new Color(0.3f, 0.85f, 1f));
                value.text = text;
            }
        }

        void Start()
        {
            var go = new GameObject("[Helmet HUD]");
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 5; // under the regular HUD (10) and the minimap (40)
        }

        void Build()
        {
            var r = _doc != null ? _doc.rootVisualElement : null;
            if (r == null) return;
            XenoarchUI.FillRoot(r); r.pickingMode = PickingMode.Ignore;
            var tex = Resources.Load<Texture2D>(TextureResource);
            _root = new VisualElement { pickingMode = PickingMode.Ignore };
            _root.style.position = Position.Absolute; _root.style.left = 0; _root.style.top = 0; _root.style.right = 0; _root.style.bottom = 0;
            if (tex != null) { _root.style.backgroundImage = new StyleBackground(tex); _root.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f)); } // stretch: pod rects are in texture %
            else Debug.LogWarning($"[Xenoarch] Helmet: Resources/{TextureResource} not found (overlay readings only)");
            r.Add(_root);
            _o2 = MakeBar(189, 124, 331, 136, false);
            _n2 = MakeBar(137, 195, 253, 207, false);
            _co2 = MakeBar(1424, 196, 1535, 207, true);
            _suit = MakeBar(765, 866, 945, 878, false);
            _env = Place(new Label { pickingMode = PickingMode.Ignore }, 1300, 116, 1500, 133);
            _env.style.fontSize = 11; _env.style.color = new Color(0.75f, 0.92f, 1f); _env.style.unityTextAlign = TextAnchor.MiddleRight;
            _env.style.overflow = Overflow.Hidden; _env.style.whiteSpace = WhiteSpace.NoWrap; _env.style.textOverflow = TextOverflow.Ellipsis;
            _env.style.paddingLeft = _env.style.paddingRight = 0; _env.style.marginLeft = _env.style.marginRight = 0;
            _built = true;
        }

        T Place<T>(T e, float x0, float y0, float x1, float y1) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            e.style.left = Length.Percent(x0 / W * 100f); e.style.top = Length.Percent(y0 / H * 100f);
            e.style.width = Length.Percent((x1 - x0) / W * 100f); e.style.height = Length.Percent((y1 - y0) / H * 100f);
            _root.Add(e);
            return e;
        }

        Bar MakeBar(float x0, float y0, float x1, float y1, bool valueLeft)
        {
            var b = new Bar { reverse = valueLeft };
            b.track = Place(new VisualElement { pickingMode = PickingMode.Ignore }, x0, y0, x1, y1);
            b.track.style.backgroundColor = new Color(0.04f, 0.07f, 0.09f, 0.85f);
            b.track.style.borderTopWidth = b.track.style.borderBottomWidth = b.track.style.borderLeftWidth = b.track.style.borderRightWidth = 1;
            var bc = new Color(0.3f, 0.85f, 1f, 0.45f);
            b.track.style.borderTopColor = b.track.style.borderBottomColor = b.track.style.borderLeftColor = b.track.style.borderRightColor = bc;
            b.fill = new VisualElement { pickingMode = PickingMode.Ignore };
            b.fill.style.position = Position.Absolute; b.fill.style.top = 0; b.fill.style.bottom = 0;
            if (valueLeft) b.fill.style.right = 0; else b.fill.style.left = 0;
            b.track.Add(b.fill);
            float w = 70f;
            b.value = valueLeft ? Place(new Label { pickingMode = PickingMode.Ignore }, x0 - w - 6f, y0 - 5f, x0 - 6f, y1 + 5f)
                                : Place(new Label { pickingMode = PickingMode.Ignore }, x1 + 6f, y0 - 5f, x1 + w + 6f, y1 + 5f);
            b.value.style.fontSize = 11; b.value.style.color = new Color(0.85f, 0.95f, 1f);
            b.value.style.unityTextAlign = valueLeft ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            b.value.style.paddingLeft = b.value.style.paddingRight = b.value.style.paddingTop = b.value.style.paddingBottom = 0;
            b.value.style.marginLeft = b.value.style.marginRight = b.value.style.marginTop = b.value.style.marginBottom = 0;
            return b;
        }

        static bool Outdoors(out EnvironmentProfile env)
        {
            env = null;
            var s = SceneManager.GetActiveScene().name;
            if (s == SceneNames.Bootstrap || s == SceneNames.MainMenu || s == SceneNames.ShipStarter) return false;
            var body = XenoarchRuntime.Session?.Exploration?.Galaxy?.FindBodyByScene(s);
            if (body != null && body.kind == CelestialKind.Station) return false; // pressurised interior
            env = Object.FindAnyObjectByType<EnvironmentDirector>()?.Current;
            return env != null && !env.breathable;
        }

        void Update()
        {
            if (_doc == null) return;
            if (_built && (_root == null || _root.panel == null)) { _built = false; _root?.RemoveFromHierarchy(); } // detached (t139u): rebuild
            if (!_built) Build();
            if (!_built) return;

            var kb = Keyboard.current;
            bool outdoors = Outdoors(out var env);
            if (kb != null && kb.hKey.wasPressedThisFrame && outdoors && !UIState.BlocksGameplay)
            {
                helmetOn = !helmetOn;
                Toast.Show(helmetOn ? "Helmet visor on  [H]" : "Helmet visor off  [H]", 2f);
            }

            Simulate(outdoors);
            bool show = outdoors && helmetOn && !RoverVehicle.AnySeated && !UIState.IsOpen(Panels.Loading) && !UIState.BlocksGameplay && !DMI.Xenoarch.Scenes.SceneLoader.Busy;
            Visible = show;
            XenoarchUI.Show(_root, show);
            // Keep other HUD out of the pods while the visor is up.
            HudLayout.TopInset = show ? 0.27f : 0f;
            HudLayout.BottomInset = show ? 0.105f : 0f;
            if (!show) return;

            _o2.Set(O2, $"{O2 * 100f:0}%", false);
            _n2.Set(N2, $"{N2 * 100f:0}%", false);
            _co2.Set(CO2, $"{CO2 * 2.5f:0.0}%", true);   // scrubber load shown as cabin CO2 %, 0-2.5
            _suit.Set(SuitIntegrity, $"{SuitIntegrity * 100f:0}%", false);
            if (env != null) _env.text = $"{env.surfaceTemperatureC:0}\u00B0C   {env.gravity / 9.81f:0.00} g   {env.atmosphere}";
        }

        void Simulate(bool outdoors)
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_fpc == null && Time.frameCount % 30 == 0) _fpc = Object.FindAnyObjectByType<FirstPersonController>();
            var fpc = _fpc;
            bool sprint = fpc != null && fpc.IsSprinting;
            if (outdoors)
            {
                float use = dt / Mathf.Max(1f, o2Minutes * 60f) * (sprint ? 1.6f : 1f);
                O2 = Mathf.Max(0f, O2 - use);
                N2 = Mathf.Max(0f, N2 - use * 0.25f);
                CO2 = Mathf.MoveTowards(CO2, sprint ? 0.55f : 0.16f, dt * (sprint ? 0.05f : 0.03f));
            }
            else
            {
                O2 = Mathf.MoveTowards(O2, 1f, dt * 0.05f);
                N2 = Mathf.MoveTowards(N2, 1f, dt * 0.05f);
                CO2 = Mathf.MoveTowards(CO2, 0.08f, dt * 0.05f);
            }
        }

        void OnDisable() { HudLayout.TopInset = 0f; HudLayout.BottomInset = 0f; Visible = false; }
    }
}
