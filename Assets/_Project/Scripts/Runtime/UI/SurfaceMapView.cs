using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Top-down terrain view of a landing region (baked relief map, <c>CelestialBodyDefinition.surfaceMap</c>) with
    /// markers: dig-site signatures (red target glyph + uncertainty circle), landing sites (green arrow), base zone (home), caches (star), and an
    /// optional scan sweep. Click a marker to select it. Square; world x/z -> map via the current zoomed view of <see cref="WorldRect"/>.
    /// When <see cref="Interactive"/>, wheel zooms toward the cursor, LMB/MMB drag pans, WASD/arrows pan, and R resets (t299u).
    /// Markers stay a constant screen size while zooming. View state lives in <see cref="ViewState"/> so it survives window rebuilds.
    /// </summary>
    public sealed class SurfaceMapView : VisualElement
    {
        public enum Kind { Site, Landing, Base, Shuttle, Player, Cache, Waypoint }
        public struct Marker { public string id, label; public Kind kind; public Vector2 world; public float uncertainty, heading; public bool selected; }

        public sealed class ViewState
        {
            public float zoom = 1f;
            /// <summary>World x/z offset of the view centre from <c>WorldRect.center</c>.</summary>
            public Vector2 pan;
            internal float targetZoom = -1f;
            internal Vector2 zoomAnchor;
            public void Reset() { zoom = 1f; pan = Vector2.zero; targetZoom = -1f; }
        }

        public readonly List<Marker> Markers = new List<Marker>();
        public Rect WorldRect;
        public ViewState State;
        /// <summary>0..1 while a scan sweeps the map, negative when idle.</summary>
        public float Sweep = -1f;
        /// <summary>Keep the element square (terminal maps). The minimap sizes itself.</summary>
        public bool AutoSquare = true;
        public bool ShowLabels = true;
        /// <summary>False on the HUD minimap: no zoom, pan, or click.</summary>
        public bool Interactive = true;
        /// <summary>Scan radius ring (world centre x/z, radius m); radius 0 = none.</summary>
        public Vector2 ScanCentre; public float ScanRadius;
        public event Action<string> Clicked;
        /// <summary>Right-click on empty map: world x/z under the cursor.</summary>
        public event Action<Vector2> Dropped;
        readonly List<Label> _labels = new List<Label>();
        // Site / cache / base / landing markers are Kenney glyph elements (t155u icon style, classes in Xenoarch.uss).
        readonly List<VisualElement> _icons = new List<VisualElement>(), _halos = new List<VisualElement>();
        int _iconSig; bool _relayoutQueued;
        VisualElement _ground;
        Vector2 _downPos, _lastPos; int _button = -1; bool _dragged;

        public static readonly Color SiteRed = new Color(0.95f, 0.2f, 0.15f), LandingGreen = new Color(0.35f, 1f, 0.55f), Amber = XenoarchPalette.Primary;
        public const float MinZoom = 0.5f, MaxZoom = 40f;
        const float DragSlop = 6f;

        public SurfaceMapView(Texture2D map, Rect worldRect, ViewState state = null)
        {
            WorldRect = worldRect.width > 1f ? worldRect : new Rect(-150f, -150f, 300f, 300f);
            State = state ?? new ViewState();
            style.backgroundColor = XenoarchPalette.PanelDeep;
            style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = 1;
            style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = XenoarchPalette.A(XenoarchPalette.Secondary, 0.5f);
            style.marginBottom = 8; style.overflow = Overflow.Hidden; style.flexShrink = 0;
            pickingMode = PickingMode.Position;
            focusable = true;
            _ground = new VisualElement { pickingMode = PickingMode.Ignore, name = "map-ground" };
            _ground.style.position = Position.Absolute;
            if (map != null)
            {
                _ground.style.backgroundImage = map;
                _ground.style.backgroundSize = new BackgroundSize(Length.Percent(100), Length.Percent(100));
                _ground.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            }
            Add(_ground);
            XenoarchIcons.EnsureStyles(this);
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(e =>
            {
                float w = resolvedStyle.width;
                if (AutoSquare && !float.IsNaN(w) && w > 10f && Mathf.Abs(resolvedStyle.height - w) > 1f) style.height = Mathf.Min(w, 340f);
                Relayout();
            });
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<ContextClickEvent>(e => e.StopPropagation());
            schedule.Execute(Tick).Every(16);
        }

        /// <summary>The visible slice of <see cref="WorldRect"/> (world x/z).</summary>
        public Rect ViewRect
        {
            get
            {
                float z = Mathf.Max(0.01f, State != null ? State.zoom : 1f);
                float w = WorldRect.width / z, h = WorldRect.height / z;
                var c = WorldRect.center + (State != null ? State.pan : Vector2.zero);
                return new Rect(c.x - w * 0.5f, c.y - h * 0.5f, w, h);
            }
        }

        /// <summary>World x/z to normalised map coordinates (0..1, y down) in the full landing region.</summary>
        public static Vector2 ToMap01(Rect worldRect, Vector2 world) =>
            new Vector2((world.x - worldRect.xMin) / worldRect.width, 1f - (world.y - worldRect.yMin) / worldRect.height);

        Vector2 ToPx(Vector2 world)
        {
            var v = ViewRect;
            float nx = v.width > 0.001f ? (world.x - v.xMin) / v.width : 0f;
            float ny = v.height > 0.001f ? 1f - (world.y - v.yMin) / v.height : 0f;
            return new Vector2(nx * contentRect.width, ny * contentRect.height);
        }

        Vector2 FromPx(Vector2 px)
        {
            var v = ViewRect;
            float w = Mathf.Max(1f, contentRect.width), h = Mathf.Max(1f, contentRect.height);
            return new Vector2(v.xMin + (px.x / w) * v.width, v.yMax - (px.y / h) * v.height);
        }

        float MetresToPx => contentRect.width / Mathf.Max(1f, ViewRect.width);

        public void Rebuild() { Relayout(); MarkDirtyRepaint(); }

        public void ResetView()
        {
            State?.Reset();
            ClampPan();
            Rebuild();
        }

        void ClampPan()
        {
            if (State == null) return;
            float z = Mathf.Max(0.01f, State.zoom);
            if (z <= 1.001f) { State.pan = Vector2.zero; return; }
            float maxX = (WorldRect.width - WorldRect.width / z) * 0.5f;
            float maxY = (WorldRect.height - WorldRect.height / z) * 0.5f;
            State.pan = new Vector2(Mathf.Clamp(State.pan.x, -maxX, maxX), Mathf.Clamp(State.pan.y, -maxY, maxY));
        }

        void Relayout()
        {
            LayoutGround();
            int li = 0;
            foreach (var m in Markers)
            {
                if (!ShowLabels || string.IsNullOrEmpty(m.label) || (m.kind == Kind.Landing && !m.selected)) continue;
                if (li >= _labels.Count) { var l = new Label { pickingMode = PickingMode.Ignore }; l.AddToClassList("xa-map-label"); _labels.Add(l); Add(l); }
                var lbl = _labels[li++]; var p = ToPx(m.world);
                lbl.text = m.label; lbl.style.left = p.x + 9f; lbl.style.top = p.y - 18f;
                lbl.style.color = m.selected ? Amber : m.kind == Kind.Site ? new Color(1f, 0.65f, 0.6f) : XenoarchPalette.Text;
                bool on = contentRect.width < 4f || (p.x > -40f && p.y > -24f && p.x < contentRect.width + 80f && p.y < contentRect.height + 24f);
                lbl.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
            for (int i = li; i < _labels.Count; i++) _labels[i].style.display = DisplayStyle.None;

            int ii = 0;
            foreach (var m in Markers)
            {
                string glyph = Glyph(m.kind); if (glyph == null) continue;
                if (ii >= _icons.Count)
                {
                    var h = new VisualElement { pickingMode = PickingMode.Ignore }; var ic = new VisualElement { pickingMode = PickingMode.Ignore };
                    // After the ground (index 0), under the labels.
                    Insert(1, ic); Insert(1, h); _halos.Add(h); _icons.Add(ic);
                }
                var halo = _halos[ii]; var icon = _icons[ii]; ii++;
                var p = ToPx(m.world);
                halo.ClearClassList(); halo.AddToClassList("xa-icon"); halo.AddToClassList("xa-icon--" + glyph); halo.AddToClassList("xa-map-icon"); halo.AddToClassList("xa-map-icon__halo");
                icon.ClearClassList(); icon.AddToClassList("xa-icon"); icon.AddToClassList("xa-icon--" + glyph); icon.AddToClassList("xa-map-icon"); icon.AddToClassList("xa-map-icon--" + m.kind.ToString().ToLowerInvariant()); if (XenoarchIcons.IsPack(glyph)) icon.AddToClassList("xa-icon--pack");
                halo.style.left = p.x - 11f; halo.style.top = p.y - 11f;
                icon.style.left = p.x - 9f; icon.style.top = p.y - 9f;
                halo.style.display = icon.style.display = DisplayStyle.Flex;
            }
            for (int i = ii; i < _icons.Count; i++) _icons[i].style.display = _halos[i].style.display = DisplayStyle.None;
            _iconSig = IconSignature();
        }

        void LayoutGround()
        {
            if (_ground == null) return;
            float w = contentRect.width, h = contentRect.height;
            if (w < 4f || h < 4f) return;
            float z = Mathf.Max(0.01f, State != null ? State.zoom : 1f);
            _ground.style.width = w * z;
            _ground.style.height = h * z;
            var topLeft = ToPx(new Vector2(WorldRect.xMin, WorldRect.yMax));
            _ground.style.left = topLeft.x;
            _ground.style.top = topLeft.y;
        }

        // ZEN-22: icon library where matched (dig site = anomaly, base = outpost, landing zone); cache has no library icon yet.
        static string Glyph(Kind k) => k == Kind.Site ? "nav-anomaly" : k == Kind.Cache ? "star" : k == Kind.Base ? "nav-base" : k == Kind.Landing ? "nav-landing" : k == Kind.Waypoint ? "nav-waypoint" : null;

        int IconSignature()
        {
            unchecked
            {
                int h = 17 + (int)contentRect.width * 31 + (State != null ? State.zoom.GetHashCode() * 13 + State.pan.GetHashCode() : 0);
                foreach (var m in Markers) if (Glyph(m.kind) != null) h = h * 31 + m.kind.GetHashCode() * 7 + m.world.GetHashCode();
                return h;
            }
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D; var r = contentRect;
            if (r.width < 4f) return;
            var view = ViewRect;
            // grid: 50 m, then 10 m, then 2 m as you zoom in
            float stepM = 50f;
            if (50f * MetresToPx > 160f) stepM = 10f;
            if (10f * MetresToPx > 160f) stepM = 2f;
            p.lineWidth = 1f; p.strokeColor = XenoarchPalette.A(XenoarchPalette.Secondary, 0.15f);
            float step = stepM * MetresToPx;
            if (step > 6f)
            {
                float x0 = (Mathf.Ceil(view.xMin / stepM) * stepM - view.xMin) * MetresToPx;
                for (float x = x0; x < r.width; x += step) { p.BeginPath(); p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, r.height)); p.Stroke(); }
                float y0 = (view.yMax - Mathf.Floor(view.yMax / stepM) * stepM) * MetresToPx;
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
                    case Kind.Waypoint:
                        if (m.selected) { p.strokeColor = XenoarchPalette.Highlight; p.lineWidth = 2f; p.BeginPath(); p.Arc(c, 12f, 0f, 360f); p.Stroke(); }
                        break;
                }
            }
            p.lineWidth = 1f;
            // Callers that only repaint after changing markers: move the glyph elements on the next frame.
            if (!_relayoutQueued && IconSignature() != _iconSig) { _relayoutQueued = true; schedule.Execute(() => { _relayoutQueued = false; Relayout(); }); }
        }

        void Tick()
        {
            AnimateZoom();
            if (!Interactive) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame) { ResetView(); return; }
            var dir = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dir.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dir.y -= 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dir.x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir.x += 1f;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            State.pan += dir * (ViewRect.width * 0.85f * dt);
            ClampPan();
            Rebuild();
        }

        void AnimateZoom()
        {
            if (State == null || State.targetZoom <= 0f) return;
            float z = Mathf.Max(0.01f, State.zoom);
            float next = Mathf.Exp(Mathf.Lerp(Mathf.Log(z), Mathf.Log(State.targetZoom), 0.25f));
            if (Mathf.Abs(Mathf.Log(next / State.targetZoom)) < 0.002f) { next = State.targetZoom; State.targetZoom = -1f; }
            var before = FromPx(State.zoomAnchor);
            State.zoom = next;
            State.pan += before - FromPx(State.zoomAnchor);
            ClampPan();
            Rebuild();
        }

        void OnDown(PointerDownEvent e)
        {
            if (!Interactive || (e.button != 0 && e.button != 1 && e.button != 2)) return;
            _button = e.button; _downPos = _lastPos = (Vector2)e.localPosition; _dragged = false;
            this.CapturePointer(e.pointerId);
            Focus();
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (!Interactive || _button < 0) return;
            var pos = (Vector2)e.localPosition;
            if (!_dragged && (pos - _downPos).sqrMagnitude >= DragSlop * DragSlop) _dragged = true;
            if (_dragged && (_button == 0 || _button == 2))
            {
                State.pan += FromPx(_lastPos) - FromPx(pos);
                ClampPan();
                Rebuild();
            }
            _lastPos = pos;
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (!Interactive || _button < 0) return;
            bool wasDrag = _dragged;
            _button = -1; _dragged = false;
            this.ReleasePointer(e.pointerId);
            e.StopPropagation();
            var pos = (Vector2)e.localPosition;
            string best = HitMarker(pos);
            if (e.button == 1)
            {
                if (!wasDrag)
                {
                    if (best != null) Clicked?.Invoke(best);
                    else Dropped?.Invoke(FromPx(pos));
                }
                return;
            }
            if (wasDrag || e.button != 0) return;
            Clicked?.Invoke(best);
        }

        string HitMarker(Vector2 pos)
        {
            string best = null; float bd = 16f;
            foreach (var m in Markers)
            {
                if (m.kind == Kind.Player || m.kind == Kind.Shuttle) continue;
                if (string.IsNullOrEmpty(m.id)) continue;
                float d = (ToPx(m.world) - pos).magnitude;
                if (d < bd) { bd = d; best = m.id; }
            }
            return best;
        }

        void OnWheel(WheelEvent e)
        {
            if (!Interactive || State == null) return;
            if (Mathf.Abs(e.delta.y) < 1e-4f) return;
            float from = State.targetZoom > 0f ? State.targetZoom : State.zoom;
            float step = e.delta.y > 0f ? 1f / 1.2f : 1.2f;
            State.targetZoom = Mathf.Clamp(from * step, MinZoom, MaxZoom);
            State.zoomAnchor = (Vector2)e.localMousePosition;
            e.StopPropagation();
        }
    }
}
