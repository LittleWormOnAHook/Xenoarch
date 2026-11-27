using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// UI Toolkit drag and drop (t108u / t213u / t223u). A click only selects; a drag starts after the pointer
    /// moves 16 px while held, and release drops. Callbacks are registered on both the root and each source so a
    /// Button/Clickable that captures the pointer cannot swallow the up or leave a pending carry.
    /// </summary>
    public sealed class DragDrop
    {
        public const float ThresholdPx = 16f;

        public sealed class Payload
        {
            public string kind;        // "backpack", "hotbar", "crate"
            public int index;
            public string itemId;
            public object container;
            public Sprite icon;
        }

        sealed class Zone { public Func<Payload, bool> accept; public Action<Payload> drop; }

        readonly VisualElement _root;
        Payload _pending; Vector2 _start; bool _dragging; VisualElement _ghost; int _pointer = -1;
        public bool Dragging => _dragging;

        public DragDrop(VisualElement root)
        {
            _root = root;
            Bind(_root, true);
        }

        void Bind(VisualElement el, bool includeCaptureOut)
        {
            el.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            el.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            el.RegisterCallback<PointerMoveEvent>(OnMove);
            el.RegisterCallback<PointerUpEvent>(OnUp);
            if (includeCaptureOut)
                el.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut, TrickleDown.TrickleDown);
        }

        /// <summary>Make an element draggable. <paramref name="make"/> returns null when there is nothing to drag.</summary>
        public void Source(VisualElement el, Func<Payload> make)
        {
            el.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                var p = make(); if (p == null) return;
                CancelPending();
                _pending = p; _start = e.position; _pointer = e.pointerId; _dragging = false;
            }, TrickleDown.TrickleDown);
            // Same-element move/up: required when the element (e.g. a Button) captures the pointer.
            el.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            el.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
        }

        public static void Target(VisualElement el, Func<Payload, bool> accept, Action<Payload> drop) =>
            el.userData = new Zone { accept = accept, drop = drop };

        void OnMove(PointerMoveEvent e)
        {
            if (_pending == null || e.pointerId != _pointer) return;
            if (!_dragging)
            {
                if (((Vector2)e.position - _start).sqrMagnitude < ThresholdPx * ThresholdPx) return;
                _dragging = true;
                _ghost = new VisualElement { pickingMode = PickingMode.Ignore };
                _ghost.style.position = Position.Absolute; _ghost.style.width = 80; _ghost.style.height = 80;
                _ghost.style.opacity = 0.85f;
                _ghost.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDark, 0.8f);
                _ghost.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                if (_pending.icon != null) { _ghost.style.backgroundImage = new StyleBackground(_pending.icon); XenoarchIcons.ApplyItem(_ghost, _pending.itemId); }
                else _ghost.Add(new Label(_pending.itemId) { pickingMode = PickingMode.Ignore, style = { fontSize = 9, whiteSpace = WhiteSpace.Normal } });
                _root.Add(_ghost);
                _root.CapturePointer(e.pointerId);
            }
            if (_ghost == null) return;
            var local = _root.WorldToLocal(e.position);
            _ghost.style.left = local.x - 28; _ghost.style.top = local.y - 28;
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (_pending == null || e.pointerId != _pointer) return;
            var p = _pending;
            bool wasDragging = _dragging;
            CancelPending();
            if (!wasDragging) return;   // a plain click: leave it to the slot's select handler
            e.StopPropagation();
            var picked = _root.panel?.Pick(e.position);
            for (var v = picked; v != null; v = v.parent)
                if (v.userData is Zone z)
                {
                    if (z.accept == null || z.accept(p)) z.drop?.Invoke(p);
                    return;
                }
        }

        void OnCaptureOut(PointerCaptureOutEvent e)
        {
            // If a Button stole and then released capture without a drag, drop the pending carry so the next
            // mouse move cannot pick the item up as if the click had locked it.
            if (e.pointerId != _pointer || _dragging) return;
            _pending = null; _pointer = -1;
        }

        void CancelPending()
        {
            if (_pointer >= 0 && _root != null && _root.HasPointerCapture(_pointer))
                _root.ReleasePointer(_pointer);
            _ghost?.RemoveFromHierarchy(); _ghost = null;
            _pending = null; _dragging = false; _pointer = -1;
        }
    }
}
