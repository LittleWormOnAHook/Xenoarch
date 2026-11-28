using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// Landing / loading screen inside the scene loader's fade: a faint Milky Way, a distant galaxy,
    /// FTL star streaks and drifting planets, a destination-planet limb, the brand title
    /// ("Adventure of a Lifetime"), a status line (destination + load stage) and a spinning ring.
    /// Textures are loaded from Resources/UI/Loading (NASA Deep Star Maps crop + Solar System Scope bakes).
    /// Kept cheap: a handful of UITK quads plus Painter2D lines, dirtied only on the streak layer each tick.
    /// </summary>
    public sealed class LoadingScreenView
    {
        public const string BrandTitle = "Adventure of a Lifetime";

        const string ResMilky = "UI/Loading/Loading_MilkyWay";
        const string ResGalaxy = "UI/Loading/Loading_Galaxy";
        const string ResLimb = "UI/Loading/Loading_PlanetLimb";
        const string ResEarth = "UI/Loading/Loading_PlanetEarth";
        const string ResMoon = "UI/Loading/Loading_PlanetMoon";

        const int StarCount = 180;
        const int StreakCount = 140;
        const float EffectDim = 0.65f;

        readonly VisualElement _root, _streaks, _spinner, _barFill, _driftA, _driftB;
        readonly Label _title, _subtitle, _status;
        readonly Star[] _stars;
        readonly Streak[] _streaksData;

        float _angle, _time = 1.2f;
        string _destination = "", _statusDetail = "";

        struct Star
        {
            public float u, v, size, a;
            public Color color;
        }

        struct Streak
        {
            public float ang, d0, length, speed, phase, bright, thick;
            public Color color;
        }

        public LoadingScreenView(VisualElement parent)
        {
            _root = new VisualElement { name = "xa-loading", pickingMode = PickingMode.Ignore };
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.right = 0; _root.style.top = 0; _root.style.bottom = 0;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.FlexEnd;
            _root.style.paddingBottom = Length.Percent(4f);
            _root.style.backgroundColor = Color.black;
            _root.style.overflow = Overflow.Hidden;

            AddBackground(_root, ResMilky, "xa-loading-mw", 0.72f, Cover);

            var galaxy = AddBackground(_root, ResGalaxy, "xa-loading-galaxy", 1f, Contain);
            if (galaxy != null)
            {
                galaxy.style.left = Length.Percent(2f);
                galaxy.style.top = Length.Percent(4f);
                galaxy.style.width = Length.Percent(22f);
                galaxy.style.height = Length.Percent(32f);
                galaxy.style.right = StyleKeyword.Auto;
                galaxy.style.bottom = StyleKeyword.Auto;
            }

            _streaks = new VisualElement { name = "xa-loading-streaks", pickingMode = PickingMode.Ignore };
            Fill(_streaks);
            _streaks.generateVisualContent += DrawStreaks;
            _root.Add(_streaks);

            var limb = AddBackground(_root, ResLimb, "xa-loading-limb", 1f, Stretch);
            if (limb != null)
            {
                limb.style.top = StyleKeyword.Auto;
                limb.style.height = Length.Percent(38f);
            }

            _driftA = AddDriftPlanet(_root, ResEarth, "xa-loading-earth");
            _driftB = AddDriftPlanet(_root, ResMoon, "xa-loading-moon");

            _title = new Label { pickingMode = PickingMode.Ignore, text = BrandTitle };
            _title.style.fontSize = 34; _title.style.color = XenoarchPalette.Heading; _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.letterSpacing = 3;
            _title.style.marginBottom = 10;
            _root.Add(_title);

            _spinner = new VisualElement { pickingMode = PickingMode.Ignore };
            _spinner.style.width = 72; _spinner.style.height = 72; _spinner.style.marginBottom = 18;
            _spinner.generateVisualContent += DrawSpinner;
            _root.Add(_spinner);

            _subtitle = new Label { pickingMode = PickingMode.Ignore };
            _subtitle.style.fontSize = 18; _subtitle.style.color = XenoarchPalette.SecondaryText; _subtitle.style.marginTop = 0;
            _root.Add(_subtitle);
            _status = new Label { pickingMode = PickingMode.Ignore };
            _status.style.fontSize = 17; _status.style.color = XenoarchPalette.Text; _status.style.marginTop = 8;
            _root.Add(_status);
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.style.width = 420; bar.style.height = 6; bar.style.marginTop = 12; bar.style.backgroundColor = XenoarchPalette.PanelDark;
            _barFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _barFill.style.height = Length.Percent(100); _barFill.style.width = Length.Percent(0); _barFill.style.backgroundColor = XenoarchPalette.Bar;
            bar.Add(_barFill); _root.Add(bar);
            parent.Add(_root);

            _stars = BuildStars();
            _streaksData = BuildStreaks();
        }

        static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0;
        }

        // Scale modes: cover the element, contain inside it, or stretch to fill.
        const int Cover = 0, Contain = 1, Stretch = 2;

        static VisualElement AddBackground(VisualElement parent, string resource, string name, float opacity, int scale)
        {
            var tex = Resources.Load<Texture2D>(resource);
            if (tex == null)
            {
                Debug.LogWarning($"[Xenoarch] Loading screen missing Resources/{resource}");
                return null;
            }
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            Fill(e);
            e.style.backgroundImage = new StyleBackground(tex);
            e.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            if (scale == Stretch) e.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
            else if (scale == Contain) e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            else e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            e.style.opacity = opacity;
            parent.Add(e);
            return e;
        }

        static VisualElement AddDriftPlanet(VisualElement parent, string resource, string name)
        {
            var e = AddBackground(parent, resource, name, 1f, Contain);
            if (e == null) return null;
            e.style.right = StyleKeyword.Auto;
            e.style.bottom = StyleKeyword.Auto;
            e.style.width = 64; e.style.height = 64;
            return e;
        }

        /// <summary>Stores the destination label (used on the status line). The on-screen title is always <see cref="BrandTitle"/>.</summary>
        public void SetTitle(string destination)
        {
            _destination = destination ?? "";
            _title.text = BrandTitle;
            RefreshStatus();
        }

        public void SetSubtitle(string t)
        {
            _subtitle.text = t ?? "";
            _subtitle.style.display = string.IsNullOrEmpty(t) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void SetStatus(string s)
        {
            _statusDetail = s ?? "";
            RefreshStatus();
        }

        /// <summary>Loading bar fill (0..1), same value as the percentage in the status line.</summary>
        public void SetProgress(float p01) => _barFill.style.width = Length.Percent(Mathf.Clamp01(p01) * 100f);
        public void SetVisible(bool on)
        {
            _root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on) _time = 1.2f;
        }

        void RefreshStatus()
        {
            string dest = UseDestinationInStatus(_destination) ? _destination : "";
            if (string.IsNullOrEmpty(dest)) _status.text = _statusDetail;
            else if (string.IsNullOrEmpty(_statusDetail)) _status.text = dest;
            else _status.text = dest + " - " + _statusDetail;
        }

        static bool UseDestinationInStatus(string dest)
        {
            if (string.IsNullOrEmpty(dest)) return false;
            if (dest == BrandTitle) return false;
            if (dest.Equals("Main menu", System.StringComparison.OrdinalIgnoreCase)) return false;
            if (dest.Equals("Xenoarch", System.StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        public void Tick(float dt)
        {
            _time += dt;
            _angle = (_angle + dt * 300f) % 360f;
            _spinner.style.rotate = new Rotate(new Angle(_angle, AngleUnit.Degree));
            PlaceDrift(_driftA, _time, 28f * Mathf.Deg2Rad, 0.00f, 38f, 96f, 0.38f, 0.055f);
            PlaceDrift(_driftB, _time, 325f * Mathf.Deg2Rad, 0.35f, 26f, 72f, 0.48f, 0.055f);
            _streaks.MarkDirtyRepaint();
        }

        void PlaceDrift(VisualElement e, float t, float ang, float phase, float minPx, float maxPx, float d0, float dSpeed)
        {
            if (e == null) return;
            var r = _root.contentRect;
            if (r.width < 2f) return;
            float u = Mathf.Repeat((t - 1.2f) * dSpeed + phase, 1.15f);
            float fade = 1f;
            if (u > 1f) fade = Mathf.Clamp01(1f - (u - 1f) / 0.15f);
            else if (u < 0.08f) fade = u / 0.08f;
            float dist = (d0 + u * 0.42f) * Mathf.Sqrt(r.width * r.width + r.height * r.height) * 0.5f;
            float cx = r.width * 0.50f, cy = r.height * 0.46f;
            float x = cx + Mathf.Cos(ang) * dist;
            float y = cy + Mathf.Sin(ang) * dist;
            float size = Mathf.Lerp(minPx, maxPx, Mathf.Clamp01(u));
            e.style.left = x - size * 0.5f;
            e.style.top = y - size * 0.5f;
            e.style.width = size;
            e.style.height = size;
            e.style.opacity = fade;
            e.style.display = fade <= 0.01f ? DisplayStyle.None : DisplayStyle.Flex;
        }

        static Star[] BuildStars()
        {
            var rng = new System.Random(4242);
            var list = new Star[StarCount];
            for (int i = 0; i < StarCount; i++)
            {
                float temp = (float)rng.NextDouble();
                Color c = temp < 0.25f ? new Color(1f, 0.82f, 0.62f) : temp > 0.75f ? new Color(0.75f, 0.85f, 1f) : Color.white;
                list[i] = new Star
                {
                    u = (float)rng.NextDouble(),
                    v = (float)rng.NextDouble() * 0.88f, // leave the planet limb mostly clear
                    size = 0.6f + (float)rng.NextDouble() * 1.4f,
                    a = 0.25f + (float)rng.NextDouble() * 0.55f,
                    color = c
                };
            }
            return list;
        }

        static Streak[] BuildStreaks()
        {
            var rng = new System.Random(11);
            var list = new Streak[StreakCount];
            for (int i = 0; i < StreakCount; i++)
            {
                float temp = (float)rng.NextDouble();
                Color c = temp < 0.2f ? new Color(1f, 0.86f, 0.68f) : temp > 0.8f ? new Color(0.72f, 0.88f, 1f) : new Color(0.95f, 0.96f, 1f);
                list[i] = new Streak
                {
                    ang = (float)rng.NextDouble() * Mathf.PI * 2f,
                    d0 = 0.02f + Mathf.Pow((float)rng.NextDouble(), 0.7f) * 0.15f,
                    length = 0.08f + (float)rng.NextDouble() * 0.50f,
                    speed = 0.35f + (float)rng.NextDouble() * 1.15f,
                    phase = (float)rng.NextDouble(),
                    bright = 0.35f + (float)rng.NextDouble() * 0.65f,
                    thick = 1.0f + (float)rng.NextDouble() * 1.6f,
                    color = c
                };
            }
            return list;
        }

        void DrawStreaks(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = ctx.visualElement.contentRect;
            if (r.width < 2f) return;
            float cx = r.width * 0.50f, cy = r.height * 0.46f;
            float rmax = Mathf.Sqrt(Mathf.Max(cx, r.width - cx) * Mathf.Max(cx, r.width - cx) + Mathf.Max(cy, r.height - cy) * Mathf.Max(cy, r.height - cy));

            for (int i = 0; i < _stars.Length; i++)
            {
                var s = _stars[i];
                p.fillColor = XenoarchPalette.A(s.color, s.a * EffectDim);
                p.BeginPath();
                p.Arc(new Vector2(s.u * r.width, s.v * r.height), s.size, 0f, 360f);
                p.Fill();
            }

            p.lineCap = LineCap.Round;
            for (int i = 0; i < _streaksData.Length; i++)
            {
                var s = _streaksData[i];
                float u = (_streaksData[i].phase + _time * s.speed * 0.28f) % 1.2f;
                if (u > 1.05f) continue;
                float fade = 1f;
                if (u < 0.08f) fade = u / 0.08f;
                else if (u > 0.85f) fade = Mathf.Max(0f, 1f - (u - 0.85f) / 0.20f);
                float dHead = (s.d0 + u * 1.05f) * rmax;
                float dTail = Mathf.Max(8f, dHead - (s.length * (0.4f + 1.6f * u)) * rmax);
                float ca = Mathf.Cos(s.ang), sa = Mathf.Sin(s.ang);
                var tail = new Vector2(cx + ca * dTail, cy + sa * dTail);
                var head = new Vector2(cx + ca * dHead, cy + sa * dHead);
                var col = s.color;
                col.a = Mathf.Clamp01(s.bright * fade * EffectDim);
                p.lineWidth = s.thick;
                p.strokeColor = col;
                p.BeginPath();
                p.MoveTo(tail);
                p.LineTo(head);
                p.Stroke();
                // bright head
                p.fillColor = XenoarchPalette.A(s.color, Mathf.Clamp01(s.bright * fade * 0.9f * EffectDim));
                p.BeginPath();
                p.Arc(head, Mathf.Max(0.8f, s.thick * 0.45f), 0f, 360f);
                p.Fill();
            }
        }

        static void DrawSpinner(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = ctx.visualElement.contentRect;
            var c = r.center; float rad = Mathf.Min(r.width, r.height) * 0.42f;
            // Soft dark disc so the cyan/orange rings stay readable on the FTL streaks.
            p.fillColor = new Color(0f, 0f, 0f, 0.55f);
            p.BeginPath(); p.Arc(c, rad + 10f, 0f, 360f); p.Fill();
            p.lineWidth = 5f; p.lineCap = LineCap.Round;
            p.strokeColor = XenoarchPalette.A(XenoarchPalette.Secondary, 0.25f);
            p.BeginPath(); p.Arc(c, rad, 0f, 360f); p.Stroke();
            p.strokeColor = XenoarchPalette.Highlight;
            p.BeginPath(); p.Arc(c, rad, -90f, 150f); p.Stroke();
            p.strokeColor = XenoarchPalette.Primary;
            p.BeginPath(); p.Arc(c, rad * 0.62f, 90f, 250f); p.Stroke();
        }
    }
}
