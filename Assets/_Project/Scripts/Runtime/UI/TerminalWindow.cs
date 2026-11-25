using System;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Base for in-world terminals: an interactable that opens a full-screen UI Toolkit window (built in code, styled by
    /// Resources/UI/Xenoarch.uss). Opening registers a UIState panel, which frees the cursor and blocks gameplay input.
    /// </summary>
    public abstract class TerminalWindow : MonoBehaviour, IInteractable
    {
        public string terminalName = "Terminal";
        [Tooltip("Optional point the interaction raycast/prompt uses (defaults to this transform).")]
        public Transform interactionPoint;

        protected VisualElement Body { get; private set; }
        protected Label Status { get; private set; }
        protected Label Hint { get; private set; }
        protected GameSession Session => XenoarchRuntime.Session;
        public bool IsOpen { get; private set; }
        protected abstract string PanelId { get; }

        UIDocument _doc;
        VisualElement _root;
        Label _title;
        float _openedAt;

        public Transform InteractionTransform => interactionPoint != null ? interactionPoint : transform;
        public virtual float GetPriority(in InteractionContext c) => IsOpen ? -1f : 1f;
        public virtual string GetPrompt(in InteractionContext c) => $"Use {terminalName}";
        public float GetHoldDuration(in InteractionContext c) => 0f;
        public bool TryInteract(in InteractionContext c) { Open(); return IsOpen; }

        public void Open()
        {
            if (IsOpen || Session == null) return;
            EnsureUi();
            if (_root == null) return;
            IsOpen = true;
            _openedAt = Time.unscaledTime;
            _title.text = terminalName.ToUpperInvariant();
            XenoarchUI.Show(_root, true);
            UIState.SetOpen(PanelId, true);
            OnOpened();
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            XenoarchUI.Show(_root, false);
            UIState.SetOpen(PanelId, false);
            OnClosed();
        }

        /// <summary>Rebuild the window contents from state.</summary>
        public void Refresh()
        {
            if (!IsOpen || Body == null) return;
            Body.Clear();
            Build(Body);
        }

        protected abstract void Build(VisualElement body);
        protected virtual void OnOpened() { }
        protected virtual void OnClosed() { }
        protected virtual void Tick() { }

        protected virtual void Update()
        {
            if (!IsOpen) return;
            Tick();
            if (Time.unscaledTime - _openedAt < 0.2f) return;
            var kb = Keyboard.current; var gp = Gamepad.current;
            if ((kb != null && (kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) || (gp != null && gp.buttonEast.wasPressedThisFrame)) Close();
        }

        protected virtual void OnDisable() { if (IsOpen) Close(); }

        void EnsureUi()
        {
            if (_doc != null) return;
            var host = new GameObject($"{terminalName} UI");
            host.transform.SetParent(transform, false);
            _doc = host.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 40;
            var docRoot = _doc.rootVisualElement;
            if (docRoot == null) { Debug.LogError($"[Xenoarch] {terminalName}: UIDocument has no root"); return; }
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch");
            if (uss != null) docRoot.styleSheets.Add(uss);

            _root = new VisualElement(); _root.AddToClassList("xa-fill"); _root.AddToClassList("xa-term-root");
            docRoot.Add(_root);
            var win = new VisualElement(); win.AddToClassList("xa-term"); _root.Add(win);
            var header = new VisualElement(); header.AddToClassList("xa-term__header"); win.Add(header);
            _title = new Label(); _title.AddToClassList("xa-term__title"); header.Add(_title);
            Status = new Label(); Status.AddToClassList("xa-term__status"); header.Add(Status);
            Body = new VisualElement(); Body.AddToClassList("xa-term__body"); win.Add(Body);
            var footer = new VisualElement(); footer.AddToClassList("xa-term__footer"); win.Add(footer);
            Hint = new Label("Esc / Tab / B: close"); Hint.AddToClassList("xa-term__hint"); footer.Add(Hint);
            var close = Btn("Close", Close); close.AddToClassList("xa-term__btn--ghost"); close.style.width = 140; footer.Add(close);
            XenoarchUI.Show(_root, false);
        }

        // ---------------------------------------------------------------- small builders
        protected static VisualElement Col(VisualElement parent, bool wide = false)
        {
            var c = new VisualElement(); c.AddToClassList("xa-term__col"); if (wide) c.AddToClassList("xa-term__col--wide");
            parent.Add(c); return c;
        }

        protected static Label Text(VisualElement parent, string text, string cls = "xa-term__text")
        {
            var l = new Label(text); l.AddToClassList(cls); parent.Add(l); return l;
        }

        protected static Button Btn(string text, Action onClick, bool enabled = true)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("xa-term__btn");
            b.SetEnabled(enabled);
            return b;
        }

        protected static Button Btn(VisualElement parent, string text, Action onClick, bool enabled = true)
        {
            var b = Btn(text, onClick, enabled); parent.Add(b); return b;
        }

        protected static VisualElement Bar(VisualElement parent, float fill01)
        {
            var bar = new VisualElement(); bar.AddToClassList("xa-term__bar");
            var f = new VisualElement(); f.AddToClassList("xa-term__bar-fill"); f.style.width = Length.Percent(Mathf.Clamp01(fill01) * 100f);
            bar.Add(f); parent.Add(bar); return f;
        }
    }
}
