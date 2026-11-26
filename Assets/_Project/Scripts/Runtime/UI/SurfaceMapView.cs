using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Top-down terrain view of a landing region (baked relief map, <c>CelestialBodyDefinition.surfaceMap</c>) with
    /// markers: dig-site signatures (red triangles + uncertainty circle), landing sites (green rings), base zone, and an
    /// optional scan sweep. Click a marker to select it. Square; world x/z -> map via <see cref="WorldRect"/>.
    /// </summary>
    public sealed class SurfaceMapView : VisualElement
    {
        public enum Kind { Site, Landing, Base, Shuttle, Player }
        public struct Marker { public string id, label; public Kind kind; public Vector2 world; public float uncertainty; public bool selected; }

        public readonly List<Marker> Markers = new List<Marker>();
        public Rect WorldRect;
        /// <summary>0..1 while a scan sweeps the map, negative when idle.</summary>
        public float Sweep = -1f;
        public event Action<string> Clicked;
        readonly List<Label> _labels = new List<Label>();

        public static readonly Color SiteRed = new Color(0.95f, 0.2f, 0.15f), LandingGreen = new Color(0.35f, 1f, 0.55f), Amber = new Color(0.95f, 0.66f, 0.22f);

        public SurfaceMapView(Texture2D map, Rect worldRect)
        {
            WorldRect = worldRect.width > 1f ? worldRect : new Rect(-150f, -150f, 300f, 300f);
            style.backgroundColor = new Color(0.03f, 0.04f, 0.05f, 0.9f);
            if (map != null) { style.backgroundImage = map; style.unityBackgroundScaleMode = ScaleMode.StretchToFill; }
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1;
            style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = new Color(0.67f, 0.8f, 0.94f, 0.2f);
            style.marginBottom = 8; style.overflow = Overflow.Hidden; style.flexShrink = 0;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(e =>
            {
                float w = resolvedStyle.width;
                if (!float.IsNaN(w) && w > 10f && Mathf.Abs(resolvedStyle.height - w) > 1f) style.height = Mathf.Min(w, 340f);
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
                if (string.IsNullOrEmpty(m.label) || (m.kind == Kind.Landing && !m.selected)) continue;
                if (li >= _labels.Count) { var l = new Label { pickingMode = PickingMode.Ignore }; l.AddToClassList("xa-map-label"); _labels.Add(l); Add(l); }
                var lbl = _labels[li++]; var p = ToPx(m.world);
                lbl.text = m.label; lbl.style.left = p.x + 9f; lbl.style.top = p.y - 18f;
                lbl.style.color = m.selected ? Amber : m.kind == Kind.Site ? new Color(1f, 0.65f, 0.6f) : new Color(0.85f, 0.95f, 0.9f);
                lbl.style.display = DisplayStyle.Flex;
            }
            for (int i = li; i < _labels.Count; i++) _labels[i].style.display = DisplayStyle.None;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D; var r = contentRect;
            if (r.width < 4f) return;
            // grid every 50 m
            p.lineWidth = 1f; p.strokeColor = new Color(0.7f, 0.85f, 1f, 0.08f);
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
                    p.strokeColor = new Color(0.4f, 0.9f, 1f, 0.5f * (1f - i / 12f)); p.lineWidth = 2f;
                    p.BeginPath(); p.MoveTo(new Vector2(x - i * 3f, 0)); p.LineTo(new Vector2(x - i * 3f, r.height)); p.Stroke();
                }
                p.lineWidth = 1f;
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
                        p.fillColor = SiteRed;
                        p.BeginPath(); p.MoveTo(c + new Vector2(0, -8)); p.LineTo(c + new Vector2(7, 5)); p.LineTo(c + new Vector2(-7, 5)); p.ClosePath(); p.Fill();
                        if (m.selected) { p.strokeColor = Amber; p.lineWidth = 2f; p.BeginPath(); p.MoveTo(c + new Vector2(0, -11)); p.LineTo(c + new Vector2(10, 7)); p.LineTo(c + new Vector2(-10, 7)); p.ClosePath(); p.Stroke(); }
                        break;
                    case Kind.Landing:
                    case Kind.Base:
                        p.strokeColor = m.selected ? Amber : m.kind == Kind.Base ? new Color(0.85f, 0.95f, 1f, 0.8f) : LandingGreen;
                        p.lineWidth = m.selected ? 3f : 2f;
                        p.BeginPath(); p.Arc(c, 6f, 0f, 360f); p.Stroke();
                        p.BeginPath(); p.MoveTo(c + new Vector2(-3, -3)); p.LineTo(c + new Vector2(-3, 3)); p.MoveTo(c + new Vector2(3, -3)); p.LineTo(c + new Vector2(3, 3)); p.MoveTo(c + new Vector2(-3, 0)); p.LineTo(c + new Vector2(3, 0)); p.Stroke();
                        break;
                    case Kind.Shuttle:
                        p.fillColor = LandingGreen; p.BeginPath(); p.MoveTo(c + new Vector2(0, -7)); p.LineTo(c + new Vector2(5, 6)); p.LineTo(c + new Vector2(-5, 6)); p.ClosePath(); p.Fill();
                        break;
                    case Kind.Player:
                        p.fillColor = Amber; p.BeginPath(); p.Arc(c, 4f, 0f, 360f); p.Fill();
                        break;
                }
            }
            p.lineWidth = 1f;
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
