using DMI.Xenoarch.Core;
using DMI.Xenoarch.Objectives;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Phase 2 M2 objective tracker HUD: main expedition permanently at the top, up to two sides beneath it, a gold
    /// star on the focused objective (click to change focus), a contextual hint for the focused line, and a hide-optional
    /// toggle. Focus is not reordered. Top-left, under the helmet pods when the visor is up.
    /// </summary>
    public sealed class ObjectiveHud : MonoBehaviour
    {
        UIDocument _doc;
        VisualElement _root, _panel;
        string _scene;
        float _next;
        ObjectiveService _bound;

        void OnEnable()
        {
            if (_doc == null)
            {
                var host = new GameObject("[Objective HUD]");
                host.transform.SetParent(transform, false);
                _doc = host.AddComponent<UIDocument>();
                _doc.panelSettings = XenoarchUI.LoadPanelSettings();
                _doc.sortingOrder = 18;
            }
            var r = _doc.rootVisualElement;
            r.Clear();
            XenoarchUI.FillRoot(r);
            r.pickingMode = PickingMode.Ignore;
            var uss = Resources.Load<StyleSheet>("UI/Xenoarch");
            if (uss != null && !r.styleSheets.Contains(uss)) r.styleSheets.Add(uss);
            _root = r;
            _panel = new VisualElement();
            _panel.AddToClassList("xa-obj");
            _panel.pickingMode = PickingMode.Position;
            r.Add(_panel);
            XenoarchUI.Show(_panel, false);
        }

        void OnDisable()
        {
            Bind(null);
            _panel = null;
        }

        void Bind(ObjectiveService svc)
        {
            if (_bound == svc) return;
            if (_bound != null) _bound.Changed -= OnChanged;
            _bound = svc;
            if (_bound != null) _bound.Changed += OnChanged;
        }

        void OnChanged() => Render();

        void Update()
        {
            var rt = XenoarchRuntime.Instance;
            bool show = rt != null && rt.SessionActive && !(XenoarchRuntime.Scenes?.IsLoading ?? true) && !UIState.BlocksGameplay;
            var svc = show ? XenoarchRuntime.Session?.Objectives : null;
            Bind(svc);
            if (_panel == null) return;
            if (svc == null) { XenoarchUI.Show(_panel, false); return; }

            var scene = XenoarchRuntime.Session?.Location?.sceneName;
            if (scene != _scene || Time.unscaledTime >= _next)
            {
                _scene = scene;
                _next = Time.unscaledTime + 0.5f;
                Render();
            }
        }

        void Render()
        {
            if (_panel == null) return;
            var svc = _bound;
            if (svc == null) { XenoarchUI.Show(_panel, false); return; }

            var main = svc.Main();
            var sides = svc.Sides();
            bool any = main != null && (main.open || main.complete) && HasVisibleLine(main);
            if (!any && sides.Count == 0) { XenoarchUI.Show(_panel, false); return; }

            _panel.Clear();
            _panel.style.top = HudLayout.TopStart;
            _panel.style.left = HudLayout.Margin;

            // t246u Morpheus tracker: header, then one block per objective (star, title, kind badge, step count, %, thin bar).
            var header = new VisualElement(); header.AddToClassList("xa-obj__header");
            MUI.L(header, "OBJECTIVES", "xa-obj__header-title");
            var jr = new VisualElement(); jr.style.flexDirection = FlexDirection.Row; jr.style.alignItems = Align.Center;
            MUI.Key(jr, "J"); MUI.L(jr, "JOURNAL", "xa-m-legend__text");
            header.Add(jr);
            _panel.Add(header);

            _first = true;
            if (main != null) Draw(svc, main, true);
            foreach (var s in sides) Draw(svc, s, false);

            var focused = svc.Focused();
            var hint = focused != null ? focused.Prompt : null;
            if (!string.IsNullOrEmpty(hint))
            {
                var p = new Label("Hint: " + hint); p.AddToClassList("xa-obj__hint"); _panel.Add(p);
            }

            bool hasOptional = false;
            foreach (var v in svc.All()) if (v.optional && v.open && !v.complete) { hasOptional = true; break; }
            if (hasOptional)
            {
                var t = new Button(() => { svc.HideOptional = !svc.HideOptional; Render(); })
                    { text = svc.HideOptional ? "Show optional" : "Hide optional" };
                t.AddToClassList("xa-obj__toggle");
                _panel.Add(t);
            }

            XenoarchUI.Show(_panel, true);
        }

        bool _first;

        static bool HasVisibleLine(ObjectiveView v)
        {
            if (v == null) return false;
            if (v.complete) return true;
            foreach (var l in v.lines) if (!l.complete) return true;
            return v.lines.Count > 0;
        }

        void Draw(ObjectiveService svc, ObjectiveView v, bool isMain)
        {
            bool focused = svc.IsFocused(v.id);
            string focusCls = focused ? "xa-obj__focus" : "xa-obj__idle";

            var block = new VisualElement();
            block.AddToClassList("xa-obj__block");
            if (_first) { block.AddToClassList("xa-obj__block--first"); _first = false; }
            block.pickingMode = PickingMode.Position;
            var captured = v.id;
            block.RegisterCallback<ClickEvent>(e => { svc.SetFocus(captured); e.StopPropagation(); });

            var head = new VisualElement();
            head.AddToClassList("xa-obj__head");
            var star = XenoarchIcons.Glyph("star", "xa-obj__star");
            if (!focused) star.AddToClassList("xa-obj__star--hidden");
            head.Add(star);
            var title = new Label(v.title);
            title.AddToClassList(isMain ? "xa-obj__title" : "xa-obj__side");
            title.AddToClassList(focusCls);
            head.Add(title);
            MUI.Badge(head, isMain ? "Main" : (v.optional ? "Optional" : "Side"), isMain ? "main" : (v.optional ? null : "side"));
            block.Add(head);

            float pct = MUI.Progress(v, out int done, out int total);
            var meta = new VisualElement(); meta.AddToClassList("xa-obj__meta");
            MUI.L(meta, v.complete ? "Complete" : $"{done} / {total} steps");
            MUI.L(meta, Mathf.RoundToInt(pct * 100f) + "%");
            block.Add(meta);
            var bw = new VisualElement(); bw.AddToClassList("xa-obj__barwrap");
            MUI.Bar(bw, pct, !isMain, true);
            block.Add(bw);

            if (!v.complete)
            {
                foreach (var l in v.lines)
                {
                    if (l.complete) continue;
                    string text = l.label;
                    if (!string.IsNullOrEmpty(l.progress)) text += "  (" + l.progress + ")";
                    var lab = new Label(text); lab.AddToClassList("xa-obj__line");
                    lab.AddToClassList(focused ? "xa-obj__focus" : "xa-obj__idle");
                    block.Add(lab);
                    break; // next open step only
                }
            }
            _panel.Add(block);
        }
    }
}