using System.Collections.Generic;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// View-only panel for a cockpit console screen: the standard TerminalWindow frame (same size and style as every
    /// other terminal) showing the screen's live RenderTexture flat. Opens by growing from the clicked screen's projected
    /// rect to the full panel with a fade (<see cref="AnimSeconds"/>, eased), and shrinks back into the screen on close.
    /// Close: Esc / Tab (base), right mouse button, or Alt + click. Pointer picking is off while it animates.
    /// Runtime-only helper; never saved into the scene.
    /// </summary>
    public sealed class ScreenViewWindow : TerminalWindow
    {
        const float AnimSeconds = 0.3f;
        enum Phase { None, WaitLayout, Animating, Open }

        RenderTexture _rt;
        Vector3[] _corners;
        Camera _cam;
        Rect _from;
        Phase _phase;
        float _q;                 // animation progress 0 (at the screen, transparent) .. 1 (full panel)
        bool _closing;
        float _shownAt;
        readonly List<(VisualElement e, PickingMode m)> _picking = new List<(VisualElement, PickingMode)>();

        protected override string PanelId => "terminal.screen_view";
        public override float GetPriority(in InteractionContext c) => -1f; // never an E target

        public void Show(RenderTexture rt, string title, Vector3[] worldCorners, Camera cam)
        {
            if (IsOpen || rt == null) return;
            _rt = rt; _corners = worldCorners; _cam = cam;
            terminalName = title;
            Open();
        }

        /// <summary>Close with no animation (standing up, scene change).</summary>
        public void CloseNow() { if (IsOpen) FinishClose(); }

        protected override void OnOpened()
        {
            Hint.text = "Esc / right-click / Alt+click: close   ·   view only";
            _shownAt = Time.unscaledTime;
            _phase = Phase.WaitLayout;
            _closing = false;
            _q = 0f;
            Root.style.opacity = 0f;      // hidden until the first layout pass gives the panel rect
            SetPickingIgnored(true);
        }

        protected override void Build(VisualElement body)
        {
            Status.text = "View only";
            var col = Col(body, true);
            col.style.marginRight = 0;
            var img = new Image { image = _rt, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            img.style.flexGrow = 1;
            img.style.minHeight = 0;
            col.Add(img);
        }

        public override void Close()
        {
            if (!IsOpen || _closing) return;
            if (!isActiveAndEnabled || Window == null || _phase == Phase.None || _phase == Phase.WaitLayout) { FinishClose(); return; }
            _closing = true;
            _phase = Phase.Animating;
            SetPickingIgnored(true);
        }

        protected override void Tick()
        {
            switch (_phase)
            {
                case Phase.WaitLayout:
                {
                    var l = Window.layout;
                    if (float.IsNaN(l.width) || l.width < 2f || l.height < 2f) return;
                    if (!ComputeFrom(out _from))
                        _from = new Rect(l.center.x - l.width * 0.25f, l.center.y - l.height * 0.25f, l.width * 0.5f, l.height * 0.5f); // fallback: fade + grow from the middle
                    _phase = Phase.Animating;
                    Apply(0f);
                    return;
                }
                case Phase.Animating:
                {
                    float step = Time.unscaledDeltaTime / AnimSeconds;
                    _q = Mathf.Clamp01(_q + (_closing ? -step : step));
                    Apply(Mathf.SmoothStep(0f, 1f, _q));
                    if (_closing && _q <= 0f) FinishClose();
                    else if (!_closing && _q >= 1f) { ClearTransform(); SetPickingIgnored(false); _phase = Phase.Open; }
                    return;
                }
                case Phase.Open:
                {
                    if (Time.unscaledTime - _shownAt < 0.25f) return; // the click that opened it
                    var m = Mouse.current; var kb = Keyboard.current;
                    bool rmb = m != null && m.rightButton.wasPressedThisFrame;
                    bool altClick = m != null && m.leftButton.wasPressedThisFrame && kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
                    if (rmb || altClick) Close();
                    return;
                }
            }
        }

        /// <summary>The screen mesh's bounds projected into panel space.</summary>
        bool ComputeFrom(out Rect r)
        {
            r = default;
            var panel = Root.panel;
            if (panel == null || _cam == null || _corners == null || _corners.Length == 0) return false;
            var mn = new Vector2(float.MaxValue, float.MaxValue); var mx = new Vector2(float.MinValue, float.MinValue);
            foreach (var c in _corners)
            {
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, c, _cam);
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y)) return false;
                mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p);
            }
            r = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
            return r.width > 2f && r.height > 2f;
        }

        /// <summary>p = 0: panel squashed onto the screen rect, transparent. p = 1: normal panel.</summary>
        void Apply(float p)
        {
            var to = Window.layout;
            Vector2 off = (_from.center - to.center) * (1f - p);
            Vector2 sc = Vector2.Lerp(new Vector2(_from.width / to.width, _from.height / to.height), Vector2.one, p);
            Window.style.translate = new Translate(off.x, off.y);
            Window.style.scale = new Scale(sc);
            Root.style.opacity = p;
        }

        void ClearTransform()
        {
            if (Window == null) return;
            Window.style.translate = StyleKeyword.Null;
            Window.style.scale = StyleKeyword.Null;
            Root.style.opacity = 1f;
        }

        void FinishClose()
        {
            _phase = Phase.None;
            _closing = false;
            ClearTransform();
            SetPickingIgnored(false);
            base.Close();
        }

        void SetPickingIgnored(bool ignore)
        {
            if (ignore)
            {
                if (_picking.Count > 0 || Root == null) return;
                _picking.Add((Root, Root.pickingMode));
                foreach (var e in Root.Query<VisualElement>().ToList()) if (e != Root) _picking.Add((e, e.pickingMode));
                foreach (var (e, _) in _picking) e.pickingMode = PickingMode.Ignore;
            }
            else
            {
                foreach (var (e, m) in _picking) if (e != null) e.pickingMode = m;
                _picking.Clear();
            }
        }
    }
}
