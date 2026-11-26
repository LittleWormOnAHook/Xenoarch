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
        string _selectedSite;
        SurfaceMapView _surface;

        void Reset() => terminalName = "Orbital Scanning Terminal";

        protected override void OnOpened() { _message = null; _landMsg = null; _scanT = -1f; }
        protected override void OnClosed() { _scanT = -1f; }

        protected override void Tick()
        {
            if (_scanT < 0f) return;
            _scanT += Time.unscaledDeltaTime;
            if (_fill != null) _fill.style.width = Length.Percent(Mathf.Clamp01(_scanT / passSeconds) * 100f);
            if (_surface != null) { _surface.Sweep = Mathf.Clamp01(_scanT / passSeconds); _surface.MarkDirtyRepaint(); }
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
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow = 1; scroll.style.minHeight = 0; right.Add(scroll);
            var col = scroll.contentContainer;
            _surface = new SurfaceMapView(b.surfaceMap, b.surfaceMapRect);
            if (_scanT >= 0f) _surface.Sweep = Mathf.Clamp01(_scanT / passSeconds);
            col.Add(_surface);
            string shuttleSite = x.Ship.shuttleSiteId ?? "";
            if (b.hasBaseLanding) _surface.Markers.Add(new SurfaceMapView.Marker { id = "landing:", kind = SurfaceMapView.Kind.Base, world = new Vector2(b.baseLanding.x, b.baseLanding.z), label = "Base", selected = shuttleSite == "" });
            int hidden = 0, shown = 0;
            var rows = new VisualElement();
            ArchaeologicalSiteDefinition sel = null;
            foreach (var site in x.SitesOnBody(b.Id))
            {
                var st = x.Site(site.Id);
                if (st.revealLevel <= 0) { hidden++; continue; }
                shown++;
                var est = site.position + ExplorationService.EstimateOffset(site, st.revealLevel);
                string what = st.revealLevel >= 3 ? site.displayName : st.revealLevel == 2 ? $"Subsurface structure ({site.displayName})" : $"Unresolved anomaly {shown}";
                string where = st.revealLevel >= 3 ? $"marked at ({site.position.x:0}, {site.position.z:0})" : $"near ({est.x:0}, {est.z:0}) +/- {ExplorationService.Uncertainty(st.revealLevel):0} m";
                bool isSel = site.Id == _selectedSite;
                if (isSel) sel = site;
                _surface.Markers.Add(new SurfaceMapView.Marker { id = site.Id, kind = SurfaceMapView.Kind.Site, world = new Vector2(est.x, est.z), uncertainty = ExplorationService.Uncertainty(st.revealLevel), label = isSel ? what : null, selected = isSel });
                if (site.hasLandingHint)
                    _surface.Markers.Add(new SurfaceMapView.Marker { id = "landing:" + site.Id, kind = SurfaceMapView.Kind.Landing, world = new Vector2(site.landingHint.x, site.landingHint.z), label = "Landing site", selected = shuttleSite == site.Id });
                var id = site.Id;
                var row = new Button(() => { _selectedSite = id; Refresh(); }) { text = "" };
                row.AddToClassList("xa-term__row");
                if (isSel) row.AddToClassList("xa-term__row--sel");
                row.Add(new Label((shuttleSite == id ? "[shuttle] " : "") + what));
                row.Add(new Label($"{where}  |  depth ~{(st.revealLevel >= 2 ? $"{site.depth:0.#} m" : "?")}"));
                rows.Add(row);
            }
            _surface.Clicked += id =>
            {
                if (id.StartsWith("landing:")) { SetShuttleSite(id.Substring(8)); return; }
                _selectedSite = id; Refresh();
            };
            Text(col, "Red: dig-site signatures (circle = position uncertainty). Green: shuttle landing sites; click one to set the shuttle's destination.", "xa-term__dim");
            if (sel != null)
            {
                Text(col, $"Selected: {(x.Site(sel.Id).revealLevel >= 3 ? sel.displayName : "signature")}", "xa-term__h");
                bool already = shuttleSite == sel.Id;
                Btn(col, already ? "Shuttle will land here" : "Land the shuttle near this site", () => SetShuttleSite(sel.Id), !already);
            }
            if (!string.IsNullOrEmpty(_landMsg)) Text(col, _landMsg, "xa-term__good");
            col.Add(rows);
            if (shown == 0) Text(col, bd.orbitalScanPasses == 0 ? "No scan data. Run an orbital scan." : "No anomalies resolved.", "xa-term__dim");
            if (hidden > 0 && bd.orbitalScanPasses > 0) Text(col, "Faint signals remain below the resolution threshold.", "xa-term__dim");
            Text(col, "Positions are landing-zone coordinates. Use the handheld scanner on the surface to pinpoint.", "xa-term__dim");
        }

        string _landMsg;

        void SetShuttleSite(string siteId)
        {
            var x = Session.Exploration;
            x.Ship.shuttleSiteId = siteId ?? "";
            _landMsg = string.IsNullOrEmpty(siteId) ? "Shuttle set to the base landing zone." : "Shuttle landing site set. Take the shuttle at the airlock.";
            if (!string.IsNullOrEmpty(siteId)) _selectedSite = siteId;
            Refresh();
        }
    }
}
