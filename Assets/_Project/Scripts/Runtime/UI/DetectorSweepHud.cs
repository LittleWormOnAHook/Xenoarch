using DMI.Xenoarch.Exploration;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Compact half-disc detector (t169u mockup B, approved via Anthony's HUD mockup). Sits above Suit Integrity
    /// While a refined-mode left-mouse sweep is active; hidden when the scanner is holstered or the sweep ends.
    /// Forward is the top of the disc and it turns with the view. Blips use icon_research_unknown; the closest
    /// in-beam contact blinks amber. SIGNAL % and distance sit to the right of the disc.
    /// </summary>
    public sealed class DetectorSweepHud : MonoBehaviour
    {
        const float Width = 360f, Height = 188f, Radius = 118f;
        UIDocument _doc;
        VisualElement _root, _disc, _blips;
        Label _title, _signal, _dist;
        readonly VisualElement[] _dots = new VisualElement[16];
        bool _built;

        void Start()
        {
            var go = new GameObject("[Detector Sweep HUD]");
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = XenoarchUI.LoadPanelSettings();
            _doc.sortingOrder = 12;
        }

        void Build()
        {
            var r = _doc != null ? _doc.rootVisualElement : null;
            if (r == null) return;
            XenoarchUI.FillRoot(r); r.pickingMode = PickingMode.Ignore;
            XenoarchIcons.EnsureStyles(r);
            _root = new VisualElement { name = "detector-sweep", pickingMode = PickingMode.Ignore };
            _root.AddToClassList("xa-detector");
            _root.style.position = Position.Absolute;
            _root.style.width = Width;
            _root.style.height = Height;
            _root.style.left = Length.Percent(50);
            _root.style.marginLeft = -Width * 0.5f;
            _root.style.bottom = HudLayout.DetectorBottom;
            _root.style.alignItems = Align.Center;

            _title = new Label("DETECTOR") { pickingMode = PickingMode.Ignore };
            _title.AddToClassList("xa-detector__title");
            _root.Add(_title);

            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexEnd;
            row.style.justifyContent = Justify.Center;

            _disc = new VisualElement { pickingMode = PickingMode.Ignore };
            _disc.AddToClassList("xa-detector__disc");
            _disc.style.width = Radius * 2f + 8f;
            _disc.style.height = Radius + 10f;
            _disc.generateVisualContent += PaintDisc;
            _blips = new VisualElement { pickingMode = PickingMode.Ignore };
            _blips.style.position = Position.Absolute;
            _blips.style.left = 0; _blips.style.top = 0; _blips.style.right = 0; _blips.style.bottom = 0;
            for (int i = 0; i < _dots.Length; i++)
            {
                var d = XenoarchIcons.Glyph("research-unknown");
                d.AddToClassList("xa-detector__blip");
                d.style.position = Position.Absolute;
                d.style.width = 18; d.style.height = 18;
                d.style.display = DisplayStyle.None;
                _blips.Add(d);
                _dots[i] = d;
            }
            _disc.Add(_blips);
            row.Add(_disc);

            var read = new VisualElement { pickingMode = PickingMode.Ignore };
            read.style.marginLeft = 10;
            read.style.marginBottom = 8;
            read.style.width = 110;
            _signal = new Label("SIGNAL  --") { pickingMode = PickingMode.Ignore };
            _signal.AddToClassList("xa-detector__signal");
            _dist = new Label("") { pickingMode = PickingMode.Ignore };
            _dist.AddToClassList("xa-detector__dist");
            read.Add(_signal); read.Add(_dist);
            row.Add(read);

            _root.Add(row);
            r.Add(_root);
            XenoarchUI.Show(_root, false);
            _built = true;
        }

        void PaintDisc(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var rect = _disc.contentRect;
            // Diameter sits on the bottom edge; the arc bulges upward. Origin is the midpoint of that diameter.
            var c = new Vector2(rect.width * 0.5f, rect.height - 4f);
            float R = Radius;

            // Fill
            p.fillColor = new Color(0.02f, 0.06f, 0.09f, 0.72f);
            p.BeginPath();
            Arc(p, c, R, -90f, 90f);
            p.LineTo(c + Dir(90f) * R);
            p.LineTo(c + Dir(-90f) * R);
            p.ClosePath();
            p.Fill();

            var cyan = new Color(0.35f, 0.9f, 1f, 0.85f);
            p.strokeColor = cyan;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            // Range rings
            for (int i = 1; i <= 3; i++)
            {
                p.lineWidth = i == 3 ? 2f : 1.1f;
                p.strokeColor = new Color(0.35f, 0.9f, 1f, i == 3 ? 0.9f : 0.35f);
                p.BeginPath();
                Arc(p, c, R * (i / 3f), -90f, 90f);
                p.Stroke();
            }
            // Radial ticks every 30°
            p.lineWidth = 1f;
            p.strokeColor = new Color(0.35f, 0.9f, 1f, 0.35f);
            for (int a = -90; a <= 90; a += 30)
            {
                p.BeginPath();
                p.MoveTo(c + Dir(a) * R * 0.18f);
                p.LineTo(c + Dir(a) * R);
                p.Stroke();
            }
            // Diameter
            p.lineWidth = 1.6f;
            p.strokeColor = cyan;
            p.BeginPath();
            p.MoveTo(c + Dir(-90f) * R);
            p.LineTo(c + Dir(90f) * R);
            p.Stroke();
            // Animated sweep beam (loops across the forward half).
            float beam = Mathf.PingPong(Time.unscaledTime * 70f, 180f) - 90f;
            p.fillColor = new Color(0.35f, 0.9f, 1f, 0.10f);
            p.BeginPath();
            p.MoveTo(c);
            Arc(p, c, R, beam - 8f, beam + 8f);
            p.LineTo(c);
            p.ClosePath();
            p.Fill();
            p.strokeColor = new Color(0.55f, 0.95f, 1f, 0.7f);
            p.lineWidth = 1.4f;
            p.BeginPath();
            p.MoveTo(c);
            p.LineTo(c + Dir(beam) * R);
            p.Stroke();
        }

        static Vector2 Dir(float headingDeg)
        {
            // heading 0 = forward = up in UI (-Y). Positive heading is to the right.
            float r = headingDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), -Mathf.Cos(r));
        }

        static void Arc(Painter2D p, Vector2 c, float radius, float fromDeg, float toDeg)
        {
            int steps = Mathf.Max(8, Mathf.RoundToInt(Mathf.Abs(toDeg - fromDeg) / 6f));
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)steps);
                var pt = c + Dir(a) * radius;
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
        }

        void Update()
        {
            if (_doc == null) return;
            if (_built && (_root == null || _root.panel == null)) { _built = false; _root?.RemoveFromHierarchy(); }
            if (!_built) Build();
            if (!_built) return;

            var tool = SurfaceScannerTool.Active;
            bool show = tool != null && tool.Sweeping && !UIState.IsOpen(Panels.Loading) && !UIState.BlocksGameplay;
            XenoarchUI.Show(_root, show);
            if (!show) return;
            _root.style.bottom = HudLayout.DetectorBottom;
            _disc.MarkDirtyRepaint();

            var blips = tool.SweepBlips;
            int used = 0;
            for (int i = 0; i < blips.Count && used < _dots.Length; i++)
            {
                var b = blips[i];
                if (b.rangeM > tool.sweepReach + 0.05f || b.headingDeg < -92f || b.headingDeg > 92f) continue;
                var dot = _dots[used++];
                dot.style.display = DisplayStyle.Flex;
                float t = Mathf.Clamp01(b.rangeM / Mathf.Max(0.5f, tool.sweepReach));
                var pos = new Vector2((_disc.resolvedStyle.width) * 0.5f, _disc.resolvedStyle.height - 4f) + Dir(b.headingDeg) * Radius * t;
                float size = b.closest ? 22f : 16f;
                dot.style.width = size; dot.style.height = size;
                dot.style.left = pos.x - size * 0.5f;
                dot.style.top = pos.y - size * 0.5f;
                if (b.closest)
                {
                    float blink = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.Lerp(4f, 12f, b.signal01)));
                    dot.style.unityBackgroundImageTintColor = new Color(1f, 0.72f, 0.15f, blink);
                    dot.style.opacity = blink;
                }
                else
                {
                    float a = b.inBeam ? Mathf.Lerp(0.55f, 1f, b.signal01) : 0.4f;
                    dot.style.unityBackgroundImageTintColor = new Color(1f, 1f, 1f, a);
                    dot.style.opacity = a;
                }
            }
            for (int i = used; i < _dots.Length; i++) _dots[i].style.display = DisplayStyle.None;

            if (tool.SweepSignal > 0f && tool.SweepAimed)
            {
                _signal.text = $"SIGNAL  {Mathf.RoundToInt(tool.SweepSignal * 100f)}%";
                _dist.text = $"{tool.SweepDistance:0.0} m";
            }
            else
            {
                _signal.text = tool.SweepAimed ? "SIGNAL  --" : "AIM AT GROUND";
                _dist.text = "";
            }
        }
    }
}
