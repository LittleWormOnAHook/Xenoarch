using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Minimal UI Toolkit drag and drop (t108u): sources start a drag after an 8 px move (a plain click still clicks),
    /// a ghost icon follows the pointer, and the drop goes to the nearest ancestor of the picked element that was
    /// registered as a zone. One instance per window root; survives the window rebuilding its contents.
    /// </summary>
    public sealed class DragDrop
    {
        public sealed class Payload
        {
            public string kind;        // "backpack", "hotbar", "crate"
            public int index;          // slot index in its container
            public string itemId;
            public object container;   // e.g. the InventoryModel the stack lives in
            public Sprite icon;
        }

        sealed class Zone { public Func<Payload, bool> accept; public Action<Payload> drop; }

        readonly VisualElement _root;
        Payload _pending; Vector2 _start; bool _dragging; VisualElement _ghost; int _pointer = -1;
        public bool Dragging => _dragging;

        public DragDrop(VisualElement root)
        {
            _root = root;
            root.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
        }

        /// <summary>Make an element draggable. <paramref name="make"/> returns null when there is nothing to drag.</summary>
        public void Source(VisualElement el, Func<Payload> make)
        {
            el.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                var p = make(); if (p == null) return;
                _pending = p; _start = e.position; _pointer = e.pointerId;
            }, TrickleDown.TrickleDown);
        }

        /// <summary>Make an element a drop target.</summary>
        public static void Target(VisualElement el, Func<Payload, bool> accept, Action<Payload> drop) =>
            el.userData = new Zone { accept = accept, drop = drop };

        void OnMove(PointerMoveEvent e)
        {
            if (_pending == null || e.pointerId != _pointer) return;
            if (!_dragging)
            {
                if (((Vector2)e.position - _start).sqrMagnitude < 64f) return;
                _dragging = true;
                _ghost = new VisualElement { pickingMode = PickingMode.Ignore };
                _ghost.style.position = Position.Absolute; _ghost.style.width = 56; _ghost.style.height = 56;
                _ghost.style.opacity = 0.85f;
                _ghost.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDark, 0.8f);
                if (_pending.icon != null) _ghost.style.backgroundImage = new StyleBackground(_pending.icon);
                else _ghost.Add(new Label(_pending.itemId) { pickingMode = PickingMode.Ignore, style = { fontSize = 9, whiteSpace = WhiteSpace.Normal } });
                _root.Add(_ghost);
                _root.CapturePointer(e.pointerId);
            }
            var local = _root.WorldToLocal(e.position);
            _ghost.style.left = local.x - 28; _ghost.style.top = local.y - 28;
            e.StopPropagation();
        }

        void OnUp(PointerUpEvent e)
        {
            if (_pending == null || e.pointerId != _pointer) return;
            var p = _pending; _pending = null;
            if (!_dragging) return;   // a click: let the button handle it
            _dragging = false;
            if (_root.HasPointerCapture(e.pointerId)) _root.ReleasePointer(e.pointerId);
            _ghost?.RemoveFromHierarchy(); _ghost = null;
            e.StopPropagation();
            var picked = _root.panel?.Pick(e.position);
            for (var v = picked; v != null; v = v.parent)
                if (v.userData is Zone z)
                {
                    if (z.accept == null || z.accept(p)) z.drop?.Invoke(p);
                    return;
                }
        }
    }
}
