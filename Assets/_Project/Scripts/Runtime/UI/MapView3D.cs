using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Interactive 2.5D map (system and galaxy maps): orthographic view of points on/around a plane.
    /// LMB/MMB drag pans, RMB drag rotates (yaw) and tilts (pitch), wheel zooms at the cursor, click selects.
    /// View state lives in <see cref="ViewState"/> so it survives terminal rebuilds.
    /// </summary>
    public sealed class MapView3D : VisualElement
    {
        public sealed class ViewState
        {
            public float yaw, pitch = 60f, zoom = 1f;
            public Vector2 pan;
            public void Reset() { yaw = 0f; pitch = 60f; zoom = 1f; pan = Vector2.zero; }
        }

        public struct Item { public string id, label; public Vector3 pos; public Color color; public float size; public bool selected, here, dim, alwaysLabel; }
        public struct Ring { public Vector3 centre; public float radius; public Color color; }
        public struct Line { public Vector3 a, b; public Color color; }

        public readonly List<Item> Items = new List<Item>();
        public readonly List<Ring> Rings = new List<Ring>();
        public readonly List<Line> Lines = new List<Line>();
        public float Extent = 1f;
        public ViewState State;
        public event Action<string> Clicked;

        readonly List<Label> _labels = new List<Label>();
        Vector2 _downPos, _lastPos; int _button = -1; bool _dragged;

        public const float MinPitch = 5f, MaxPitch = 90f, MinZoom = 0.4f, MaxZoom = 40f;

        public MapView3D(ViewState state)
        {
            State = state ?? new ViewState();
            AddToClassList("xa-term__map");
            style.overflow = Overflow.Hidden;
            pickingMode = PickingMode.Position;
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Relayout());
            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<ContextClickEvent>(e => e.StopPropagation());
        }

        /// <summary>Orthographic projection: yaw about Y, then elevation (90 = top-down, 0 = edge-on). Returns UI offset
        /// from the view centre in map units (y down) and a depth (larger = nearer the viewer).</summary>
        public static Vector2 Project(Vector3 p, float yaw, float pitch, out float depth)
        {
            var r = Quaternion.Euler(0f, yaw, 0f) * p;
            float e = pitch * Mathf.Deg2Rad, s = Mathf.Sin(e), c = Mathf.Cos(e);
            depth = r.y * s - r.z * c;
            return new Vector2(r.x, -(r.z * s + r.y * c));
        }

        float Scale => Mathf.Min(contentRect.width, contentRect.height) * 0.46f / Mathf.Max(0.0001f, Extent) * State.zoom;
        Vector2 Centre => contentRect.center + State.pan;
        public Vector2 ToScreen(Vector3 p, out float depth) => Centre + Project(p, State.yaw, State.pitch, out depth) * Scale;

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
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (_button < 0 || !this.HasPointerCapture(e.pointerId)) return;
            var pos = (Vector2)e.localPosition; var d = pos - _lastPos; _lastPos = pos;
            if ((pos - _downPos).sqrMagnitude > 16f) _dragged = true;
            if (!_dragged) return;
            if (_button == 1 || (_button == 0 && e.altKey))
            {
                State.yaw -= d.x * 0.4f;
                State.pitch = Mathf.Clamp(State.pitch - d.y * 0.3f, MinPitch, MaxPitch);
            }
            else State.pan += d;
            Rebuild();
        }

        void OnUp(PointerUpEvent e)
        {
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
            bool click = _button == 0 && !_dragged;
            _button = -1;
            if (!click) return;
            var pos = (Vector2)e.localPosition;
            string best = null; float bestD = float.MaxValue;
            foreach (var it in Items)
            {
                var s = ToScreen(it.pos, out _);
                float d = (s - pos).magnitude, hit = Mathf.Max(10f, it.size * 0.5f + 5f);
                if (d <= hit && d < bestD) { bestD = d; best = it.id; }
            }
            if (best != null) Clicked?.Invoke(best);
        }

        void OnWheel(WheelEvent e)
        {
            float old = State.zoom;
            State.zoom = Mathf.Clamp(State.zoom * Mathf.Pow(1.15f, -Mathf.Sign(e.delta.y)), MinZoom, MaxZoom);
            // Zoom toward the cursor.
            var m = (Vector2)e.localMousePosition - contentRect.center;
            State.pan = m - (m - State.pan) * (State.zoom / old);
            Rebuild();
            e.StopPropagation();
        }
    }
}
