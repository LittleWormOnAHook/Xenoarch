using DMI.Xenoarch.Core;
using DMI.Xenoarch.Objectives;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Phase 2 M2 objective tracker HUD: one main objective and up to two sides, plus a contextual hint for the
    /// current line and a hide-optional toggle. Top-left, under the helmet pods when the visor is up.
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

            if (main != null) Draw(main, true);
            if (sides.Count > 0)
            {
                var h = new Label("OPTIONAL"); h.AddToClassList("xa-obj__sub"); _panel.Add(h);
                foreach (var s in sides) Draw(s, false);
            }

            var hint = main != null ? main.Prompt : null;
            if (string.IsNullOrEmpty(hint) && sides.Count > 0) hint = sides[0].Prompt;
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

        static bool HasVisibleLine(ObjectiveView v)
        {
            if (v == null) return false;
            if (v.complete) return true;
            foreach (var l in v.lines) if (!l.complete) return true;
            return v.lines.Count > 0;
        }

        void Draw(ObjectiveView v, bool isMain)
        {
            var title = new Label(isMain ? v.title.ToUpperInvariant() : v.title);
            title.AddToClassList(isMain ? "xa-obj__title" : "xa-obj__side");
            _panel.Add(title);
            if (v.complete)
            {
                var done = new Label("Complete"); done.AddToClassList("xa-obj__done"); _panel.Add(done);
                return;
            }
            foreach (var l in v.lines)
            {
                if (isMain && l.complete) continue; // HUD focuses on the current required lines
                string text = l.complete ? l.label : l.label;
                if (!string.IsNullOrEmpty(l.progress) && !l.complete) text += "  (" + l.progress + ")";
                var row = new VisualElement(); row.AddToClassList("xa-obj__row");
                var mark = new Label(l.complete ? "\u2713" : ">"); mark.AddToClassList(l.complete ? "xa-obj__mark--done" : "xa-obj__mark");
                row.Add(mark);
                var lab = new Label(text); lab.AddToClassList(l.complete ? "xa-obj__line--done" : "xa-obj__line");
                row.Add(lab);
                _panel.Add(row);
                if (isMain && !l.complete) break; // show the current required step, not the whole chain
            }
        }
    }
}