using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Journals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Datapad note reader for <see cref="JournalEntry"/>: title, author / date line, scrolling body; prev / next through
    /// the collected journals when opened from the inventory. Frees the cursor and pauses like the M map; E / Esc closes.
    /// Opening marks the entry read.
    /// </summary>
    public sealed class JournalReader : MonoBehaviour
    {
        public const string Panel = "journal.reader";
        static JournalReader _inst;
        /// <summary>True while open and on the frame it closed (so Esc does not also close the window underneath).</summary>
        public static bool Blocking => _inst != null && (_inst._veil != null || Time.frameCount <= _inst._closedFrame + 1);

        UIDocument _doc; VisualElement _veil;
        List<string> _list = new List<string>(); int _index;
        float _openedAt, _prevTimeScale = 1f; int _closedFrame = -10;

        void Awake() { _inst = this; }
        void OnDestroy() { if (_inst == this) _inst = null; }

        /// <summary>Opens one entry; <paramref name="browse"/> (ids) enables prev / next.</summary>
        public static void Show(string id, IList<string> browse = null)
        {
            if (_inst == null) { var rt = XenoarchRuntime.Instance; if (rt == null) return; _inst = rt.gameObject.GetComponent<JournalReader>() ?? rt.gameObject.AddComponent<JournalReader>(); }
            _inst.Open(id, browse);
        }

        void Open(string id, IList<string> browse)
        {
            _list = browse != null && browse.Count > 0 ? browse.ToList() : new List<string> { id };
            _index = Mathf.Max(0, _list.IndexOf(id));
            if (_veil == null)
            {
                if (_doc == null)
                {
                    var host = new GameObject("JournalReader"); host.transform.SetParent(transform, false);
                    _doc = host.AddComponent<UIDocument>(); _doc.panelSettings = XenoarchUI.LoadPanelSettings(); _doc.sortingOrder = 70;
                }
                var root = _doc.rootVisualElement; XenoarchUI.FillRoot(root); root.pickingMode = PickingMode.Ignore;
                var uss = Resources.Load<StyleSheet>("UI/Xenoarch"); if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);
                _veil = new VisualElement(); XenoarchUI.FillRoot(_veil); _veil.AddToClassList("xa-term-root");
                root.Add(_veil);
                UIState.SetOpen(Panel, true);
                if (Time.timeScale > 0f) { _prevTimeScale = Time.timeScale; Time.timeScale = 0f; } else _prevTimeScale = -1f;
            }
            _openedAt = Time.unscaledTime;
            Render();
        }

        void Close()
        {
            if (_veil == null) return;
            _veil.RemoveFromHierarchy(); _veil = null;
            _closedFrame = Time.frameCount;
            UIState.SetOpen(Panel, false);
            if (_prevTimeScale > 0f && Time.timeScale == 0f && !UIState.IsOpen(Panels.Pause) && !UIState.IsOpen(DigSiteMapWindow.Panel)) Time.timeScale = _prevTimeScale;
        }

        void OnDisable() => Close();

        void Update()
        {
            if (_veil == null || Time.unscaledTime - _openedAt < 0.25f) return;
            var kb = Keyboard.current; var gp = Gamepad.current;
            if ((kb != null && (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) || (gp != null && gp.buttonEast.wasPressedThisFrame)) { Close(); return; }
            if (_list.Count > 1)
            {
                if ((kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame)) || (gp != null && gp.leftShoulder.wasPressedThisFrame)) Step(-1);
                else if ((kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame)) || (gp != null && gp.rightShoulder.wasPressedThisFrame)) Step(1);
            }
        }

        void Step(int d) { _index = (_index + d + _list.Count) % _list.Count; Render(); }

        void Render()
        {
            if (_veil == null) return;
            _veil.Clear();
            var s = XenoarchRuntime.Session;
            var e = JournalService.Find(_list[_index]);
            if (e == null) { Close(); return; }
            s?.Journals.MarkRead(e.Id);

            var pad = new VisualElement();
            pad.style.width = 780; pad.style.height = 640; pad.style.maxWidth = Length.Percent(92); pad.style.maxHeight = Length.Percent(90);
            pad.style.backgroundColor = new Color(0.06f, 0.07f, 0.08f, 0.97f);
            Border(pad, XenoarchPalette.A(XenoarchPalette.Highlight, 0.65f), 2f, 10f);
            pad.style.paddingLeft = 34; pad.style.paddingRight = 34; pad.style.paddingTop = 22; pad.style.paddingBottom = 18;
            _veil.Add(pad);

            // Header strip: device label + category + position.
            var strip = Row(pad); strip.style.justifyContent = Justify.SpaceBetween; strip.style.marginBottom = 10;
            strip.Add(Lbl($"SURVEY DATAPAD  //  {e.category.ToString().ToUpperInvariant()}", 12, XenoarchPalette.Highlight, true, 3));
            strip.Add(Lbl(_list.Count > 1 ? $"{_index + 1} / {_list.Count}" : "", 12, XenoarchPalette.SecondaryText));

            var title = Lbl(e.title, 28, XenoarchPalette.Primary, true, 1); title.style.whiteSpace = WhiteSpace.Normal; pad.Add(title);
            string by = string.Join("   |   ", new[] { e.author, e.dateLine }.Where(x => !string.IsNullOrEmpty(x)));
            if (by.Length > 0) { var b = Lbl(by, 14, XenoarchPalette.SecondaryText); b.style.marginTop = 4; pad.Add(b); }
            var rule = new VisualElement(); rule.style.height = 1; rule.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.Highlight, 0.35f); rule.style.marginTop = 12; rule.style.marginBottom = 12; pad.Add(rule);

            var sv = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            sv.style.flexGrow = 1; sv.style.minHeight = 0; pad.Add(sv);
            foreach (var para in (e.body ?? "").Replace("\r", "").Split(new[] { "\n\n" }, System.StringSplitOptions.None))
            {
                var p = Lbl(para.Trim(), 17, XenoarchPalette.Text); p.style.whiteSpace = WhiteSpace.Normal; p.style.marginBottom = 12;
                sv.contentContainer.Add(p);
            }

            var foot = Row(pad); foot.style.justifyContent = Justify.SpaceBetween; foot.style.alignItems = Align.Center; foot.style.marginTop = 8;
            var nav = Row(foot);
            if (_list.Count > 1) { nav.Add(Button("< Prev", () => Step(-1))); nav.Add(Button("Next >", () => Step(1))); }
            foot.Add(Lbl(_list.Count > 1 ? "Q / R or arrows: prev / next    E / Esc: close" : "E / Esc: close", 13, XenoarchPalette.SecondaryText));
            foot.Add(Button("Close", Close));
        }

        static VisualElement Row(VisualElement parent) { var r = new VisualElement(); r.style.flexDirection = FlexDirection.Row; parent.Add(r); return r; }

        static Label Lbl(string text, int size, Color col, bool bold = false, float spacing = 0f)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size; l.style.color = col; if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            if (spacing > 0f) l.style.letterSpacing = spacing;
            return l;
        }

        static Button Button(string text, System.Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("xa-button"); b.style.height = 38; b.style.width = 120; b.style.marginLeft = 6; b.style.marginRight = 6; b.style.fontSize = 15;
            return b;
        }

        static void Border(VisualElement v, Color c, float w, float radius)
        {
            v.style.borderLeftColor = c; v.style.borderRightColor = c; v.style.borderTopColor = c; v.style.borderBottomColor = c;
            v.style.borderLeftWidth = w; v.style.borderRightWidth = w; v.style.borderTopWidth = w + 1; v.style.borderBottomWidth = w;
            v.style.borderTopLeftRadius = radius; v.style.borderTopRightRadius = radius; v.style.borderBottomLeftRadius = radius; v.style.borderBottomRightRadius = radius;
        }
    }
}
