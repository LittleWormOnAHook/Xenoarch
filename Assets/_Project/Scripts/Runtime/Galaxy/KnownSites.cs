using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// t182u: what the player knows about a body's dig sites, read straight from <see cref="ExplorationService"/>
    /// (world.discovery in the save): orbital scan passes, the handheld scanner and discovery chains all raise the same
    /// SiteState.revealLevel. The full map (M), the minimaps, the orbital scanner, the shuttle window and any other map
    /// list sites through here, so they all show the same signatures, positions and cleared state.
    /// </summary>
    public readonly struct KnownSite
    {
        public readonly ArchaeologicalSiteDefinition Def;
        public readonly int Level;
        public readonly bool Cleared;
        public readonly Vector3 Estimate;
        public readonly float Uncertainty;

        public KnownSite(ArchaeologicalSiteDefinition def, int level, bool cleared)
        {
            Def = def; Level = level; Cleared = cleared;
            Estimate = def.position + ExplorationService.EstimateOffset(def, level);
            Uncertainty = ExplorationService.Uncertainty(level);
        }

        public bool Pinpointed => Level >= (int)SiteRevealLevel.Pinpointed;
        public Vector2 Map => new Vector2(Estimate.x, Estimate.z);

        /// <summary>Map label: the site name once pinpointed, otherwise <paramref name="unresolved"/>; "(cleared)" when every find is recovered.</summary>
        public string Label(string unresolved = "Signature") => (Pinpointed ? Def.displayName : unresolved) + (Cleared ? " (cleared)" : "");
    }

    public static class KnownSites
    {
        /// <summary>Sites on <paramref name="bodyId"/> with reveal level above Hidden, in definition order.</summary>
        public static IEnumerable<KnownSite> OnBody(ExplorationService x, string bodyId)
        {
            if (x == null || string.IsNullOrEmpty(bodyId)) yield break;
            foreach (var site in x.SitesOnBody(bodyId))
            {
                var k = Get(x, site);
                if (k.HasValue) yield return k.Value;
            }
        }

        /// <summary>The known state of one site, or null while it is still hidden.</summary>
        public static KnownSite? Get(ExplorationService x, ArchaeologicalSiteDefinition site)
        {
            if (x == null || site == null) return null;
            int level = x.Site(site.Id).revealLevel;
            if (level <= (int)SiteRevealLevel.Hidden) return null;
            return new KnownSite(site, level, IsCleared(x, site));
        }

        public static bool IsCleared(ExplorationService x, ArchaeologicalSiteDefinition site)
            => site != null && (x.IsCleared(site.Id) || DMI.Xenoarch.Excavation.ExcavationSite.IsCleared(site));
    }
}
