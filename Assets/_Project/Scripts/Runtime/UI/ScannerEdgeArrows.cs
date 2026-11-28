using System.Collections.Generic;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Screen-edge arrows (approved mockup 02_scanner_edge_arrows, t408s1998). Replaces the old gold crosshair chevron.
    /// Each handheld scanner pulse flashes one arrow per known target in range (sites, caches, POIs; nearest 8, coloured by
    /// type) around the edge of the screen. They hold and fade over <see cref="CompassConfig.ArrowSeconds"/> (3.5 s), then go.
    /// A green arrow for the focused objective stays on while its destination is known. The ring sits at the regular
    /// screen edges, below the compass block, and renders above the helmet mask. Every element is PickingMode.Ignore.
    /// </summary>
    public sealed class ScannerEdgeArrows : MonoBehaviour
    {
        const float Inset = 48f, TopInset = 124f, Size = 28f, ObjectiveHideM = 6f;

        sealed class ArrowView
        {
            public VisualElement el; public Label lab; public Color color; public int lastDist = -1; public string lastName;
        }
        struct Contact { public CompassKind kind; public Vector3 pos; public float dist0; }

        UIDocument _doc;
        VisualElement _root;
        readonly ArrowView[] _views = new ArrowView[CompassConfig.MaxPulseArrows + 1];   // last one is the objective
        readonly List<Contact> _contacts = new List<Contact>(CompassConfig.MaxPulseArrows);
        float _pulseAt = -999f;
        bool _built;

        static readonly System.Comparison<Contact> NearFirst = (a, b) => a.dist0.CompareTo(b.dist0);

        void OnEnable() { SurfaceScannerTool.PulseFired += OnPulse; }
        void OnDisable() { SurfaceScannerTool.PulseFired -= OnPulse; }

        void Start()
        {
            var go = new GameObject("[Scanner Edge Arrows]");
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 19;   // above the helmet mask (5), below the compass (20), minimap (40) and windows
        }

        static VisualElement E() => new VisualElement { pickingMode = PickingMode.Ignore };

        void Build()
        {
            var r = _doc != null ? _doc.rootVisualElement : null;
            if (r == null) return;
            XenoarchUI.FillRoot(r); r.pickingMode = PickingMode.Ignore;
            _root = E();
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.top = 0; _root.style.right = 0; _root.style.bottom = 0;
            r.Add(_root);
            for (int i = 0; i < _views.Length; i++)
            {
                var v = new ArrowView { el = E(), lab = new Label { pickingMode = PickingMode.Ignore } };
                v.el.style.position = Position.Absolute; v.el.style.width = Size; v.el.style.height = Size; v.el.style.display = DisplayStyle.None;
                var view = v;
                v.el.generateVisualContent += ctx => Paint(ctx, view);
                v.lab.style.position = Position.Absolute; v.lab.style.width = 90f; v.lab.style.height = 30f; v.lab.style.display = DisplayStyle.None;
                v.lab.style.fontSize = 11f; v.lab.style.unityFontStyleAndWeight = FontStyle.Bold; v.lab.style.unityTextAlign = TextAnchor.MiddleCenter;
                v.lab.style.whiteSpace = WhiteSpace.Normal;
                v.lab.style.paddingLeft = v.lab.style.paddingRight = v.lab.style.paddingTop = v.lab.style.paddingBottom = 0;
                v.lab.style.marginLeft = v.lab.style.marginRight = v.lab.style.marginTop = v.lab.style.marginBottom = 0;
                v.lab.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 0f, color = new Color(0f, 0f, 0f, 0.85f) };
                _root.Add(v.el); _root.Add(v.lab); _views[i] = v;
            }
            _built = true;
        }

        static void Paint(MeshGenerationContext ctx, ArrowView v)
        {
            var p = ctx.painter2D;
            p.fillColor = v.color; p.strokeColor = new Color(40f / 255f, 9f / 255f, 14f / 255f, 0.9f);
            p.lineWidth = 2f; p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(new Vector2(14f, 2f)); p.LineTo(new Vector2(25f, 25f)); p.LineTo(new Vector2(14f, 19f)); p.LineTo(new Vector2(3f, 25f)); p.ClosePath();
            p.Fill(); p.Stroke();
        }

        void OnPulse()
        {
            CompassSource.Refresh(true);
            _contacts.Clear(); _pulseAt = Time.time;
            if (!CompassSource.OnSurface || !CompassSource.View(out var pos, out _)) return;
            float range = ScannerStats.HandheldRange() * CompassConfig.PulseRangeMult;
            foreach (var m in CompassSource.Marks)
            {
                if (m.kind != CompassKind.Site && m.kind != CompassKind.Cache && m.kind != CompassKind.Poi) continue;
                float d = CompassSource.Flat(pos, m.pos);
                if (d > range) continue;
                _contacts.Add(new Contact { kind = m.kind, pos = m.pos, dist0 = d });
            }
            _contacts.Sort(NearFirst);
            if (_contacts.Count > CompassConfig.MaxPulseArrows) _contacts.RemoveRange(CompassConfig.MaxPulseArrows, _contacts.Count - CompassConfig.MaxPulseArrows);
        }

        void Update()
        {
            if (_doc == null) return;
            if (_built && (_root == null || _root.panel == null)) { _built = false; _root?.RemoveFromHierarchy(); }
            if (!_built) Build();
            if (!_built) return;

            CompassSource.Refresh();
            Vector3 pos = default; float yaw = 0f;
            bool show = CompassSource.HudVisible() && CompassSource.OnSurface && CompassSource.View(out pos, out yaw);
            XenoarchUI.Show(_root, show);
            if (!show) return;

            float W = _root.resolvedStyle.width, H = _root.resolvedStyle.height;
            if (!(W > 100f) || !(H > 100f)) { W = CompassConfig.RefW; H = CompassConfig.RefH; }

            float age = Time.time - _pulseAt;
            float life = CompassConfig.ArrowSeconds, fade = CompassConfig.ArrowFadeSeconds;
            int used = 0;
            if (age >= 0f && age < life)
            {
                float a = age <= life - fade ? 1f : Mathf.Clamp01((life - age) / fade);
                for (int i = 0; i < _contacts.Count && used < CompassConfig.MaxPulseArrows; i++)
                {
                    var c = _contacts[i];
                    Place(_views[used++], c.kind, c.pos, pos, yaw, W, H, a);
                }
            }
            for (int i = used; i < CompassConfig.MaxPulseArrows; i++) Hide(_views[i]);

            // persistent green objective arrow
            var ov = _views[_views.Length - 1];
            bool objShown = false;
            var marks = CompassSource.Marks;
            for (int i = 0; i < marks.Count; i++)
            {
                if (marks[i].kind != CompassKind.Objective) continue;
                if (CompassSource.Flat(pos, marks[i].pos) > ObjectiveHideM) { Place(ov, CompassKind.Objective, marks[i].pos, pos, yaw, W, H, 1f); objShown = true; }
                break;
            }
            if (!objShown) Hide(ov);
        }

        static void Hide(ArrowView v)
        {
            if (v.el.style.display != DisplayStyle.None) { v.el.style.display = DisplayStyle.None; v.lab.style.display = DisplayStyle.None; }
        }

        static void Place(ArrowView v, CompassKind kind, Vector3 target, Vector3 from, float yaw, float W, float H, float alpha)
        {
            float bearing = Mathf.Atan2(target.x - from.x, target.z - from.z) * Mathf.Rad2Deg;
            float rel = Mathf.DeltaAngle(yaw, bearing);
            float rad = rel * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));   // 0 = up, + = right (UI y points down)
            Vector2 c = new Vector2(W * 0.5f, H * 0.5f);
            float l = Inset, r = W - Inset, t = TopInset, b = H - Inset;
            float tx = dir.x > 1e-4f ? (r - c.x) / dir.x : dir.x < -1e-4f ? (l - c.x) / dir.x : float.MaxValue;
            float ty = dir.y > 1e-4f ? (b - c.y) / dir.y : dir.y < -1e-4f ? (t - c.y) / dir.y : float.MaxValue;
            float s = Mathf.Min(tx, ty);
            Vector2 p = c + dir * s - dir * 8f;

            var col = CompassConfig.ColorOf(kind);
            if (v.color != col) { v.color = col; v.el.MarkDirtyRepaint(); }
            v.el.style.display = DisplayStyle.Flex; v.lab.style.display = DisplayStyle.Flex;
            v.el.style.left = p.x - Size * 0.5f; v.el.style.top = p.y - Size * 0.5f;
            v.el.style.rotate = new Rotate(new Angle(rel, AngleUnit.Degree));
            v.el.style.opacity = alpha;
            Vector2 lp = p - dir * 30f;
            v.lab.style.left = lp.x - 45f; v.lab.style.top = lp.y - 15f;
            v.lab.style.color = col; v.lab.style.opacity = alpha;
            int dist = Mathf.RoundToInt(CompassSource.Flat(from, target));
            string name = CompassConfig.NameOf(kind);
            if (dist != v.lastDist || name != v.lastName) { v.lastDist = dist; v.lastName = name; v.lab.text = name + "\n" + dist + " m"; }
        }
    }
}