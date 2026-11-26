using System.Collections.Generic;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// Ship navigation: system map + body list (discovery state), destination details, fuel cost, Travel (orbit
    /// change) and Land/Dock (leave the ship into the body's scene). Data: GalaxyDefinition + ExplorationService.
    /// </summary>
    public sealed class NavigationTerminal : TerminalWindow
    {
        protected override string PanelId => "terminal.navigation";
        string _selectedId;
        string _message;
        bool _galaxy;
        string _selectedSystemId;
        readonly MapView3D.ViewState _sysView = new MapView3D.ViewState();
        readonly MapView3D.ViewState _galView = new MapView3D.ViewState { pitch = 50f };

        void Reset() => terminalName = "Navigation Terminal";

        protected override void OnOpened() { _message = null; _selectedId ??= Session?.Exploration.Ship.currentBodyId; }

        protected override void Build(VisualElement body)
        {
            var x = Session.Exploration;
            var sys = x.CurrentSystem;
            var course = x.PlottedBody;
            Status.text = $"{ShipTravel.LocationText(x)}   |   Fuel {x.Ship.fuel:0.#} / {x.Ship.fuelCapacity:0}" + (course != null ? $"   |   Course: {course.displayName}" : "");
            if (sys == null) { Text(body, "No star system data (run Xenoarch > Setup > 7. Phase 1B assets).", "xa-term__warn"); return; }
            if (_galaxy && !x.GalaxyMapUnlocked) _galaxy = false;
            if (_galaxy) { BuildGalaxy(body, x, sys); return; }

            // --- left: list
            var left = Col(body);
            Text(left, $"{sys.displayName.ToUpperInvariant()} SYSTEM", "xa-term__h");
            var list = new ScrollView(); list.AddToClassList("xa-term__list"); left.Add(list);
            foreach (var b in Ordered(sys))
            {
                bool known = x.IsDiscovered(b.Id);
                var row = new Button(() => { _selectedId = b.Id; _message = null; Refresh(); }) { text = "" };
                row.AddToClassList("xa-term__row");
                if (!string.IsNullOrEmpty(b.parentId)) row.AddToClassList("xa-term__row--child");
                if (b.Id == _selectedId) row.AddToClassList("xa-term__row--sel");
                if (b.Id == x.Ship.currentBodyId) row.AddToClassList("xa-term__row--here");
                if (!known) row.AddToClassList("xa-term__row--locked");
                var name = new Label((b.Id == x.Ship.currentBodyId ? "> " : "") + (known ? b.displayName : "Unknown object"));
                var tag = new Label(known ? Tag(x, b) : "?"); tag.style.color = new Color(0.67f, 0.8f, 0.94f, 0.7f);
                row.Add(name); row.Add(tag);
                list.Add(row);
            }

            // --- middle: interactive map
            var mid = Col(body, true);
            MapHeader(mid, x, false);
            var map = new MapView3D(_sysView); mid.Add(map);
            FillSystemMap(map, sys, x);
            map.Clicked += id => { _selectedId = id; _message = null; Refresh(); };

            // --- right: details + actions
            var right = Col(body); right.style.minHeight = 0; right.style.overflow = Overflow.Hidden;
            var sel = sys.FindBody(_selectedId) ?? x.CurrentBody;
            if (sel == null) return;
            InfoSection(right, BodyInfo(x, sys, sel), 0, "xa-term__dim");
            bool here = sel.Id == x.Ship.currentBodyId;
            if (!here)
            {
                float cost = x.FuelCost(sel);
                bool can = x.CanTravel(sel, out var why);
                Text(right, $"Fuel cost {cost:0.#}"); // distance from the ship is in the info above
                if (!can) Text(right, why, "xa-term__warn");
                bool plotted = x.Ship.plottedBodyId == sel.Id;
                if (plotted) Text(right, "Course plotted. Engage the drive at the cockpit helm.", "xa-term__good");
                Btn(right, plotted ? "Clear course" : "Plot course", () =>
                {
                    if (plotted) { x.ClearCourse(); _message = null; }
                    else if (x.PlotCourse(sel, out var r)) _message = $"Course to {sel.displayName} plotted. Go to the cockpit helm.";
                    else _message = r;
                    Refresh();
                }, can || plotted);
            }
            else if (sel.HasLanding)
            {
                bool station = sel.kind == CelestialKind.Station;
                bool down = x.Ship.landed || x.Ship.docked;
                if (down) Text(right, station ? "Docked. Use the airlock to go aboard the station." : "Landed. Use the airlock to go outside.", "xa-term__good");
                bool busy = ShipLanding.Running;
                if (busy) Text(right, "Manoeuvre in progress...", "xa-term__warn");
                Btn(right, station ? (down ? "Undock" : "Dock") : (down ? "Take off" : "Land"), () =>
                {
                    if (ShipLanding.Begin(sel, !down, station)) { _message = null; Close(); }
                }, !busy);
            }
            else Text(right, "In orbit. No surveyed landing zone here yet.", "xa-term__dim");
            if (!string.IsNullOrEmpty(_message)) Text(right, _message, "xa-term__good");
        }

        /// <summary>Right-panel details for the selected body / system: title, info lines and description in a
        /// scroll area that takes the free height, so the actions added after it stay at the bottom.</summary>
        void InfoSection(VisualElement right, (string title, List<string> lines, string desc) info, int slot, string descCls)
        {
            Text(right, info.title, "xa-term__h");
            var sv = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            sv.style.flexGrow = 1; sv.style.flexShrink = 1; sv.style.minHeight = 0; sv.style.marginBottom = 6;
            right.Add(sv);
            foreach (var l in info.lines) { var lb = Text(sv, l); lb.style.whiteSpace = WhiteSpace.Normal; }
            if (!string.IsNullOrEmpty(info.desc)) { var d = Text(sv, info.desc, descCls); d.style.whiteSpace = WhiteSpace.Normal; d.style.marginTop = 4; }
        }

        static (string, List<string>, string) BodyInfo(ExplorationService x, StarSystemDefinition sys, CelestialBodyDefinition b)
        {
            var lines = new List<string>();
            bool known = x.IsDiscovered(b.Id);
            if (!known) return ("UNKNOWN OBJECT", new List<string> { "Not yet identified. Fly closer or scan the system." }, null);
            var parent = string.IsNullOrEmpty(b.parentId) ? null : sys.FindBody(b.parentId);
            lines.Add($"Type: {b.kind}{(parent != null ? $" of {parent.displayName}" : "")}");
            if (b.kind != CelestialKind.Station && b.kind != CelestialKind.Belt && b.radiusKm > 0f) lines.Add($"Radius {b.radiusKm:N0} km   Gravity {b.gravityG:0.##} g");
            if (b.environment != null) lines.Add($"Atmosphere: {b.environment.atmosphere}");
            if (b.kind != CelestialKind.Star)
            {
                lines.Add($"From the star: {(parent != null ? parent.orbitAU : b.orbitAU):0.###} AU{(b.orbitPeriodDays > 0f ? $"   Orbit {b.orbitPeriodDays:0.#} days" : "")}");
                if (x.CurrentBody != null && x.CurrentBody != b) lines.Add($"From the ship: {x.DistanceAU(x.CurrentBody, b):0.###} AU");
                else if (x.CurrentBody == b) lines.Add("The ship is here.");
            }
            int total = 0, revealed = 0, pinned = 0;
            foreach (var s in x.SitesOnBody(b.Id)) { total++; var st = x.Site(s.Id); if (st.revealLevel > 0) revealed++; if (st.revealLevel >= 3) pinned++; }
            var bd = x.Body(b.Id);
            if (b.HasLanding && b.kind != CelestialKind.Station)
                lines.Add(bd.orbitalScanPasses == 0 ? "Sites: not scanned yet (orbital scanner)" : $"Sites: {revealed} signature(s) found, {pinned} pinpointed ({bd.orbitalScanPasses} scan pass{(bd.orbitalScanPasses == 1 ? "" : "es")})");
            lines.Add($"Archaeological potential: {Potential(b.archaeologicalPotential)}");
            lines.Add($"Status: {(bd.visited ? "visited" : "not visited")}{(b.HasLanding ? (b.kind == CelestialKind.Station ? ", docking available" : ", landing zone surveyed") : ", orbit only")}");
            return (b.displayName.ToUpperInvariant(), lines, b.description);
        }

        static (string, List<string>, string) SystemInfo(ExplorationService x, StarSystemDefinition here, StarSystemDefinition s)
        {
            var lines = new List<string>
            {
                $"Star class {s.stellarClass}",
                s == here ? "Current system" : $"Distance {Vector3.Distance(here.galacticPosition, s.galacticPosition):0.0} ly",
                $"Known bodies: {(s.bodies != null ? s.bodies.Count : 0)}",
                $"Archaeological potential: {Potential(s.archaeologicalPotential)}",
                $"Status: {(s == here ? "explored" : !string.IsNullOrEmpty(s.revealFact) ? "located from decoded Vael data" : "charted")}",
            };
            return (s.displayName.ToUpperInvariant(), lines, s.revealNote);
        }

        static string Tag(ExplorationService x, CelestialBodyDefinition b)
        {
            if (b.kind == CelestialKind.Star) return "star";
            string t = b.HasLanding ? (b.kind == CelestialKind.Station ? "dock" : "landing") : "";
            if (x.Body(b.Id).visited) t = (t.Length > 0 ? t + " | " : "") + "visited";
            return t;
        }

        static string Potential(float p) => p <= 0f ? "none detected" : p < 0.3f ? "low" : p < 0.6f ? "moderate" : "high";

        static List<CelestialBodyDefinition> Ordered(StarSystemDefinition sys)
        {
            var roots = new List<CelestialBodyDefinition>();
            foreach (var b in sys.bodies) if (b != null && string.IsNullOrEmpty(b.parentId)) roots.Add(b);
            roots.Sort((a, c) => a.orbitAU.CompareTo(c.orbitAU));
            var result = new List<CelestialBodyDefinition>();
            foreach (var r in roots)
            {
                result.Add(r);
                foreach (var b in sys.bodies) if (b != null && b.parentId == r.Id) result.Add(b);
            }
            return result;
        }

        void MapHeader(VisualElement col, ExplorationService x, bool galaxy)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; col.Add(row);
            var sysBtn = Btn(row, "System map", () => { _galaxy = false; Refresh(); }, galaxy);
            sysBtn.style.width = 130;
            if (x.GalaxyMapUnlocked) { var g = Btn(row, "Galaxy map", () => { _galaxy = true; Refresh(); }, !galaxy); g.style.width = 130; }
            var reset = Btn(row, "Reset view", () => { (galaxy ? _galView : _sysView).Reset(); if (galaxy) _galView.pitch = 50f; Refresh(); }); reset.style.width = 110;
            Text(col, "RMB drag: rotate freely   Shift+RMB / Q E: roll   LMB drag: pan   Ctrl+LMB: depth   Wheel: zoom at cursor   Click: select   Double-click: focus   R: reset", "xa-term__dim");
        }

        /// <summary>Map units: star at the origin, planets on log-scaled orbits (radius 0..1), moons around their parent.</summary>
        void FillSystemMap(MapView3D map, StarSystemDefinition sys, ExplorationService x)
        {
            map.Extent = 1.08f;
            float maxAU = 0.1f;
            foreach (var b in sys.bodies) if (b != null && string.IsNullOrEmpty(b.parentId)) maxAU = Mathf.Max(maxAU, b.orbitAU);
            float R(float au) => au <= 0f ? 0f : Mathf.Log(1f + au * 4f) / Mathf.Log(1f + maxAU * 4f);
            var placed = new Dictionary<string, Vector3>();
            foreach (var b in Ordered(sys))
            {
                bool child = !string.IsNullOrEmpty(b.parentId);
                Vector3 p;
                if (!child)
                {
                    float r = R(b.orbitAU);
                    if (r > 0f) map.Rings.Add(new MapView3D.Ring { centre = Vector3.zero, radius = r, color = new Color(0.67f, 0.8f, 0.94f, 0.16f) });
                    float ang = (Mathf.Abs(b.seed) % 360) * Mathf.Deg2Rad;
                    p = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                }
                else
                {
                    placed.TryGetValue(b.parentId, out var pp);
                    int idx = 0; foreach (var s in sys.bodies) { if (s == b) break; if (s != null && s.parentId == b.parentId) idx++; }
                    float ang = idx * 2.1f + 0.6f, rr = 0.035f + idx * 0.012f;
                    p = pp + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rr;
                    map.Rings.Add(new MapView3D.Ring { centre = pp, radius = rr, color = new Color(0.67f, 0.8f, 0.94f, 0.08f) });
                }
                placed[b.Id] = p;
                bool known = x.IsDiscovered(b.Id);
                map.Items.Add(new MapView3D.Item
                {
                    id = b.Id, pos = p, label = known ? b.displayName : "?",
                    color = known ? b.mapColor : new Color(0.4f, 0.4f, 0.45f),
                    size = b.kind == CelestialKind.Star ? 22f : child ? 6f : b.kind == CelestialKind.Belt ? 5f : 10f,
                    selected = b.Id == _selectedId, here = b.Id == x.Ship.currentBodyId, dim = !known, alwaysLabel = !child,
                });
            }
        }

        // ------------------------------------------------------------------ galaxy
        void BuildGalaxy(VisualElement body, ExplorationService x, StarSystemDefinition here)
        {
            var g = x.Galaxy;
            var known = new List<StarSystemDefinition>();
            foreach (var s in g.systems) if (x.IsSystemKnown(s) || s == here) known.Add(s);
            _selectedSystemId ??= here.Id;

            var left = Col(body);
            Text(left, "KNOWN SYSTEMS", "xa-term__h");
            var list = new ScrollView(); list.AddToClassList("xa-term__list"); left.Add(list);
            foreach (var s in known)
            {
                var id = s.Id;
                var row = new Button(() => { _selectedSystemId = id; Refresh(); }) { text = "" };
                row.AddToClassList("xa-term__row");
                if (id == _selectedSystemId) row.AddToClassList("xa-term__row--sel");
                if (s == here) row.AddToClassList("xa-term__row--here");
                row.Add(new Label((s == here ? "> " : "") + s.displayName));
                var tag = new Label($"{Vector3.Distance(here.galacticPosition, s.galacticPosition):0.0} ly"); tag.style.color = new Color(0.67f, 0.8f, 0.94f, 0.7f); row.Add(tag);
                list.Add(row);
            }

            var mid = Col(body, true);
            MapHeader(mid, x, true);
            var map = new MapView3D(_galView); mid.Add(map);
            float ext = 2f;
            foreach (var s in known) ext = Mathf.Max(ext, (s.galacticPosition - here.galacticPosition).magnitude);
            map.Extent = ext * 1.1f;
            for (float r = 5f; r <= ext + 0.01f; r += 5f) map.Rings.Add(new MapView3D.Ring { centre = Vector3.zero, radius = r, color = new Color(0.67f, 0.8f, 0.94f, 0.12f) });
            if (x.Ship.ftlRangeLy > 0f) map.Rings.Add(new MapView3D.Ring { centre = Vector3.zero, radius = x.Ship.ftlRangeLy, color = new Color(0.4f, 1f, 0.6f, 0.35f) });
            foreach (var s in known)
            {
                var p = s.galacticPosition - here.galacticPosition;
                map.Lines.Add(new MapView3D.Line { a = p, b = new Vector3(p.x, 0f, p.z), color = new Color(0.67f, 0.8f, 0.94f, 0.18f) });
                bool signal = !string.IsNullOrEmpty(s.revealFact);
                map.Items.Add(new MapView3D.Item
                {
                    id = s.Id, pos = p, label = s.displayName + (signal ? "  [Vael signal]" : ""), alwaysLabel = true,
                    color = signal ? new Color(0.95f, 0.66f, 0.22f) : StarColor(s.stellarClass),
                    size = s == here ? 12f : signal ? 11f : 8f, selected = s.Id == _selectedSystemId, here = s == here,
                });
            }
            map.Clicked += id => { _selectedSystemId = id; Refresh(); };

            var right = Col(body); right.style.minHeight = 0; right.style.overflow = Overflow.Hidden;
            var sel = g.FindSystem(_selectedSystemId) ?? here;
            InfoSection(right, SystemInfo(x, here, sel), 1, "xa-term__good");
            if (sel != here)
            {
                float need = Mathf.Max(sel.minFtlRangeLy, Vector3.Distance(here.galacticPosition, sel.galacticPosition));
                Text(right, x.Ship.ftlRangeLy >= need ? "Within FTL range." : $"Out of range: needs a long-range drive ({need:0.0} ly, this ship {x.Ship.ftlRangeLy:0.#} ly).", "xa-term__warn");
                Text(right, "Interstellar jumps arrive in a later phase.", "xa-term__dim");
            }
        }

        static Color StarColor(string cls)
        {
            char c = string.IsNullOrEmpty(cls) ? 'G' : char.ToUpperInvariant(cls[0]);
            switch (c)
            {
                case 'O': case 'B': return new Color(0.65f, 0.75f, 1f);
                case 'A': return new Color(0.85f, 0.9f, 1f);
                case 'F': return new Color(1f, 0.97f, 0.88f);
                case 'G': return new Color(1f, 0.92f, 0.7f);
                case 'K': return new Color(1f, 0.78f, 0.5f);
                default: return new Color(1f, 0.6f, 0.45f);
            }
        }
    }
}
