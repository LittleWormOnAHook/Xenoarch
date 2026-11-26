using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Excavation;
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
    /// Maps window (N), ZEN-9B. Tabs: "Surface" (this body's landing region with marked sites by code) and "Dig Sites"
    /// (one plan per fenced dig site: fence, numbered spot flags and their state: flagged, digging, unfinished, done).
    /// Reads <see cref="DigSiteMapsService"/>, so it works away from the site (e.g. aboard the ship) too.
    /// </summary>
    public sealed class DigSiteMapWindow : MonoBehaviour
    {
        public Key toggleKey = Key.N;
        UIDocument _doc; VisualElement _veil, _body;
        string _tab = "sites", _selected;
        float _next;
        public bool IsOpen => _veil != null;
        public const string Panel = "digsite_maps";

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[toggleKey].wasPressedThisFrame)
            {
                if (IsOpen) Close();
                else if (!UIState.BlocksGameplay && XenoarchRuntime.Instance != null && XenoarchRuntime.Instance.SessionActive && !(XenoarchRuntime.Scenes?.IsLoading ?? true)) Open();
            }
            else if (IsOpen && kb != null && kb.escapeKey.wasPressedThisFrame) Close();
            if (IsOpen && _tab == "surface" && Time.unscaledTime >= _next) { _next = Time.unscaledTime + 0.5f; Render(); }
        }

        void Open()
        {
            if (_doc == null)
            {
                var host = new GameObject("DigSiteMaps"); host.transform.SetParent(transform, false);
                _doc = host.AddComponent<UIDocument>(); _doc.panelSettings = XenoarchUI.LoadPanelSettings(); _doc.sortingOrder = 55;
            }
            var root = _doc.rootVisualElement; XenoarchUI.FillRoot(root);
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch"); if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);
            _veil = new VisualElement(); XenoarchUI.FillRoot(_veil);
            _veil.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDeep, 0.7f);
            _veil.style.alignItems = Align.Center; _veil.style.justifyContent = Justify.Center;
            var win = new VisualElement(); win.AddToClassList("xa-menu-panel");
            win.style.width = 1100; win.style.height = 720;
            var head = new VisualElement(); head.style.flexDirection = FlexDirection.Row; head.style.alignItems = Align.Center;
            var title = new Label("MAPS"); title.AddToClassList("xa-title"); title.style.fontSize = 30; title.style.marginRight = 24; head.Add(title);
            Tab(head, "Surface", "surface"); Tab(head, "Dig Sites", "sites");
            var close = new Label("[N] / Esc close"); close.AddToClassList("xa-hint"); close.style.marginLeft = 20; head.Add(close);
            win.Add(head);
            _body = new VisualElement(); _body.style.flexGrow = 1; _body.style.flexDirection = FlexDirection.Row; _body.style.marginTop = 10;
            win.Add(_body);
            _veil.Add(win); root.Add(_veil);
            UIState.SetOpen(Panel, true);
            Render();
        }

        void Close()
        {
            _veil?.RemoveFromHierarchy(); _veil = null; _body = null;
            UIState.SetOpen(Panel, false);
        }

        void OnDisable() { if (IsOpen) Close(); }

        void Tab(VisualElement head, string label, string id)
        {
            var b = new Button(() => { _tab = id; Render(); }) { text = label };
            b.AddToClassList("xa-button"); b.style.width = 160; b.style.marginRight = 8;
            head.Add(b);
        }

        void Render()
        {
            if (_body == null) return;
            _body.Clear();
            if (_tab == "surface") RenderSurface(); else RenderSites();
        }

        // ---------------------------------------------------------------- Surface tab
        void RenderSurface()
        {
            var s = XenoarchRuntime.Session; if (s == null) return;
            var x = s.Exploration;
            var body = x.Galaxy != null ? x.Galaxy.FindBodyByScene(SceneManager.GetActiveScene().name) : null;
            if (body == null || body.surfaceMapRect.width < 10f) { Note(_body, "No surface map here. Land on a body to see its landing region; the Dig Sites tab works anywhere."); return; }
            var map = new SurfaceMapView(body.surfaceMap, body.surfaceMapRect) { AutoSquare = false };
            map.style.width = 600; map.style.height = 600;
            foreach (var site in x.SitesOnBody(body.Id))
            {
                var st = x.Site(site.Id);
                if (st.revealLevel <= 0) continue;
                var est = site.position + ExplorationService.EstimateOffset(site, st.revealLevel);
                var dm = s.DigMaps.Find(site.Id);
                map.Markers.Add(new SurfaceMapView.Marker { id = site.Id, kind = SurfaceMapView.Kind.Site, world = new Vector2(est.x, est.z), uncertainty = ExplorationService.Uncertainty(st.revealLevel),
                    label = dm != null ? $"{dm.Code} {site.displayName}" : st.revealLevel >= 3 ? site.displayName : "Signature" });
            }
            var rig = PlayerRigLocator.Find();
            if (rig != null) { var p = rig.Root.position; map.Markers.Add(new SurfaceMapView.Marker { id = "player", kind = SurfaceMapView.Kind.Player, world = new Vector2(p.x, p.z), heading = rig.Root.eulerAngles.y }); }
            map.Clicked += id => { if (s.DigMaps.Find(id) != null) { _selected = id; _tab = "sites"; Render(); } };
            map.Rebuild();
            _body.Add(map);
            var side = new VisualElement(); side.style.marginLeft = 16; side.style.flexShrink = 1;
            Note(side, $"{body.displayName}: landing region {body.surfaceMapRect.width:0} m. Marked dig sites show their code (e.g. {DigSiteMapsService.PrefixFor(body.displayName)}1). Click one to open its dig map.");
            _body.Add(side);
        }

        // ---------------------------------------------------------------- Dig Sites tab
        void RenderSites()
        {
            var s = XenoarchRuntime.Session; if (s == null) return;
            var maps = s.DigMaps.Sites;
            var list = new ScrollView(); list.style.width = 300; list.style.marginRight = 14;
            _body.Add(list);
            if (maps.Count == 0) { Note(list, "No fenced dig sites yet. Pinpoint a site with the scanner (1); it gets a fence and a code like Ma1. Then hold 1 inside the fence for the refined scanner, which flags buried finds."); return; }
            if (_selected == null || s.DigMaps.Find(_selected) == null) _selected = maps[0].siteId;
            foreach (var m in maps)
            {
                int open = 0, unfinished = 0, done = 0;
                foreach (var sp in m.spots) { if (sp.State == DigSpotState.Done) done++; else if (sp.State == DigSpotState.Unfinished) unfinished++; else open++; }
                var id = m.siteId;
                var b = new Button(() => { _selected = id; Render(); }) { text = $"{m.Code}  {m.displayName}\n{m.spots.Count} spots: {open} open, {unfinished} unfinished, {done} done" };
                b.AddToClassList("xa-button"); b.style.height = 58; b.style.fontSize = 14; b.style.whiteSpace = WhiteSpace.Normal;
                if (id == _selected) { b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = XenoarchPalette.Primary; }
                list.Add(b);
            }
            var sel = s.DigMaps.Find(_selected);
            var right = new VisualElement(); right.style.flexGrow = 1; right.style.flexDirection = FlexDirection.Row;
            var plan = new DigSitePlan(sel); plan.style.width = 470; plan.style.height = 470;
            right.Add(plan);
            var spots = new ScrollView(); spots.style.marginLeft = 12; spots.style.flexGrow = 1;
            var h = new Label($"{sel.Code}  {sel.displayName}"); h.style.color = XenoarchPalette.Primary; h.style.fontSize = 20; h.style.marginBottom = 6; spots.Add(h);
            Note(spots, $"Fenced {sel.sizeX:0} x {sel.sizeZ:0} m. Codes: {sel.prefix} = body, {sel.number} = dig site, last digit(s) = spot.");
            if (sel.spots.Count == 0) Note(spots, "No spots yet: hold 1 inside the fence (refined scanner) and pulse to flag buried finds.");
            foreach (var sp in sel.spots)
            {
                var l = new Label($"{sel.SpotCode(sp)}   {StateText(sp.State)}");
                l.style.color = DigSitePlan.ColorFor(sp.State); l.style.fontSize = 16; l.style.marginTop = 3;
                spots.Add(l);
            }
            right.Add(spots);
            _body.Add(right);
        }

        static string StateText(DigSpotState s) => s switch
        {
            DigSpotState.Flagged => "flagged (red flag)",
            DigSpotState.Digging => "digging",
            DigSpotState.Unfinished => "UNFINISHED dig (flag replaced)",
            _ => "done: find recovered",
        };

        static void Note(VisualElement parent, string text)
        {
            var l = new Label(text); l.AddToClassList("xa-hint"); l.style.whiteSpace = WhiteSpace.Normal; l.style.fontSize = 14; l.style.marginBottom = 6;
            parent.Add(l);
        }
    }

    /// <summary>Top-down plan of one dig site: fence, 1 m grid, spot flags with codes coloured by state.</summary>
    public sealed class DigSitePlan : VisualElement
    {
        readonly DigSiteMap _map;
        readonly List<Label> _labels = new List<Label>();

        public static Color ColorFor(DigSpotState s) => s switch
        {
            DigSpotState.Flagged => new Color(0.95f, 0.2f, 0.15f),
            DigSpotState.Digging => XenoarchPalette.Primary,
            DigSpotState.Unfinished => new Color(1f, 0.45f, 0.35f),
            _ => new Color(0.4f, 1f, 0.55f),
        };

        public DigSitePlan(DigSiteMap map)
        {
            _map = map;
            style.backgroundColor = XenoarchPalette.PanelDeep;
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1;
            style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = XenoarchPalette.A(XenoarchPalette.Secondary, 0.5f);
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        Rect Area()
        {
            var r = contentRect; float pad = 24f;
            float s = Mathf.Min((r.width - pad * 2) / Mathf.Max(1f, _map.sizeX), (r.height - pad * 2) / Mathf.Max(1f, _map.sizeZ));
            float w = _map.sizeX * s, h = _map.sizeZ * s;
            return new Rect((r.width - w) * 0.5f, (r.height - h) * 0.5f, w, h);
        }

        Vector2 ToPx(float lx, float lz) { var a = Area(); return new Vector2(a.x + lx / Mathf.Max(0.01f, _map.sizeX) * a.width, a.yMax - lz / Mathf.Max(0.01f, _map.sizeZ) * a.height); }

        void Layout()
        {
            int i = 0;
            foreach (var s in _map.spots)
            {
                if (i >= _labels.Count) { var l = new Label { pickingMode = PickingMode.Ignore }; l.style.position = Position.Absolute; l.style.fontSize = 13; _labels.Add(l); Add(l); }
                var lbl = _labels[i++]; var p = ToPx(s.lx, s.lz);
                lbl.text = _map.SpotCode(s) + (s.State == DigSpotState.Unfinished ? " unfinished" : "");
                lbl.style.left = p.x + 8; lbl.style.top = p.y - 22; lbl.style.color = ColorFor(s.State);
            }
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D; var a = Area();
            if (a.width < 4f) return;
            p.lineWidth = 1f; p.strokeColor = XenoarchPalette.A(XenoarchPalette.Secondary, 0.18f);
            for (float x = 1f; x < _map.sizeX; x += 1f) { var q = ToPx(x, 0); p.BeginPath(); p.MoveTo(new Vector2(q.x, a.yMin)); p.LineTo(new Vector2(q.x, a.yMax)); p.Stroke(); }
            for (float z = 1f; z < _map.sizeZ; z += 1f) { var q = ToPx(0, z); p.BeginPath(); p.MoveTo(new Vector2(a.xMin, q.y)); p.LineTo(new Vector2(a.xMax, q.y)); p.Stroke(); }
            // fence
            p.lineWidth = 3f; p.strokeColor = new Color(1f, 0.45f, 0.1f);
            p.BeginPath(); p.MoveTo(new Vector2(a.xMin, a.yMin)); p.LineTo(new Vector2(a.xMax, a.yMin)); p.LineTo(new Vector2(a.xMax, a.yMax)); p.LineTo(new Vector2(a.xMin, a.yMax)); p.ClosePath(); p.Stroke();
            p.fillColor = new Color(0.86f, 0.8f, 0.68f);
            float step = 2.5f / Mathf.Max(0.01f, _map.sizeX) * a.width;
            if (step > 4f)
                for (float t = 0; t <= a.width + 0.1f; t += step) { Post(p, new Vector2(a.xMin + t, a.yMin)); Post(p, new Vector2(a.xMin + t, a.yMax)); }
            // spots
            foreach (var s in _map.spots)
            {
                var c = ToPx(s.lx, s.lz); var col = ColorFor(s.State);
                if (s.State == DigSpotState.Done)
                {
                    p.strokeColor = col; p.lineWidth = 2.5f; p.BeginPath(); p.MoveTo(c + new Vector2(-5, 0)); p.LineTo(c + new Vector2(-1, 5)); p.LineTo(c + new Vector2(6, -6)); p.Stroke();
                    continue;
                }
                p.strokeColor = Color.white; p.lineWidth = 1.5f; p.BeginPath(); p.MoveTo(c); p.LineTo(c + new Vector2(0, -16)); p.Stroke();
                p.fillColor = col; p.BeginPath(); p.MoveTo(c + new Vector2(0, -16)); p.LineTo(c + new Vector2(11, -12)); p.LineTo(c + new Vector2(0, -8)); p.ClosePath(); p.Fill();
                if (s.State == DigSpotState.Unfinished || s.State == DigSpotState.Digging) { p.strokeColor = col; p.lineWidth = 1.5f; p.BeginPath(); p.Arc(c, 9f, 0f, 360f); p.Stroke(); }
            }
        }

        static void Post(Painter2D p, Vector2 c) { p.BeginPath(); p.Arc(c, 2.5f, 0f, 360f); p.Fill(); }
    }
}
