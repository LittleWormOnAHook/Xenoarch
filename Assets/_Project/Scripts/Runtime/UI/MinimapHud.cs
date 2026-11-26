using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Per-planet minimap (top-right, or mid-right inside the visor when the helmet is on — t169u/t170u) over the baked relief of the landing region: player arrow, shuttle, orbit-scanned
    /// dig sites (approximate, with their uncertainty), revealed valuable caches and the handheld scan radius.
    /// M opens the full Maps window (Surface + Dig Sites tabs, <see cref="DigSiteMapWindow"/>). Only on bodies with a baked surface map.
    /// </summary>
    public sealed class MinimapHud : MonoBehaviour
    {
        public Key toggleKey = Key.M;
        public float smallSize = 190f, largeSize = 440f;

        UIDocument _doc; VisualElement _frame; SurfaceMapView _map; Label _title;
        CelestialBodyDefinition _body; bool _large; float _next;

        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void Start() => Bind(SceneManager.GetActiveScene());
        void OnLoaded(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) Bind(s); }

        void EnsureDoc()
        {
            if (_doc != null) return;
            // Own child object (t135u): a UIDocument on the runtime object made every other runtime UIDocument (inventory,
            // maps, pause, helmet, journal reader - children of that object) a nested child of the minimap's panel, and
            // Bind's root.Clear() on each scene load wiped them (inventory / M map opened invisibly and blocked input).
            var host = new GameObject("[Minimap HUD]");
            host.transform.SetParent(transform, false);
            _doc = host.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 40;
        }

        void Bind(Scene scene)
        {
            _body = null; HudLayout.TopRightBottom = 0f;
            var x = XenoarchRuntime.Session?.Exploration;
            if (x != null && x.Galaxy != null)
            {
                var b = x.Galaxy.FindBodyByScene(scene.name);
                if (ShipTravel.UsesShuttle(b) && b.surfaceMapRect.width >= 10f) _body = b;
            }
            EnsureDoc();
            var root = _doc.rootVisualElement; XenoarchUI.FillRoot(root);
            if (root == null) return;
            root.Clear(); _frame = null; _map = null;
            root.pickingMode = PickingMode.Ignore;
            if (_body == null) return;

            _frame = new VisualElement { pickingMode = PickingMode.Ignore };
            _frame.style.position = Position.Absolute; _frame.style.right = HudLayout.CornerMargin; _frame.style.top = HudLayout.CornerMargin;
            _frame.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDeep, 0.8f);
            _frame.style.paddingLeft = _frame.style.paddingRight = _frame.style.paddingTop = _frame.style.paddingBottom = 4;
            _title = new Label { pickingMode = PickingMode.Ignore };
            _title.style.color = XenoarchPalette.Highlight; _title.style.fontSize = 11; _title.style.marginBottom = 3;
            _frame.Add(_title);
            _map = new SurfaceMapView(_body.surfaceMap, _body.surfaceMapRect) { AutoSquare = false, pickingMode = PickingMode.Ignore };
            _map.style.marginBottom = 0;
            _frame.Add(_map);
            root.Add(_frame);
            Resize();
        }

        void Resize()
        {
            if (_map == null) return;
            float s = _large ? largeSize : smallSize;
            _map.style.width = s; _map.style.height = s; _map.ShowLabels = _large;
            _title.text = _large ? $"{_body.displayName.ToUpperInvariant()}  -  landing region {_body.surfaceMapRect.width:0} m   [M] close" : $"{_body.displayName.ToUpperInvariant()}   [M] map";
        }

        void Update()
        {
            if (_frame == null) return;
            bool show = !UIState.BlocksGameplay && !(DMI.Xenoarch.Scenes.SceneLoader.Busy);
            _frame.style.top = HudLayout.MinimapTop;
            _frame.style.right = HudLayout.MinimapRight;
            XenoarchUI.Show(_frame, show);
            HudLayout.TopRightBottom = show && !float.IsNaN(_frame.layout.yMax) && _frame.layout.height > 1f ? _frame.layout.yMax : 0f;
            if (!show) return;
            // M is handled by DigSiteMapWindow (full-size map with cursor + pause); the minimap stays small.
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.15f;
            Refresh();
        }

        void Refresh()
        {
            var session = XenoarchRuntime.Session;
            if (session == null || _map == null) return;
            var x = session.Exploration;
            _map.Markers.Clear();
            foreach (var k in KnownSites.OnBody(x, _body.Id))   // t182u: orbital + handheld reveals, shared with the M map
                _map.Markers.Add(new SurfaceMapView.Marker { id = k.Def.Id, kind = SurfaceMapView.Kind.Site, world = k.Map,
                    uncertainty = k.Uncertainty, label = k.Label() });
            var vdir = DMI.Xenoarch.Valuables.ValuableSiteDirector.Instance;
            if (vdir != null)
                foreach (var v in vdir.Sites)
                    if (session.Valuables.IsRevealed(v.id) && !session.Valuables.IsCollected(v.id))
                        _map.Markers.Add(new SurfaceMapView.Marker { id = v.id, kind = SurfaceMapView.Kind.Cache, world = new Vector2(v.position.x, v.position.z), label = "Cache" });
            var shuttle = ShuttleDirector.Instance != null ? ShuttleDirector.Instance.Shuttle : null;
            if (shuttle != null)
                _map.Markers.Add(new SurfaceMapView.Marker { id = "shuttle", kind = SurfaceMapView.Kind.Shuttle, world = new Vector2(shuttle.transform.position.x, shuttle.transform.position.z), label = "Shuttle" });
            var rig = PlayerRigLocator.Find();
            if (rig != null)
            {
                var p = rig.Root.position; var cam = Camera.main;
                float yaw = cam != null ? cam.transform.eulerAngles.y : rig.Root.eulerAngles.y;
                _map.Markers.Add(new SurfaceMapView.Marker { id = "player", kind = SurfaceMapView.Kind.Player, world = new Vector2(p.x, p.z), heading = yaw });
                bool scannerOut = rig.Root.GetComponentInChildren<SurfaceScannerTool>() is SurfaceScannerTool sc && sc.Equipped;
                _map.ScanCentre = new Vector2(p.x, p.z); _map.ScanRadius = scannerOut || _large ? ScannerStats.HandheldRange() : 0f;
            }
            _map.Rebuild();
        }
    }
}
