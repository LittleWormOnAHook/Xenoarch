using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Valuables;
using UnityEngine;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// t299u: shared site-info block for the surface Maps window and the orbital scanner.
    /// Reuses existing <c>xa-term__*</c> classes, palette and type — no new visual style.
    /// </summary>
    public static class MapSiteInfo
    {
        /// <summary>Fills <paramref name="parent"/> with name, type, distance, status and expected loot for <paramref name="id"/>. Returns false when the id is unknown.</summary>
        public static bool Fill(VisualElement parent, string id, CelestialBodyDefinition body, Vector2? playerXz, System.Action openDigMap = null, System.Action setWaypoint = null, System.Action track = null, System.Action remove = null)
        {
            if (parent == null || string.IsNullOrEmpty(id)) return false;
            var session = XenoarchRuntime.Session;
            var x = session?.Exploration;
            if (session == null || x == null) return false;

            string name, type, status, loot;
            Vector2 world;
            bool canOpenDig = false;
            bool isWaypoint = WaypointService.IsId(id);

            if (isWaypoint)
            {
                var w = session.Waypoints != null ? session.Waypoints.Find(id) : null;
                if (w == null) return false;
                name = string.IsNullOrEmpty(w.name) ? "Waypoint" : w.name;
                type = w.IsCustom ? "Custom waypoint" : "Site waypoint";
                status = session.Waypoints.IsTracked(id) ? "Tracked" : "Saved";
                loot = null;
                world = w.Xz;
            }
            else if (id.StartsWith("landing:"))
            {
                string siteId = id.Substring(8);
                if (string.IsNullOrEmpty(siteId))
                {
                    if (body == null || !body.hasBaseLanding) return false;
                    name = "Base landing zone"; type = "Landing zone"; status = "Available"; loot = null;
                    world = new Vector2(body.baseLanding.x, body.baseLanding.z);
                }
                else
                {
                    var def = x.SiteDef(siteId) ?? FindSite(x, body, siteId);
                    if (def == null || !def.hasLandingHint) return false;
                    name = string.IsNullOrEmpty(def.displayName) ? "Landing zone" : $"Landing zone — {def.displayName}";
                    type = "Landing zone"; status = "Available"; loot = null;
                    world = new Vector2(def.landingHint.x, def.landingHint.z);
                }
            }
            else if (id.StartsWith("cache."))
            {
                var vdir = ValuableSiteDirector.Instance;
                ValuableSite? found = null;
                if (vdir != null)
                    foreach (var v in vdir.Sites) if (v.id == id) { found = v; break; }
                if (found == null) return false;
                var vsite = found.Value;
                name = "Cache"; type = "Discovered cache";
                bool collected = session.Valuables.IsCollected(id);
                status = collected ? "Excavated" : session.Valuables.IsRevealed(id) ? "Discovered" : "Scanned";
                loot = CacheLoot(session, vsite);
                world = new Vector2(vsite.position.x, vsite.position.z);
            }
            else
            {
                var def = x.SiteDef(id) ?? FindSite(x, body, id);
                if (def == null) return false;
                var known = KnownSites.Get(x, def);
                if (known == null) return false;
                var k = known.Value;
                bool survey = def.isSurveyCache;
                name = k.Pinpointed ? (string.IsNullOrEmpty(def.displayName) ? def.Id : def.displayName) : k.Label();
                var dm = session.DigMaps != null ? session.DigMaps.Find(def.Id) : null;
                if (dm != null) name = $"{dm.Code}  {name}";
                type = survey ? "Survey cache" : "Dig site";
                status = k.Cleared ? "Excavated" : k.Pinpointed ? "Discovered" : "Scanned";
                loot = k.Pinpointed ? SiteLoot(session, def) : "Unknown";
                world = k.Map;
                canOpenDig = dm != null && openDigMap != null;
            }

            Title(parent, name);
            Line(parent, "Type", type);
            Line(parent, "Distance", playerXz.HasValue ? DistanceLabel(playerXz.Value, world) : "Not on the surface");
            Line(parent, "Status", status);
            if (loot != null) Line(parent, "Expected loot", loot);
            if (canOpenDig)
            {
                var b = new Button(openDigMap) { text = "Open dig map" };
                b.AddToClassList("xa-term__btn");
                b.style.marginTop = 8;
                parent.Add(b);
            }
            if (setWaypoint != null && !isWaypoint)
            {
                var b = new Button(setWaypoint) { text = "Set waypoint" };
                b.AddToClassList("xa-term__btn");
                b.style.marginTop = 8;
                parent.Add(b);
            }
            if (isWaypoint)
            {
                if (track != null && (session.Waypoints == null || !session.Waypoints.IsTracked(id)))
                {
                    var b = new Button(track) { text = "Track this waypoint" };
                    b.AddToClassList("xa-term__btn");
                    b.style.marginTop = 8;
                    parent.Add(b);
                }
                if (remove != null)
                {
                    var b = new Button(remove) { text = "Remove waypoint" };
                    b.AddToClassList("xa-term__btn");
                    b.AddToClassList("xa-term__btn--ghost");
                    b.style.marginTop = 8;
                    parent.Add(b);
                }
            }
            return true;
        }

        public static Vector2? PlayerXz()
        {
            var rig = PlayerRigLocator.Find();
            if (rig == null) return null;
            var p = rig.Root.position;
            return new Vector2(p.x, p.z);
        }

        public static string DistanceLabel(Vector2 from, Vector2 to)
        {
            float d = (to - from).magnitude;
            return d < 1f ? "< 1 m" : $"{d:0} m";
        }

        static ArchaeologicalSiteDefinition FindSite(ExplorationService x, CelestialBodyDefinition body, string id)
        {
            if (x == null || string.IsNullOrEmpty(id)) return null;
            string bodyId = body != null ? body.Id : x.CurrentBody != null ? x.CurrentBody.Id : null;
            if (string.IsNullOrEmpty(bodyId)) return null;
            foreach (var s in x.SitesOnBody(bodyId)) if (s.Id == id) return s;
            return null;
        }

        static string CacheLoot(GameSession session, ValuableSite v)
        {
            // Do not call Valuables.State — that would create a save entry just for a peek.
            int stored = -1;
            if (session.Valuables?.Data?.caches != null)
                foreach (var c in session.Valuables.Data.caches)
                    if (c != null && c.id == v.id) { stored = c.tier; break; }
            if (stored >= 0) return ValuableSiteDirector.TierName((LootTier)stored);
            var table = session.Config != null ? session.Config.valuableLoot : null;
            if (table == null) return "Unknown";
            return ValuableSiteDirector.TierName(table.RollTier(new System.Random(v.seed)));
        }

        static string SiteLoot(GameSession session, ArchaeologicalSiteDefinition def)
        {
            if (def?.artifacts == null || def.artifacts.Count == 0) return "None recorded";
            var arts = session.Artifacts;
            if (arts == null) return "Unknown";
            var best = DMI.Xenoarch.Artifacts.ArtifactRarity.Common;
            bool any = false;
            foreach (var sp in def.artifacts)
            {
                if (sp == null || string.IsNullOrEmpty(sp.artifactId)) continue;
                var a = arts.Definition(sp.artifactId);
                if (a == null) continue;
                if (!any || a.rarity > best) { best = a.rarity; any = true; }
            }
            return any ? $"Up to {best}" : "Unknown";
        }

        static void Title(VisualElement parent, string text)
        {
            var l = new Label(text); l.AddToClassList("xa-term__h"); l.style.whiteSpace = WhiteSpace.Normal; parent.Add(l);
        }

        static void Line(VisualElement parent, string key, string value)
        {
            var l = new Label($"{key}:  {value}"); l.AddToClassList("xa-term__text"); l.style.whiteSpace = WhiteSpace.Normal; l.style.marginBottom = 2; parent.Add(l);
        }
    }
}
