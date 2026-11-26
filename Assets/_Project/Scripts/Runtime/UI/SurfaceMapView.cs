using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Top-down terrain view of a landing region (baked relief map, <c>CelestialBodyDefinition.surfaceMap</c>) with
    /// markers: dig-site signatures (red target glyph + uncertainty circle), landing sites (green arrow), base zone (home), caches (star), and an
    /// optional scan sweep. Click a marker to select it. Square; world x/z -> map via <see cref="WorldRect"/>.
    /// </summary>
    public sealed class SurfaceMapView : VisualElement
    {
        public enum Kind { Site, Landing, Base, Shuttle, Player, Cache }
        public struct Marker { public string id, label; public Kind kind; public Vector2 world; public float uncertainty, heading; public bool selected; }

        public readonly List<Marker> Markers = new List<Marker>();
        public Rect WorldRect;
        /// <summary>0..1 while a scan sweeps the map, negative when idle.</summary>
        public float Sweep = -1f;
        /// <summary>Keep the element square (terminal maps). The minimap sizes itself.</summary>
        public bool AutoSquare = true;
        public bool ShowLabels = true;
        /// <summary>Scan radius ring (world centre x/z, radius m); radius 0 = none.</summary>
        public Vector2 ScanCentre; public float ScanRadius;
        public event Action<string> Clicked;
        readonly List<Label> _labels = new List<Label>();
        // Site / cache / base / landing markers are Kenney glyph elements (t155u icon style, classes in Xenoarch.uss).
        readonly List<VisualElement> _icons = new List<VisualElement>(), _halos = new List<VisualElement>();
        int _iconSig; bool _relayoutQueued;

        public static readonly Color SiteRed = new Color(0.95f, 0.2f, 0.15f), LandingGreen = new Color(0.35f, 1f, 0.55f), Amber = XenoarchPalette.Primary;

        public SurfaceMapView(Texture2D map, Rect worldRect)
        {
            WorldRect = worldRect.width > 1f ? worldRect : new Rect(-150f, -150f, 300f, 300f);
            style.backgroundColor = XenoarchPalette.PanelDeep;
            if (map != null) { style.backgroundImage = map; style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100)); style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat); }
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1;
            style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = XenoarchPalette.A(XenoarchPalette.Secondary, 0.5f);
            style.marginBottom = 8; style.overflow = Overflow.Hidden; style.flexShrink = 0;
            XenoarchIcons.EnsureStyles(this);
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(e =>
            {
                float w = resolvedStyle.width;
                if (AutoSquare && !float.IsNaN(w) && w > 10f && Mathf.Abs(resolvedStyle.height - w) > 1f) style.height = Mathf.Min(w, 340f);
                Relayout();
            });
            RegisterCallback<PointerUpEvent>(OnClick);
        }

        /// <summary>World x/z to normalised map coordinates (0..1, y down).</summary>
        public static Vector2 ToMap01(Rect worldRect, Vector2 world) =>
            new Vector2((world.x - worldRect.xMin) / worldRect.width, 1f - (world.y - worldRect.yMin) / worldRect.height);

        Vector2 ToPx(Vector2 world) { var n = ToMap01(WorldRect, world); return new Vector2(n.x * contentRect.width, n.y * contentRect.height); }
        float MetresToPx => contentRect.width / Mathf.Max(1f, WorldRect.width);

        public void Rebuild() { Relayout(); MarkDirtyRepaint(); }

        void Relayout()
        {
            int li = 0;
            foreach (var m in Markers)
            {
                if (!ShowLabels || string.IsNullOrEmpty(m.label) || (m.kind == Kind.Landing && !m.selected)) continue;
                if (li >= _labels.Count) { var l = new Label { pickingMode = PickingMode.Ignore }; l.AddToClassList("xa-map-label"); _labels.Add(l); Add(l); }
                var lbl = _labels[li++]; var p = ToPx(m.world);
                lbl.text = m.label; lbl.style.left = p.x + 9f; lbl.style.top = p.y - 18f;
                lbl.style.color = m.selected ? Amber : m.kind == Kind.Site ? new Color(1f, 0.65f, 0.6f) : XenoarchPalette.Text;
                lbl.style.display = DisplayStyle.Flex;
            }
            for (int i = li; i < _labels.Count; i++) _labels[i].style.display = DisplayStyle.None;

            int ii = 0;
            foreach (var m in Markers)
            {
                string glyph = Glyph(m.kind); if (glyph == null) continue;
                if (ii >= _icons.Count)
                {
                    var h = new VisualElement { pickingMode = PickingMode.Ignore }; var ic = new VisualElement { pickingMode = PickingMode.Ignore };
                    Insert(0, ic); Insert(0, h); _halos.Add(h); _icons.Add(ic); // under the labels
                }
                var halo = _halos[ii]; var icon = _icons[ii]; ii++;
                var p = ToPx(m.world);
                halo.ClearClassList(); halo.AddToClassList("xa-icon"); halo.AddToClassList("xa-icon--" + glyph); halo.AddToClassList("xa-map-icon"); halo.AddToClassList("xa-map-icon__halo");
                icon.ClearClassList(); icon.AddToClassList("xa-icon"); icon.AddToClassList("xa-icon--" + glyph); icon.AddToClassList("xa-map-icon"); icon.AddToClassList("xa-map-icon--" + m.kind.ToString().ToLowerInvariant());
                halo.style.left = p.x - 11f; halo.style.top = p.y - 11f;
                icon.style.left = p.x - 9f; icon.style.top = p.y - 9f;
                halo.style.display = icon.style.display = DisplayStyle.Flex;
            }
            for (int i = ii; i < _icons.Count; i++) _icons[i].style.display = _halos[i].style.display = DisplayStyle.None;
            _iconSig = IconSignature();
        }

        static string Glyph(Kind k) => k == Kind.Site ? "target" : k == Kind.Cache ? "star" : k == Kind.Base ? "home" : k == Kind.Landing ? "arrowDown" : null;

        int IconSignature()
        {
            unchecked
            {
                int h = 17 + (int)contentRect.width * 31;
                foreach (var m in Markers) if (Glyph(m.kind) != null) h = h * 31 + m.kind.GetHashCode() * 7 + m.world.GetHashCode();
                return h;
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D; var r = contentRect;
            if (r.width < 4f) return;
            // grid every 50 m
            p.lineWidth = 1f; p.strokeColor = XenoarchPalette.A(XenoarchPalette.Secondary, 0.15f);
            float step = 50f * MetresToPx;
            if (step > 6f)
            {
                float x0 = (Mathf.Ceil(WorldRect.xMin / 50f) * 50f - WorldRect.xMin) * MetresToPx;
                for (float x = x0; x < r.width; x += step) { p.BeginPath(); p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, r.height)); p.Stroke(); }
                float y0 = (WorldRect.yMax - Mathf.Floor(WorldRect.yMax / 50f) * 50f) * MetresToPx;
                for (float y = y0; y < r.height; y += step) { p.BeginPath(); p.MoveTo(new Vector2(0, y)); p.LineTo(new Vector2(r.width, y)); p.Stroke(); }
            }
            if (Sweep >= 0f)
            {
                float x = Mathf.Clamp01(Sweep) * r.width;
                for (int i = 0; i < 12; i++)
                {
                    p.strokeColor = XenoarchPalette.A(XenoarchPalette.Highlight, 0.5f * (1f - i / 12f)); p.lineWidth = 2f;
                    p.BeginPath(); p.MoveTo(new Vector2(x - i * 3f, 0)); p.LineTo(new Vector2(x - i * 3f, r.height)); p.Stroke();
                }
                p.lineWidth = 1f;
            }
            if (ScanRadius > 0.5f)
            {
                p.strokeColor = XenoarchPalette.A(XenoarchPalette.Highlight, 0.55f); p.lineWidth = 1.5f;
                p.BeginPath(); p.Arc(ToPx(ScanCentre), ScanRadius * MetresToPx, 0f, 360f); p.Stroke();
                p.fillColor = XenoarchPalette.A(XenoarchPalette.Highlight, 0.06f); p.BeginPath(); p.Arc(ToPx(ScanCentre), ScanRadius * MetresToPx, 0f, 360f); p.Fill();
            }
            foreach (var m in Markers)
            {
                var c = ToPx(m.world);
                switch (m.kind)
                {
                    case Kind.Site:
                        if (m.uncertainty > 0.5f)
                        {
                            p.strokeColor = new Color(SiteRed.r, SiteRed.g, SiteRed.b, 0.45f); p.lineWidth = 1f;
                            p.BeginPath(); p.Arc(c, m.uncertainty * MetresToPx, 0f, 360f); p.Stroke();
                        }
                        if (m.selected) { p.strokeColor = Amber; p.lineWidth = 2f; p.BeginPath(); p.Arc(c, 12f, 0f, 360f); p.Stroke(); }
                        break;
                    case Kind.Landing:
                    case Kind.Base:
                        if (m.selected) { p.strokeColor = Amber; p.lineWidth = 2f; p.BeginPath(); p.Arc(c, 12f, 0f, 360f); p.Stroke(); }
                        break;
                    case Kind.Shuttle:
                        p.fillColor = LandingGreen; p.BeginPath(); p.MoveTo(c + new Vector2(0, -7)); p.LineTo(c + new Vector2(5, 6)); p.LineTo(c + new Vector2(-5, 6)); p.ClosePath(); p.Fill();
                        break;
                    case Kind.Player:
                    {
                        float a = m.heading * Mathf.Deg2Rad; var f = new Vector2(Mathf.Sin(a), -Mathf.Cos(a)); var rt = new Vector2(-f.y, f.x);
                        p.fillColor = Amber; p.BeginPath(); p.MoveTo(c + f * 8f); p.LineTo(c - f * 5f + rt * 5f); p.LineTo(c - f * 2f); p.LineTo(c - f * 5f - rt * 5f); p.ClosePath(); p.Fill();
                        break;
                    }
                    case Kind.Cache:
                        if (m.selected) { p.strokeColor = new Color(0.4f, 1f, 0.55f); p.lineWidth = 1.5f; p.BeginPath(); p.Arc(c, 12f, 0f, 360f); p.Stroke(); }
                        break;
                }
            }
            p.lineWidth = 1f;
            // Callers that only repaint after changing markers: move the glyph elements on the next frame.
            if (!_relayoutQueued && IconSignature() != _iconSig) { _relayoutQueued = true; schedule.Execute(() => { _relayoutQueued = false; Relayout(); }); }
        }

        void OnClick(PointerUpEvent e)
        {
            if (e.button != 0) return;
            var pos = (Vector2)e.localPosition;
            string best = null; float bd = 14f;
            foreach (var m in Markers)
            {
                if (m.kind == Kind.Player || m.kind == Kind.Shuttle) continue;
                float d = (ToPx(m.world) - pos).magnitude;
                if (d < bd) { bd = d; best = m.id; }
            }
            if (best != null) Clicked?.Invoke(best);
        }
    }
}
