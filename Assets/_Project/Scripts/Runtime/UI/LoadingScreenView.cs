using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Landing / loading screen content built inside the scene loader's black fade: a starfield with a planet rim glow,
    /// the destination title, a status line and a spinning loading ring. Pure UITK, no assets.
    /// </summary>
    public sealed class LoadingScreenView
    {
        readonly VisualElement _root, _spinner;
        readonly Label _title, _status;
        float _angle;

        public LoadingScreenView(VisualElement parent)
        {
            _root = new VisualElement { name = "xa-loading", pickingMode = PickingMode.Ignore };
            _root.style.position = Position.Absolute; _root.style.left = 0; _root.style.right = 0; _root.style.top = 0; _root.style.bottom = 0;
            _root.style.alignItems = Align.Center; _root.style.justifyContent = Justify.Center;

            var stars = new VisualElement { pickingMode = PickingMode.Ignore };
            stars.style.position = Position.Absolute; stars.style.left = 0; stars.style.right = 0; stars.style.top = 0; stars.style.bottom = 0;
            stars.generateVisualContent += DrawBackdrop;
            _root.Add(stars);

            _spinner = new VisualElement { pickingMode = PickingMode.Ignore };
            _spinner.style.width = 72; _spinner.style.height = 72; _spinner.style.marginBottom = 26;
            _spinner.generateVisualContent += DrawSpinner;
            _root.Add(_spinner);

            _title = new Label { pickingMode = PickingMode.Ignore };
            _title.style.fontSize = 34; _title.style.color = new Color(0.82f, 0.93f, 1f); _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.letterSpacing = 3;
            _root.Add(_title);
            _status = new Label { pickingMode = PickingMode.Ignore };
            _status.style.fontSize = 17; _status.style.color = new Color(0.95f, 0.72f, 0.35f); _status.style.marginTop = 8;
            _root.Add(_status);
            parent.Add(_root);
        }

        public void SetTitle(string t) => _title.text = t ?? "";
        public void SetStatus(string s) => _status.text = s ?? "";
        public void SetVisible(bool on) => _root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        public void Tick(float dt)
        {
            _angle = (_angle + dt * 300f) % 360f;
            _spinner.style.rotate = new Rotate(new Angle(_angle, AngleUnit.Degree));
        }

        static void DrawSpinner(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = ctx.visualElement.contentRect;
            var c = r.center; float rad = Mathf.Min(r.width, r.height) * 0.42f;
            p.lineWidth = 5f; p.lineCap = LineCap.Round;
            p.strokeColor = new Color(0.3f, 0.75f, 0.95f, 0.18f);
            p.BeginPath(); p.Arc(c, rad, 0f, 360f); p.Stroke();
            p.strokeColor = new Color(0.35f, 0.85f, 1f, 0.95f);
            p.BeginPath(); p.Arc(c, rad, -90f, 150f); p.Stroke();
            p.strokeColor = new Color(1f, 0.72f, 0.3f, 0.9f);
            p.BeginPath(); p.Arc(c, rad * 0.62f, 90f, 250f); p.Stroke();
        }

        static void DrawBackdrop(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = ctx.visualElement.contentRect;
            if (r.width < 2f) return;
            var rng = new System.Random(4242);
            for (int i = 0; i < 160; i++)
            {
                float x = (float)rng.NextDouble() * r.width, y = (float)rng.NextDouble() * r.height;
                float s = 0.6f + (float)rng.NextDouble() * 1.4f, a = 0.25f + (float)rng.NextDouble() * 0.6f;
                p.fillColor = new Color(0.85f, 0.9f, 1f, a);
                p.BeginPath(); p.Arc(new Vector2(x, y), s, 0f, 360f); p.Fill();
            }
            // planet rim glow along the bottom
            var centre = new Vector2(r.width * 0.5f, r.height * 2.35f);
            float pr = r.height * 1.6f;
            for (int k = 0; k < 6; k++)
            {
                p.lineWidth = 2f + k * 5f;
                p.strokeColor = new Color(0.95f, 0.55f, 0.25f, 0.10f - k * 0.014f);
                p.BeginPath(); p.Arc(centre, pr + k * 2f, 225f, 315f); p.Stroke();
            }
        }
    }
}
