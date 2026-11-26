using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Interactive 3D map (system and galaxy maps): orthographic view that orbits a focus point, free in all directions.
    /// RMB drag (or Alt+LMB) rotates trackball-style about the view's own axes (no clamps, no gimbal flip: you can go
    /// over the top and underneath); Shift+RMB drag or Q/E rolls. LMB/MMB drag pans in screen space along the rotated
    /// view (any direction); Ctrl+LMB drag moves the focus in depth. Wheel zooms smoothly toward the cursor over a wide
    /// range; double-click a body to fly the focus to it and orbit around it; click selects. View state lives in
    /// <see cref="ViewState"/> so it survives terminal rebuilds.
    /// </summary>
    public sealed class MapView3D : VisualElement
    {
        public sealed class ViewState
        {
            [Tooltip("Tilt used by Reset (90 = top-down).")] public float pitch = 60f;
            public float yaw;
            public float zoom = 1f;
            /// <summary>World point at the view centre (the orbit pivot).</summary>
            public Vector3 focus;
            Quaternion _cam; bool _hasCam;
            /// <summary>Camera orientation (view looks along its forward; up is screen up).</summary>
            public Quaternion Cam { get { if (!_hasCam) { _cam = DefaultCam(yaw, pitch); _hasCam = true; } return _cam; } set { _cam = Normalize(value); _hasCam = true; } }
            internal float targetZoom = -1f; internal Vector2 zoomAnchor; internal bool focusAnim; internal Vector3 focusTarget;
            public void Reset() { zoom = 1f; targetZoom = -1f; focus = Vector3.zero; focusAnim = false; _hasCam = false; }
        }

        public static Quaternion DefaultCam(float yaw, float pitch) => Quaternion.Euler(pitch, -yaw, 0f);
        static Quaternion Normalize(Quaternion q) { float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w); return m < 1e-6f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m); }

        public struct Item { public string id, label; public Vector3 pos; public Color color; public float size; public bool selected, here, dim, alwaysLabel; }
        public struct Ring { public Vector3 centre; public float radius; public Color color; }
        public struct Line { public Vector3 a, b; public Color color; }

        public readonly List<Item> Items = new List<Item>();
        public readonly List<Ring> Rings = new List<Ring>();
        public readonly List<Line> Lines = new List<Line>();
        public float Extent = 1f;
        public ViewState State;
        public event Action<string> Clicked;
        /// <summary>Raised after a double-click focused a body.</summary>
        public event Action<string> Focused;

        readonly List<Label> _labels = new List<Label>();
        Vector2 _downPos, _lastPos; int _button = -1; bool _dragged; bool _shift, _ctrl, _alt;
        // Static: the terminal rebuilds the map on the first click, so a per-instance record would miss the second one.
        static string _lastClickId; static float _lastClickTime;

        public const float MinZoom = 0.05f, MaxZoom = 400f;

        public MapView3D(ViewState state)
        {
            State = state ?? new ViewState();
            AddToClassList("xa-term__map");
            style.overflow = Overflow.Hidden;
            pickingMode = PickingMode.Position;
            focusable = true;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Relayout());
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKey);
            RegisterCallback<ContextClickEvent>(e => e.StopPropagation());
            schedule.Execute(Animate).Every(16);
        }

        /// <summary>Orthographic projection for a camera at (yaw, pitch) (90 = top-down, 0 = edge-on). Returns the UI offset
        /// from the view centre in map units (y down) and a depth (larger = nearer the viewer).</summary>
        public static Vector2 Project(Vector3 p, float yaw, float pitch, out float depth) => Project(p, DefaultCam(yaw, pitch), out depth);

        public static Vector2 Project(Vector3 p, Quaternion cam, out float depth)
        {
            var v = Quaternion.Inverse(cam) * p;
            depth = -v.z;
            return new Vector2(v.x, -v.y);
        }

        float ScaleFor(float zoom) => Mathf.Min(contentRect.width, contentRect.height) * 0.46f / Mathf.Max(0.0001f, Extent) * zoom;
        float Scale => ScaleFor(State.zoom);
        Vector2 Centre => contentRect.center;
        public Vector2 ToScreen(Vector3 p, out float depth) => Centre + Project(p - State.focus, State.Cam, out depth) * Scale;

        /// <summary>World point under a UI position, on the plane through the focus facing the viewer.</summary>
        Vector3 ScreenToWorld(Vector2 local, float zoom)
        {
            var m = (local - Centre) / Mathf.Max(1e-6f, ScaleFor(zoom));
            return State.focus + State.Cam * new Vector3(m.x, -m.y, 0f);
        }

        public void Rebuild() { Relayout(); MarkDirtyRepaint(); }

        void Relayout()
        {
            if (float.IsNaN(contentRect.width) || contentRect.width < 4f) return;
            int li = 0;
            foreach (var it in Items)
            {
                if (string.IsNullOrEmpty(it.label)) continue;
                if (!(it.alwaysLabel || it.selected || it.here || State.zoom >= 1.6f)) continue;
                if (li >= _labels.Count) { var l = new Label { pickingMode = PickingMode.Ignore }; l.AddToClassList("xa-map-label"); _labels.Add(l); Add(l); }
                var lbl = _labels[li++];
                var p = ToScreen(it.pos, out _);
                lbl.text = (it.here ? "> " : "") + it.label;
                lbl.style.left = p.x + it.size * 0.5f + 3f; lbl.style.top = p.y - 8f;
                lbl.style.color = it.here ? new Color(0.95f, 0.66f, 0.22f) : it.selected ? new Color(1f, 0.85f, 0.55f) : new Color(0.84f, 0.9f, 0.95f, it.dim ? 0.45f : 0.85f);
                lbl.style.display = contentRect.Contains(p) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            for (int i = li; i < _labels.Count; i++) _labels[i].style.display = DisplayStyle.None;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p2 = ctx.painter2D;
            p2.lineWidth = 1f;
            foreach (var r in Rings)
            {
                p2.strokeColor = r.color; p2.BeginPath();
                const int seg = 72;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    var s = ToScreen(r.centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r.radius, out _);
                    if (i == 0) p2.MoveTo(s); else p2.LineTo(s);
                }
                p2.Stroke();
            }
            foreach (var l in Lines)
            {
                p2.strokeColor = l.color; p2.BeginPath();
                p2.MoveTo(ToScreen(l.a, out _)); p2.LineTo(ToScreen(l.b, out _)); p2.Stroke();
            }
            var order = new List<(float d, int i)>();
            for (int i = 0; i < Items.Count; i++) { ToScreen(Items[i].pos, out float d); order.Add((d, i)); }
            order.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var (_, i) in order)
            {
                var it = Items[i];
                var s = ToScreen(it.pos, out _);
                float rad = Mathf.Max(2f, it.size * 0.5f);
                p2.fillColor = it.dim ? new Color(it.color.r * 0.5f, it.color.g * 0.5f, it.color.b * 0.55f, 0.8f) : it.color;
                p2.BeginPath(); p2.Arc(s, rad, 0f, 360f); p2.Fill();
                if (it.selected || it.here)
                {
                    p2.lineWidth = 2f; p2.strokeColor = it.selected ? new Color(0.95f, 0.66f, 0.22f) : new Color(0.55f, 0.85f, 1f);
                    p2.BeginPath(); p2.Arc(s, rad + 4f, 0f, 360f); p2.Stroke(); p2.lineWidth = 1f;
                }
            }
        }

        void OnDown(PointerDownEvent e)
        {
            _button = e.button; _downPos = _lastPos = (Vector2)e.localPosition; _dragged = false;
            _shift = e.shiftKey; _ctrl = e.ctrlKey || e.commandKey; _alt = e.altKey;
            this.CapturePointer(e.pointerId);
            Focus();
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (_button < 0 || !this.HasPointerCapture(e.pointerId)) return;
            var pos = (Vector2)e.localPosition; var d = pos - _lastPos; _lastPos = pos;
            if ((pos - _downPos).sqrMagnitude > 16f) _dragged = true;
            if (!_dragged) return;
            var cam = State.Cam;
            if (_button == 1 && (_shift || e.shiftKey))
                State.Cam = cam * Quaternion.AngleAxis(-d.x * 0.4f, Vector3.forward);               // roll
            else if (_button == 1 || (_button == 0 && _alt))
                State.Cam = cam * Quaternion.AngleAxis(d.x * 0.4f, Vector3.up) * Quaternion.AngleAxis(-d.y * 0.4f, Vector3.right); // trackball: own axes, unclamped
            else if (_button == 0 && _ctrl)
                { State.focus += cam * Vector3.forward * (-d.y / Mathf.Max(1e-6f, Scale)); State.focusAnim = false; } // depth
            else
                { State.focus -= cam * new Vector3(d.x, -d.y, 0f) / Mathf.Max(1e-6f, Scale); State.focusAnim = false; } // screen-space pan
            Rebuild();
        }

        void OnUp(PointerUpEvent e)
        {
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            bool click = _button == 0 && !_dragged;
            _button = -1;
            if (!click) return;
            var pos = (Vector2)e.localPosition;
            string best = null; float bestD = float.MaxValue; Vector3 bestPos = default;
            foreach (var it in Items)
            {
                var s = ToScreen(it.pos, out _);
                float d = (s - pos).magnitude, hit = Mathf.Max(10f, it.size * 0.5f + 5f);
                if (d <= hit && d < bestD) { bestD = d; best = it.id; bestPos = it.pos; }
            }
            if (best == null) return;
            bool dbl = best == _lastClickId && Time.unscaledTime - _lastClickTime < 0.4f;
            _lastClickId = best; _lastClickTime = Time.unscaledTime;
            if (dbl) { FocusOn(bestPos); Focused?.Invoke(best); _lastClickId = null; }
            else Clicked?.Invoke(best);
        }

        /// <summary>Fly the orbit pivot to a world point (rotation then orbits around it); zooms in a little if far out.</summary>
        public void FocusOn(Vector3 world)
        {
            State.focusTarget = world; State.focusAnim = true;
            if (State.zoom < 2f) { State.targetZoom = 2f; State.zoomAnchor = Centre; }
        }

        void OnWheel(WheelEvent e)
        {
            float from = State.targetZoom > 0f ? State.targetZoom : State.zoom;
            if (Mathf.Abs(e.delta.y) < 1e-4f) return;
            float step = e.delta.y > 0f ? 1f / 1.2f : 1.2f;
            State.targetZoom = Mathf.Clamp(from * step, MinZoom, MaxZoom);
            State.zoomAnchor = (Vector2)e.localMousePosition;
            e.StopPropagation();
        }

        void OnKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Q || e.keyCode == KeyCode.E)
            {
                State.Cam = State.Cam * Quaternion.AngleAxis(e.keyCode == KeyCode.Q ? 5f : -5f, Vector3.forward);
                Rebuild(); e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.R) { State.Reset(); Rebuild(); e.StopPropagation(); }
        }

        /// <summary>Smooth zoom toward the cursor (the world point under it stays put) and focus flights.</summary>
        void Animate()
        {
            bool dirty = false;
            if (State.targetZoom > 0f)
            {
                float z = State.zoom;
                float next = Mathf.Exp(Mathf.Lerp(Mathf.Log(z), Mathf.Log(State.targetZoom), 0.25f));
                if (Mathf.Abs(Mathf.Log(next / State.targetZoom)) < 0.002f) { next = State.targetZoom; State.targetZoom = -1f; }
                var anchorWorld = ScreenToWorld(State.zoomAnchor, z);
                State.zoom = next;
                if (!State.focusAnim)
                {
                    var after = ScreenToWorld(State.zoomAnchor, next);
                    State.focus += anchorWorld - after;
                }
                dirty = true;
            }
            if (State.focusAnim)
            {
                State.focus = Vector3.Lerp(State.focus, State.focusTarget, 0.2f);
                if ((State.focus - State.focusTarget).sqrMagnitude < 1e-8f * Mathf.Max(1f, Extent * Extent)) { State.focus = State.focusTarget; State.focusAnim = false; }
                dirty = true;
            }
            if (dirty) Rebuild();
        }
    }
}
