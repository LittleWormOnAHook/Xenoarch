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
    /// Per-planet minimap (top-right) over the baked relief of the landing region: player arrow, shuttle, orbit-scanned
    /// dig sites (approximate, with their uncertainty), revealed valuable caches and the handheld scan radius.
    /// M toggles a larger view. Only on bodies with a baked surface map.
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
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 40;
        }

        void Bind(Scene scene)
        {
            _body = null;
            var x = XenoarchRuntime.Session?.Exploration;
            if (x != null && x.Galaxy != null)
            {
                var b = x.Galaxy.FindBodyByScene(scene.name);
                if (ShipTravel.UsesShuttle(b) && b.surfaceMapRect.width >= 10f) _body = b;
            }
            EnsureDoc();
            var root = _doc.rootVisualElement;
            if (root == null) return;
            root.Clear(); _frame = null; _map = null;
            root.pickingMode = PickingMode.Ignore;
            if (_body == null) return;

            _frame = new VisualElement { pickingMode = PickingMode.Ignore };
            _frame.style.position = Position.Absolute; _frame.style.right = 18; _frame.style.top = 18;
            _frame.style.backgroundColor = new Color(0.02f, 0.03f, 0.04f, 0.75f);
            _frame.style.paddingLeft = _frame.style.paddingRight = _frame.style.paddingTop = _frame.style.paddingBottom = 4;
            _title = new Label { pickingMode = PickingMode.Ignore };
            _title.style.color = new Color(0.67f, 0.8f, 0.94f, 0.85f); _title.style.fontSize = 11; _title.style.marginBottom = 3;
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
            _title.text = _large ? $"{_body.displayName.ToUpperInvariant()}  -  landing region {_body.surfaceMapRect.width:0} m   [M] close" : $"{_body.displayName.ToUpperInvariant()}   [M]";
        }

        void Update()
        {
            if (_frame == null) return;
            bool show = !UIState.BlocksGameplay && !(DMI.Xenoarch.Scenes.SceneLoader.Busy);
            XenoarchUI.Show(_frame, show);
            if (!show) return;
            var kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame) { _large = !_large; Resize(); }
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
            foreach (var site in x.SitesOnBody(_body.Id))
            {
                var st = x.Site(site.Id);
                if (st.revealLevel <= 0) continue;
                var est = site.position + ExplorationService.EstimateOffset(site, st.revealLevel);
                _map.Markers.Add(new SurfaceMapView.Marker { id = site.Id, kind = SurfaceMapView.Kind.Site, world = new Vector2(est.x, est.z),
                    uncertainty = ExplorationService.Uncertainty(st.revealLevel), label = st.revealLevel >= 3 ? site.displayName : "Signature" });
            }
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
