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

        void Reset() => terminalName = "Navigation Terminal";

        protected override void OnOpened() { _message = null; _selectedId ??= Session?.Exploration.Ship.currentBodyId; }

        protected override void Build(VisualElement body)
        {
            var x = Session.Exploration;
            var sys = x.CurrentSystem;
            var course = x.PlottedBody;
            Status.text = $"{ShipTravel.LocationText(x)}   |   Fuel {x.Ship.fuel:0.#} / {x.Ship.fuelCapacity:0}" + (course != null ? $"   |   Course: {course.displayName}" : "");
            if (sys == null) { Text(body, "No star system data (run Xenoarch > Setup > 7. Phase 1B assets).", "xa-term__warn"); return; }

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

            // --- middle: map
            var mid = Col(body, true);
            Text(mid, "SYSTEM MAP (log scale)", "xa-term__h");
            var map = new VisualElement(); map.AddToClassList("xa-term__map"); mid.Add(map);
            map.RegisterCallback<GeometryChangedEvent>(_ => DrawMap(map, sys, x));

            // --- right: details + actions
            var right = Col(body);
            var sel = sys.FindBody(_selectedId) ?? x.CurrentBody;
            if (sel == null) return;
            bool selKnown = x.IsDiscovered(sel.Id);
            Text(right, selKnown ? sel.displayName.ToUpperInvariant() : "UNKNOWN OBJECT", "xa-term__h");
            if (selKnown)
            {
                Text(right, $"{sel.kind}" + (string.IsNullOrEmpty(sel.parentId) ? "" : $" of {sys.FindBody(sel.parentId)?.displayName}"));
                if (sel.kind != CelestialKind.Station && sel.kind != CelestialKind.Belt) Text(right, $"Gravity {sel.gravityG:0.##} g   Radius {sel.radiusKm:N0} km");
                if (sel.environment != null) Text(right, $"Atmosphere: {sel.environment.atmosphere}");
                Text(right, $"Archaeological potential: {Potential(sel.archaeologicalPotential)}");
                var bd = x.Body(sel.Id);
                Text(right, bd.visited ? "Visited" : "Not visited", "xa-term__dim");
                if (!string.IsNullOrEmpty(sel.description)) Text(right, sel.description, "xa-term__dim");
            }

            var spacer = new VisualElement(); spacer.style.flexGrow = 1; right.Add(spacer);
            bool here = sel.Id == x.Ship.currentBodyId;
            if (!here)
            {
                float cost = x.FuelCost(sel);
                bool can = x.CanTravel(sel, out var why);
                Text(right, $"Distance {x.DistanceAU(x.CurrentBody, sel):0.###} AU   Fuel cost {cost:0.#}");
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

        void DrawMap(VisualElement map, StarSystemDefinition sys, ExplorationService x)
        {
            map.Clear();
            float w = map.resolvedStyle.width, h = map.resolvedStyle.height;
            if (float.IsNaN(w) || w < 10f) return;
            Vector2 c = new Vector2(w * 0.5f, h * 0.5f);
            float maxR = Mathf.Min(w, h) * 0.47f;
            float maxAU = 0.1f;
            foreach (var b in sys.bodies) if (b != null && string.IsNullOrEmpty(b.parentId)) maxAU = Mathf.Max(maxAU, b.orbitAU);
            float R(float au) => au <= 0f ? 0f : maxR * Mathf.Log(1f + au * 4f) / Mathf.Log(1f + maxAU * 4f);
            var placed = new Dictionary<string, Vector2>();
            foreach (var b in Ordered(sys))
            {
                bool child = !string.IsNullOrEmpty(b.parentId);
                Vector2 p;
                if (!child)
                {
                    float r = R(b.orbitAU);
                    if (r > 0f)
                    {
                        var ring = new VisualElement(); ring.AddToClassList("xa-map-ring");
                        ring.style.left = c.x - r; ring.style.top = c.y - r; ring.style.width = r * 2; ring.style.height = r * 2;
                        ring.pickingMode = PickingMode.Ignore; map.Add(ring);
                    }
                    float ang = (Mathf.Abs(b.seed) % 360) * Mathf.Deg2Rad;
                    p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                }
                else
                {
                    placed.TryGetValue(b.parentId, out var pp);
                    int idx = 0; foreach (var s in sys.bodies) { if (s == b) break; if (s != null && s.parentId == b.parentId) idx++; }
                    float ang = idx * 2.1f + 0.6f;
                    p = pp + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (14f + idx * 4f);
                }
                placed[b.Id] = p;
                bool known = x.IsDiscovered(b.Id);
                float size = b.kind == CelestialKind.Star ? 22f : child ? 6f : b.kind == CelestialKind.Belt ? 5f : 10f;
                var dot = new VisualElement(); dot.AddToClassList("xa-map-dot");
                dot.style.left = p.x - size / 2; dot.style.top = p.y - size / 2; dot.style.width = size; dot.style.height = size;
                dot.style.backgroundColor = known ? b.mapColor : new Color(0.4f, 0.4f, 0.45f);
                if (b.Id == _selectedId) { dot.style.borderTopWidth = dot.style.borderBottomWidth = dot.style.borderLeftWidth = dot.style.borderRightWidth = 2; dot.style.borderTopColor = dot.style.borderBottomColor = dot.style.borderLeftColor = dot.style.borderRightColor = new Color(0.95f, 0.66f, 0.22f); }
                var id = b.Id;
                dot.RegisterCallback<ClickEvent>(_ => { _selectedId = id; _message = null; Refresh(); });
                map.Add(dot);
                if (!child || b.Id == _selectedId || b.Id == x.Ship.currentBodyId)
                {
                    var lbl = new Label((b.Id == x.Ship.currentBodyId ? "> " : "") + (known ? b.displayName : "?")); lbl.AddToClassList("xa-map-label");
                    lbl.style.left = p.x + size / 2 + 2; lbl.style.top = p.y - 8; lbl.pickingMode = PickingMode.Ignore;
                    if (b.Id == x.Ship.currentBodyId) lbl.style.color = new Color(0.95f, 0.66f, 0.22f);
                    map.Add(lbl);
                }
            }
        }
    }
}
