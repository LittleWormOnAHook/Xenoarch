using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Top-centre compass strip (approved mockup 01_compass_strip, t408s1998). Sits in the dark band at the top of the
    /// helmet mask and renders above it (own UIDocument, sortingOrder 20 vs the helmet's 5), at a fixed spot whether the
    /// visor is up or not. Heading ticks and cardinals follow the view camera; markers are circles per type (site, cache,
    /// POI, shuttle, rover, waypoint) and a green SQUARE for the focused objective. Markers grow as you get close, off-arc
    /// ones pin to the strip ends. Every element is PickingMode.Ignore (pickable overlays caused earlier click bugs).
    /// Data comes from <see cref="CompassSource"/>. Strip, ticks and marker elements are built once and only restyled.
    /// </summary>
    public sealed class CompassHud : MonoBehaviour
    {
        const float StripW = 820f, StripH = 62f, StripTop = 6f, FovDeg = 140f;
        const float TickBase = 34f, MarkY = 45f, EndPad = 14f, OffArcAlpha = 0.6f;
        const int MaxViews = 24;
        const float InnerW = StripW - 4f;   // minus the 2 px frame border each side
        const float Cx = InnerW * 0.5f;

        static readonly Color Dark = new Color(40f / 255f, 9f / 255f, 14f / 255f, 0.85f);

        sealed class MarkView { public VisualElement el, inner, ring; public int kind = -1; }
        struct Item { public CompassMark m; public float dist, delta; public bool off; }

        UIDocument _doc;
        VisualElement _root, _strip, _labelHost;
        Label _heading, _objLabel, _nearLabel;
        readonly VisualElement[] _ticks = new VisualElement[24];
        readonly Label[] _cards = new Label[8];
        readonly MarkView[] _views = new MarkView[MaxViews];
        readonly List<Item> _items = new List<Item>(32);
        int _lastHeading = -1;
        bool _built;

        static readonly System.Comparison<Item> FarFirst = (a, b) => b.dist.CompareTo(a.dist);

        void Start()
        {
            var go = new GameObject("[Compass HUD]");
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 20;   // above the helmet mask (5), detector (12) and objective tracker (18); below the minimap (40) and windows
        }

        // ------------------------------------------------------------------ build
        static VisualElement E() => new VisualElement { pickingMode = PickingMode.Ignore };

        static Label L(string text, float size, Color c, FontStyle style = FontStyle.Normal)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size; l.style.color = c; l.style.unityFontStyleAndWeight = style;
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 0f, color = new Color(0f, 0f, 0f, 0.8f) };
            return l;
        }

        static void Round(VisualElement e, StyleLength r)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
        }

        static void Border(VisualElement e, float w, Color c)
        {
            e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = w;
            e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = c;
        }

        void Build()
        {
            var r = _doc != null ? _doc.rootVisualElement : null;
            if (r == null) return;
            XenoarchUI.FillRoot(r); r.pickingMode = PickingMode.Ignore;
            _root = E();
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.top = 0; _root.style.right = 0; _root.style.bottom = 0;
            r.Add(_root);

            // glass strip: deep crimson, frame border, orange top rule (same chrome as the objective tracker / terminals)
            _strip = E();
            _strip.style.position = Position.Absolute;
            _strip.style.left = Length.Percent(50); _strip.style.marginLeft = -StripW * 0.5f;
            _strip.style.top = StripTop; _strip.style.width = StripW; _strip.style.height = StripH;
            _strip.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDeep, 0.92f);
            Border(_strip, 2f, XenoarchPalette.Frame);
            _strip.style.borderTopWidth = 3f; _strip.style.borderTopColor = XenoarchPalette.Heading;
            Round(_strip, 4f);
            _strip.style.overflow = Overflow.Hidden;
            _root.Add(_strip);

            for (int i = 0; i < _ticks.Length; i++)
            {
                int ang = i * 15;
                bool card = ang % 90 == 0, inter = !card && ang % 45 == 0;
                var t = E();
                t.style.position = Position.Absolute;
                float h = card ? 12f : inter ? 9f : 6f, w = card ? 2f : 1f;
                t.style.width = w; t.style.height = h; t.style.top = TickBase - h;
                t.style.backgroundColor = card ? (ang == 0 ? XenoarchPalette.Heading : XenoarchPalette.Text) : XenoarchPalette.A(XenoarchPalette.Text, inter ? 0.8f : 0.55f);
                _strip.Add(t); _ticks[i] = t;
                if (card || inter)
                {
                    string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
                    var lab = L(names[ang / 45], card ? (ang == 0 ? 15f : 13f) : 11f, ang == 0 ? XenoarchPalette.Heading : XenoarchPalette.Text, card ? FontStyle.Bold : FontStyle.Normal);
                    lab.style.position = Position.Absolute; lab.style.width = 28; lab.style.unityTextAlign = TextAnchor.LowerCenter;
                    lab.style.top = TickBase - h - 19f; lab.style.height = 17f;
                    _strip.Add(lab); _cards[ang / 45] = lab;
                }
            }

            // centre pointer
            var ptr = E();
            ptr.style.position = Position.Absolute; ptr.style.left = Cx - 8f; ptr.style.top = 0f; ptr.style.width = 16f; ptr.style.height = 10f;
            ptr.generateVisualContent += ctx =>
            {
                var p = ctx.painter2D; var rc = ctx.visualElement.contentRect;
                p.fillColor = XenoarchPalette.Heading; p.BeginPath();
                p.MoveTo(new Vector2(0f, 0f)); p.LineTo(new Vector2(rc.width, 0f)); p.LineTo(new Vector2(rc.width * 0.5f, rc.height)); p.ClosePath(); p.Fill();
            };
            _strip.Add(ptr);

            for (int i = 0; i < MaxViews; i++)
            {
                var v = new MarkView { el = E(), inner = E(), ring = E() };
                v.el.style.position = Position.Absolute; v.el.style.display = DisplayStyle.None;
                v.inner.style.position = Position.Absolute;
                v.inner.style.left = Length.Percent(32); v.inner.style.top = Length.Percent(32);
                v.inner.style.width = Length.Percent(36); v.inner.style.height = Length.Percent(36);
                Round(v.inner, Length.Percent(50));
                v.ring.style.position = Position.Absolute;
                v.ring.style.left = -5; v.ring.style.top = -5; v.ring.style.right = -5; v.ring.style.bottom = -5;
                Border(v.ring, 2f, CompassConfig.Objective); v.ring.style.display = DisplayStyle.None;
                v.el.Add(v.inner); v.el.Add(v.ring);
                _strip.Add(v.el); _views[i] = v;
            }

            // distance labels + heading sit just under the strip (outside its clipping)
            _labelHost = E();
            _labelHost.style.position = Position.Absolute;
            _labelHost.style.left = Length.Percent(50); _labelHost.style.marginLeft = -StripW * 0.5f;
            _labelHost.style.top = StripTop; _labelHost.style.width = StripW; _labelHost.style.height = StripH + 40f;
            _root.Add(_labelHost);
            _heading = L("000", 16f, XenoarchPalette.Heading, FontStyle.Bold);
            _heading.style.position = Position.Absolute; _heading.style.left = 2f + Cx - 30f; _heading.style.width = 60f; _heading.style.top = StripH + 4f;
            _heading.style.unityTextAlign = TextAnchor.UpperCenter;
            _labelHost.Add(_heading);
            _objLabel = NewDistLabel(); _nearLabel = NewDistLabel();

            // legend
            var legend = E();
            legend.style.position = Position.Absolute; legend.style.left = 0; legend.style.right = 0; legend.style.top = StripTop + StripH + 30f;
            legend.style.flexDirection = FlexDirection.Row; legend.style.justifyContent = Justify.Center; legend.style.alignItems = Align.Center;
            CompassKind[] order = { CompassKind.Site, CompassKind.Cache, CompassKind.Poi, CompassKind.Shuttle, CompassKind.Rover, CompassKind.Waypoint, CompassKind.Objective };
            foreach (var k in order)
            {
                var sw = E(); sw.style.width = 10; sw.style.height = 10; sw.style.marginLeft = 9; sw.style.marginRight = 4;
                sw.style.backgroundColor = CompassConfig.ColorOf(k); Border(sw, 1f, Dark);
                if (k != CompassKind.Objective) Round(sw, Length.Percent(50));
                legend.Add(sw);
                legend.Add(L(CompassConfig.NameOf(k), 11f, XenoarchPalette.A(XenoarchPalette.Text, 0.9f), FontStyle.Bold));
            }
            _root.Add(legend);
            _built = true;
        }

        Label NewDistLabel()
        {
            var l = L("", 11f, XenoarchPalette.Text, FontStyle.Bold);
            l.style.position = Position.Absolute; l.style.width = 60f; l.style.top = StripH + 2f; l.style.unityTextAlign = TextAnchor.UpperCenter;
            l.style.display = DisplayStyle.None;
            _labelHost.Add(l);
            return l;
        }

        static void Restyle(MarkView v, CompassKind k)
        {
            var c = CompassConfig.ColorOf(k);
            bool obj = k == CompassKind.Objective;
            v.el.style.backgroundColor = c;
            Border(v.el, 2f, Dark);
            Round(v.el, obj ? (StyleLength)0f : Length.Percent(50));
            v.inner.style.backgroundColor = obj ? new Color(40f / 255f, 9f / 255f, 14f / 255f, 0.65f) : new Color(1f, 1f, 1f, 0.28f);
            Round(v.inner, obj ? (StyleLength)0f : Length.Percent(50));
            v.ring.style.display = obj ? DisplayStyle.Flex : DisplayStyle.None;
            v.kind = (int)k;
        }

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (_doc == null) return;
            if (_built && (_root == null || _root.panel == null)) { _built = false; _root?.RemoveFromHierarchy(); }   // detached: rebuild
            if (!_built) Build();
            if (!_built) return;

            CompassSource.Refresh();
            Vector3 pos = default; float yaw = 0f;
            bool show = CompassSource.HudVisible() && CompassSource.OnSurface && CompassSource.View(out pos, out yaw);
            XenoarchUI.Show(_root, show);
            if (!show) return;

            float halfFov = FovDeg * 0.5f;
            UpdateTicks(yaw, halfFov);
            int hd = Mathf.RoundToInt((yaw % 360f + 360f) % 360f) % 360;
            if (hd != _lastHeading) { _lastHeading = hd; _heading.text = hd.ToString("000"); }

            // gather in-range marks, far first so near ones draw on top
            _items.Clear();
            var marks = CompassSource.Marks;
            for (int i = 0; i < marks.Count; i++)
            {
                var m = marks[i];
                float dist = CompassSource.Flat(pos, m.pos);
                bool always = m.kind == CompassKind.Objective || m.kind == CompassKind.Shuttle || m.kind == CompassKind.Rover;
                if (!always && dist > CompassConfig.MaxMarkerDistance) continue;
                if ((m.kind == CompassKind.Rover || m.kind == CompassKind.Shuttle) && dist < 3f) continue;   // standing at / in it
                var bearing = Mathf.Atan2(m.pos.x - pos.x, m.pos.z - pos.z) * Mathf.Rad2Deg;
                float delta = Mathf.DeltaAngle(yaw, bearing);
                _items.Add(new Item { m = m, dist = dist, delta = delta, off = Mathf.Abs(delta) > halfFov });
            }
            _items.Sort(FarFirst);
            while (_items.Count > MaxViews)
            {
                int drop = 0;   // farthest non-priority item (list is far first)
                while (drop < _items.Count - 1 && _items[drop].m.kind == CompassKind.Objective) drop++;
                _items.RemoveAt(drop);
            }

            int nearest = -1; float nearestD = float.MaxValue; int obj = -1;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                if (it.m.kind == CompassKind.Objective) obj = i;
                else if (!it.off && it.dist < nearestD) { nearestD = it.dist; nearest = i; }
            }

            float halfW = Cx - EndPad;
            for (int i = 0; i < MaxViews; i++)
            {
                var v = _views[i];
                if (i >= _items.Count) { v.el.style.display = DisplayStyle.None; continue; }
                var it = _items[i];
                if (v.kind != (int)it.m.kind) Restyle(v, it.m.kind);
                bool isObj = it.m.kind == CompassKind.Objective;
                float size = CompassConfig.SizeAt(it.dist, isObj);
                float d = Mathf.Clamp(it.delta, -halfFov, halfFov);
                float x = Mathf.Clamp(d / halfFov * Cx, -halfW, halfW);
                v.el.style.display = DisplayStyle.Flex;
                v.el.style.width = size; v.el.style.height = size;
                v.el.style.left = Cx + x - size * 0.5f; v.el.style.top = MarkY - size * 0.5f;
                v.el.style.opacity = it.off ? OffArcAlpha : 1f;
            }

            SetDist(_objLabel, obj >= 0 ? _items[obj] : (Item?)null, obj >= 0 ? _items[obj].off : false, halfFov, halfW);
            SetDist(_nearLabel, nearest >= 0 ? _items[nearest] : (Item?)null, false, halfFov, halfW);
        }

        void SetDist(Label l, Item? it, bool off, float halfFov, float halfW)
        {
            if (!it.HasValue) { l.style.display = DisplayStyle.None; return; }
            var m = it.Value;
            float x = Mathf.Clamp(Mathf.Clamp(m.delta, -halfFov, halfFov) / halfFov * Cx, -halfW, halfW);
            if (Mathf.Abs(x) < 34f) x += 40f;   // keep clear of the centred heading readout
            l.style.display = DisplayStyle.Flex;
            l.style.left = 2f + Cx + x - 30f;
            l.text = $"{Mathf.RoundToInt(m.dist)} m";
            l.style.color = CompassConfig.ColorOf(m.m.kind);
            l.style.opacity = off ? OffArcAlpha : 1f;
        }

        void UpdateTicks(float yaw, float halfFov)
        {
            for (int i = 0; i < _ticks.Length; i++)
            {
                float delta = Mathf.DeltaAngle(yaw, i * 15f);
                var t = _ticks[i];
                bool vis = Mathf.Abs(delta) <= halfFov;
                t.style.display = vis ? DisplayStyle.Flex : DisplayStyle.None;
                Label lab = (i * 15) % 45 == 0 ? _cards[(i * 15) / 45] : null;
                if (lab != null) lab.style.display = vis ? DisplayStyle.Flex : DisplayStyle.None;
                if (!vis) continue;
                float x = delta / halfFov * Cx;
                t.style.left = Cx + x - ((i * 15) % 90 == 0 ? 1f : 0.5f);
                if (lab != null) lab.style.left = Cx + x - 14f;
            }
        }
    }
}