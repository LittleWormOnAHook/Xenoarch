using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Orbital scanning: each pass over the current body progressively reveals archaeological anomalies
    /// (hidden -> orbital anomaly ~60 m -> surface signal ~15 m). Pinpointing needs the handheld scanner on the surface.
    /// Pass count is limited by the ship's scanner level; state persists per body.
    /// </summary>
    public sealed class OrbitalScannerTerminal : TerminalWindow
    {
        public float passSeconds = 2.5f;
        protected override string PanelId => "terminal.orbital_scanner";

        float _scanT = -1f;
        VisualElement _fill;
        string _message;

        void Reset() => terminalName = "Orbital Scanning Terminal";

        protected override void OnOpened() { _message = null; _scanT = -1f; }
        protected override void OnClosed() { _scanT = -1f; }

        protected override void Tick()
        {
            if (_scanT < 0f) return;
            _scanT += Time.unscaledDeltaTime;
            if (_fill != null) _fill.style.width = Length.Percent(Mathf.Clamp01(_scanT / passSeconds) * 100f);
            if (_scanT < passSeconds) return;
            _scanT = -1f;
            int n = Session.Exploration.OrbitalScanPass();
            _message = n > 0 ? $"Scan complete: {n} signature(s) refined." : "Scan complete: no new signatures.";
            Refresh();
        }

        protected override void Build(VisualElement body)
        {
            var x = Session.Exploration;
            var b = x.CurrentBody;
            Status.text = $"{ShipTravel.LocationText(x)}   |   Scanner Mk {x.Ship.scannerLevel}";
            var left = Col(body);
            var right = Col(body, true);
            if (b == null) { Text(left, "No body in range.", "xa-term__warn"); return; }

            var bd = x.Body(b.Id);
            Text(left, $"TARGET: {b.displayName.ToUpperInvariant()}", "xa-term__h");
            Text(left, $"Scan passes {bd.orbitalScanPasses} / {x.MaxOrbitalPasses}");
            Bar(left, bd.orbitalScanPasses / (float)x.MaxOrbitalPasses);
            Text(left, $"Archaeological potential: {(b.archaeologicalPotential <= 0f ? "none detected" : b.archaeologicalPotential < 0.3f ? "low" : b.archaeologicalPotential < 0.6f ? "moderate" : "high")}");
            bool landedOrDocked = x.Ship.landed || x.Ship.docked;
            bool canScan = !landedOrDocked && bd.orbitalScanPasses < x.MaxOrbitalPasses && b.kind != CelestialKind.Station && b.kind != CelestialKind.Star;
            if (_scanT >= 0f)
            {
                Text(left, "Scanning...", "xa-term__good");
                var barFill = Bar(left, 0f); _fill = barFill;
            }
            else
            {
                Btn(left, bd.orbitalScanPasses == 0 ? "Begin orbital scan" : "Run another pass", () => { _scanT = 0f; _message = null; Refresh(); }, canScan);
                if (landedOrDocked) Text(left, "Orbital scan needs the ship in orbit.", "xa-term__dim");
                else if (bd.orbitalScanPasses >= x.MaxOrbitalPasses) Text(left, "Scanner resolution limit reached. Upgrade the scanner or survey on foot.", "xa-term__dim");
            }
            if (!string.IsNullOrEmpty(_message)) Text(left, _message, "xa-term__good");

            Text(right, "SIGNATURES", "xa-term__h");
            var list = new ScrollView(); list.AddToClassList("xa-term__list"); right.Add(list);
            int hidden = 0, shown = 0;
            foreach (var site in x.SitesOnBody(b.Id))
            {
                var st = x.Site(site.Id);
                if (st.revealLevel <= 0) { hidden++; continue; }
                shown++;
                var est = site.position + ExplorationService.EstimateOffset(site, st.revealLevel);
                string what = st.revealLevel >= 3 ? site.displayName : st.revealLevel == 2 ? $"Subsurface structure ({site.displayName})" : "Unresolved anomaly";
                string where = st.revealLevel >= 3 ? $"marked at ({site.position.x:0}, {site.position.z:0})" : $"near ({est.x:0}, {est.z:0}) +/- {ExplorationService.Uncertainty(st.revealLevel):0} m";
                var row = new VisualElement(); row.AddToClassList("xa-term__row");
                row.Add(new Label($"{what}"));
                row.Add(new Label($"{where}  |  depth ~{(st.revealLevel >= 2 ? $"{site.depth:0.#} m" : "?")}"));
                list.Add(row);
            }
            if (shown == 0) Text(list, bd.orbitalScanPasses == 0 ? "No scan data. Run an orbital scan." : "No anomalies resolved.", "xa-term__dim");
            if (hidden > 0 && bd.orbitalScanPasses > 0) Text(right, "Faint signals remain below the resolution threshold.", "xa-term__dim");
            Text(right, "Positions are landing-zone coordinates. Use the handheld scanner on the surface to pinpoint.", "xa-term__dim");
        }
    }
}
