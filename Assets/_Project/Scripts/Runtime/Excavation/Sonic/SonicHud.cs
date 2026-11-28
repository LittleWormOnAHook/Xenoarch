using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// Energy and heat bars, dial read-out and head-shape chips for the sonic tools (mockup 03). Built into the DigTool
    /// panel with the Xenoarch palette; nothing here changes gameplay.
    /// </summary>
    public sealed class SonicHud
    {
        VisualElement _bars, _strip, _eFill, _hFill;
        Label _ePct, _hPct, _dials, _hint;
        readonly Label[] _chips = new Label[4];
        static readonly string[] ChipNames = { "SPHERE", "DISC", "CUBE", "CONE" };

        public static SonicHud Build(VisualElement panel, VisualElement root)
        {
            var h = new SonicHud();
            h._dials = new Label { pickingMode = PickingMode.Ignore };
            h._dials.style.color = XenoarchPalette.Highlight; h._dials.style.fontSize = 15; h._dials.style.marginTop = 2;
            h._bars = new VisualElement { pickingMode = PickingMode.Ignore };
            h._bars.style.marginTop = 3;
            h._bars.Add(Row("ENERGY", XenoarchPalette.Highlight, out h._eFill, out h._ePct));
            h._bars.Add(Row("HEAT", XenoarchPalette.Primary, out h._hFill, out h._hPct));
            h._hint = new Label { pickingMode = PickingMode.Ignore };
            h._hint.style.color = XenoarchPalette.SecondaryText; h._hint.style.fontSize = 13; h._hint.style.marginTop = 3;
            h._hint.style.whiteSpace = WhiteSpace.Normal;
            panel.Add(h._dials); panel.Add(h._bars); panel.Add(h._hint);

            h._strip = new VisualElement { pickingMode = PickingMode.Ignore };
            h._strip.style.position = Position.Absolute; h._strip.style.left = 0; h._strip.style.right = 0; h._strip.style.top = Length.Percent(57);
            h._strip.style.flexDirection = FlexDirection.Row; h._strip.style.justifyContent = Justify.Center;
            for (int i = 0; i < 4; i++)
            {
                var c = new Label(ChipNames[i]) { pickingMode = PickingMode.Ignore };
                c.style.fontSize = 13; c.style.marginLeft = 3; c.style.marginRight = 3;
                c.style.paddingLeft = 8; c.style.paddingRight = 8; c.style.paddingTop = 2; c.style.paddingBottom = 2;
                c.style.borderTopWidth = c.style.borderBottomWidth = c.style.borderLeftWidth = c.style.borderRightWidth = 1;
                h._chips[i] = c; h._strip.Add(c);
            }
            root.Add(h._strip);
            XenoarchUI.Show(h._bars, false); XenoarchUI.Show(h._strip, false); XenoarchUI.Show(h._dials, false); XenoarchUI.Show(h._hint, false);
            return h;
        }

        static VisualElement Row(string name, Color fill, out VisualElement fillEl, out Label pct)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; row.style.marginTop = 2;
            var l = new Label(name) { pickingMode = PickingMode.Ignore };
            l.style.width = 70; l.style.fontSize = 13; l.style.color = XenoarchPalette.SecondaryText;
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.flexGrow = 1; track.style.height = 7; track.style.minWidth = 160;
            track.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.Text, 0.18f);
            fillEl = new VisualElement { pickingMode = PickingMode.Ignore };
            fillEl.style.height = Length.Percent(100); fillEl.style.backgroundColor = fill;
            track.Add(fillEl);
            pct = new Label("0%") { pickingMode = PickingMode.Ignore };
            pct.style.width = 46; pct.style.fontSize = 13; pct.style.color = XenoarchPalette.Text; pct.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(l); row.Add(track); row.Add(pct);
            return row;
        }

        /// <summary>radius = effective head radius (m); status = overheat / power warning or null.</summary>
        public void Refresh(bool visible, SonicToolKind kind, float radius, string status, int cells = 0)
        {
            if (_bars == null) return;
            bool show = visible && kind != SonicToolKind.None;
            XenoarchUI.Show(_bars, show); XenoarchUI.Show(_dials, show); XenoarchUI.Show(_hint, show);
            XenoarchUI.Show(_strip, show && kind == SonicToolKind.Excavator && !SonicTools.IsFine(kind));
            if (!show) return;

            float e = SonicTools.Energy01, h = SonicTools.Heat01(kind);
            bool locked = SonicTools.Locked(kind);
            _eFill.style.width = Length.Percent(e * 100f);
            _eFill.style.backgroundColor = e < 0.15f ? XenoarchPalette.WarnText : XenoarchPalette.Highlight;
            _hFill.style.width = Length.Percent(h * 100f);
            _hFill.style.backgroundColor = locked || h > 0.8f ? XenoarchPalette.WarnText : XenoarchPalette.Primary;
            _ePct.text = $"{e * 100f:0}%"; _hPct.text = $"{h * 100f:0}%";

            string pw = SonicTools.Power.ToString().ToUpperInvariant();
            string cellHint = "    X power cell (" + cells + ")";
            if (kind == SonicToolKind.Burster)
            {
                _dials.text = $"SHOCK CONE {radius:0.00} m   REACH {SonicTools.ReachFor(kind):0.0} m   POWER {pw}   Mk {SonicTools.MkText}";
                _hint.text = "SHIFT+WHEEL reach    R power" + cellHint;
            }
            else if (kind == SonicToolKind.Brush)
            {
                _dials.text = $"SPREAD {SonicTools.Spread:0.00} m   REACH {SonicTools.ReachFor(kind):0.0} m   POWER {pw}   Mk {SonicTools.MkText}";
                _hint.text = "WHEEL spread    SHIFT+WHEEL reach    R power" + cellHint;
            }
            else
            {
                string head = SonicTools.IsFine(kind) ? "FINE WAVE" : SonicTools.Head.ToString().ToUpperInvariant();
                _dials.text = $"{head} {radius:0.00} m   REACH {SonicTools.ReachFor(kind):0.0} m   POWER {pw}   FOCUS {SonicTools.Focus.ToString().ToUpperInvariant()}   Mk {SonicTools.MkText}";
                _hint.text = "WHEEL size    SHIFT+WHEEL reach    Q head    R power    T focus (Fine = precision)" + cellHint;
                for (int i = 0; i < 4; i++)
                {
                    bool on = i == (int)SonicTools.Head;
                    var c = _chips[i];
                    c.style.color = on ? XenoarchPalette.Primary : XenoarchPalette.SecondaryText;
                    var b = on ? XenoarchPalette.Primary : XenoarchPalette.A(XenoarchPalette.Secondary, 0.6f);
                    c.style.borderTopColor = c.style.borderBottomColor = c.style.borderLeftColor = c.style.borderRightColor = b;
                    c.style.backgroundColor = XenoarchPalette.A(XenoarchPalette.PanelDark, on ? 0.95f : 0.7f);
                }
            }
            if (!string.IsNullOrEmpty(status)) { _hint.text = status; _hint.style.color = XenoarchPalette.WarnText; }
            else _hint.style.color = XenoarchPalette.SecondaryText;
        }
    }
}
