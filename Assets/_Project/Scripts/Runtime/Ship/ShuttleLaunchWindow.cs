using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// "Take Shuttle" console opened from the airlock: choose where the shuttle lands (base landing zone or near a dig
    /// site/signature found by the orbital scanner), then launch. The ship itself stays in orbit.
    /// </summary>
    public sealed class ShuttleLaunchWindow : TerminalWindow
    {
        protected override string PanelId => "terminal.shuttle";
        string _selected = "";
        string _message;

        void Reset() => terminalName = "Shuttle Bay";
        // Opened by the airlock only (never targeted directly).
        public override float GetPriority(in InteractionContext c) => -1f;

        protected override void OnOpened() { _message = null; var x = Session?.Exploration; _selected = x != null ? x.Ship.shuttleSiteId ?? "" : ""; if (!Valid(_selected)) _selected = ""; }

        bool Valid(string id)
        {
            if (string.IsNullOrEmpty(id)) return true;
            var x = Session.Exploration; var b = x.CurrentBody;
            var s = x.Sites != null ? x.Sites.Get(id) : null;
            return s != null && b != null && s.bodyId == b.Id && x.Site(id).revealLevel > 0;
        }

        protected override void Build(VisualElement body)
        {
            var x = Session.Exploration;
            var b = x.CurrentBody;
            Status.text = ShipTravel.LocationText(x);
            var left = ScrollCol(body, 0); ListCol(left);
            var right = Col(body, true);
            if (!ShipTravel.UsesShuttle(b)) { Text(left, b == null ? "Nothing in range." : $"No surveyed landing zone on {b.displayName}.", "xa-term__warn"); return; }

            Text(left, $"LANDING ZONES: {b.displayName.ToUpperInvariant()}", "xa-term__h");
            Zone(left, "", "Base landing zone", "Surveyed flat ground near the survey beacon.");
            int n = 0;
            foreach (var site in x.SitesOnBody(b.Id))
            {
                var st = x.Site(site.Id);
                if (st.revealLevel <= 0) continue;
                n++;
                string what = st.revealLevel >= 3 ? site.displayName : st.revealLevel == 2 ? $"Subsurface structure ({site.displayName})" : $"Unresolved anomaly {n}";
                string where = st.revealLevel >= 3 ? "pinpointed" : $"+/- {ExplorationService.Uncertainty(st.revealLevel):0} m";
                Zone(left, site.Id, $"Near: {what}", $"Signature {where}");
            }
            if (n == 0) Text(left, "No signatures yet. Run an orbital scan to land near a dig site.", "xa-term__dim");

            Text(right, "SHUTTLE", "xa-term__h");
            var map = new SurfaceMapView(b.surfaceMap, b.surfaceMapRect);
            if (b.hasBaseLanding) map.Markers.Add(new SurfaceMapView.Marker { id = "", kind = SurfaceMapView.Kind.Base, world = new Vector2(b.baseLanding.x, b.baseLanding.z), label = "Base", selected = string.IsNullOrEmpty(_selected) });
            foreach (var site in x.SitesOnBody(b.Id))
            {
                var st = x.Site(site.Id);
                if (st.revealLevel <= 0) continue;
                var est = site.position + ExplorationService.EstimateOffset(site, st.revealLevel);
                map.Markers.Add(new SurfaceMapView.Marker { id = site.Id, kind = SurfaceMapView.Kind.Site, world = new Vector2(est.x, est.z), uncertainty = ExplorationService.Uncertainty(st.revealLevel), selected = site.Id == _selected });
                if (site.hasLandingHint) map.Markers.Add(new SurfaceMapView.Marker { id = site.Id, kind = SurfaceMapView.Kind.Landing, world = new Vector2(site.landingHint.x, site.landingHint.z), label = "Landing site", selected = site.Id == _selected });
            }
            map.Clicked += id => { _selected = id; Refresh(); };
            right.Add(map);
            var sel = string.IsNullOrEmpty(_selected) || x.Sites == null ? null : x.Sites.Get(_selected);
            Text(right, sel == null ? "Destination: base landing zone" : $"Destination: semi-flat ground near the selected signature");
            Text(right, "The shuttle sets down on the flattest clear ground close to the site. You step out beside it. Board it again (E) to return to the ship.", "xa-term__dim");
            Btn(right, $"Take Shuttle to {b.displayName}", () =>
            {
                if (!ShipTravel.TakeShuttle(_selected, out var why)) { _message = why; Refresh(); return; }
                Close();
            });
            if (!string.IsNullOrEmpty(_message)) Text(right, _message, "xa-term__warn");
        }

        void Zone(VisualElement parent, string id, string title, string detail)
        {
            bool on = (_selected ?? "") == id;
            var btn = Btn(parent, $"{(on ? "> " : "")}{title}\n{detail}", () => { _selected = id; Refresh(); });
            btn.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (on) { btn.style.borderLeftWidth = 4; btn.style.borderLeftColor = XenoarchPalette.Highlight; }
        }
    }
}
