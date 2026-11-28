using DMI.Xenoarch.Core;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Materials;
using ArtifactInstance = DMI.Xenoarch.Artifacts.ArtifactInstance;
using UnityEngine;

namespace DMI.Xenoarch.UI
{
    /// <summary>
    /// UI-only grouping of the 17 material item ids (ids and recipes are unchanged). Ore, Regolith, Volatiles and
    /// Salts are shown on spec sheets; refined products stay labelled Refined. Grade / purity is a per-stack field
    /// (0 on older saves = Typical, with a few item-specific default labels such as He-3 rich).
    /// The Helium-3 Regolith icon is a stand-in (icon_resource_helium3_regolith_standin) until a dedicated atlas icon exists.
    /// </summary>
    public static class ResourceGroups
    {
        public enum Kind { None, Ore, Regolith, Volatiles, Salts, Refined }

        public static Kind Of(string itemId) => itemId switch
        {
            "material.iron_ore" or "material.nickel_iron_ore" or "material.copper_sulfide"
                or "material.auriferous_quartz" or "material.platinoid_ore" => Kind.Ore,
            "material.regolith" or "material.helium3" => Kind.Regolith,
            "material.water_ice" or "material.methane_frost" => Kind.Volatiles,
            "material.mineral_salts" => Kind.Salts,
            "material.alloy_ingot" or "material.copper_ingot" or "material.nickel_ingot"
                or "material.gold_ingot" or "material.platinum_ingot"
                or "material.fuel_cell" or "material.lab_reagent" => Kind.Refined,
            _ => Kind.None
        };

        public static string GroupLabel(ItemDefinition d)
        {
            if (d == null) return "";
            var k = Of(d.Id);
            if (k != Kind.None) return "Resource · " + k;
            return d.category.ToString();
        }

        /// <summary>Per-stack grade if set, otherwise a stable default label so the spec sheet always has a value.</summary>
        public static string GradeLabel(ItemDefinition d, ItemStack st)
        {
            if (st != null && st.grade > 0.001f) return $"{st.grade * 100f:0}% purity";
            if (d == null) return "—";
            return d.Id switch
            {
                "material.helium3" => "He-3 rich",
                "material.auriferous_quartz" => "Gold-bearing",
                "material.platinoid_ore" => "PGM-bearing",
                _ => d is MaterialDefinition ? "Typical" : "—"
            };
        }

        public static string OriginLabel(ItemStack st, ArtifactInstance art)
        {
            string body = art != null ? art.bodyId : st?.originBodyId;
            string site = art != null ? art.siteId : st?.originSiteId;
            body = Pretty(body);
            site = Pretty(site);
            if (string.IsNullOrEmpty(body) && string.IsNullOrEmpty(site)) return "Unknown";
            if (string.IsNullOrEmpty(body)) return site;
            if (string.IsNullOrEmpty(site)) return body;
            return body + " · " + site;
        }

        public static string MaterialType(ItemDefinition d)
        {
            if (d is DMI.Xenoarch.Artifacts.ArtifactDefinition a && !string.IsNullOrEmpty(a.material)) return a.material;
            if (d is MaterialDefinition) return GroupLabel(d);
            return d != null ? d.category.ToString() : "—";
        }

        static string Pretty(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            // body.proxima_centauri_c / site.vael_shrine -> readable
            var s = id;
            int dot = s.IndexOf('.');
            if (dot >= 0 && dot < s.Length - 1) s = s.Substring(dot + 1);
            return s.Replace('_', ' ');
        }
    }
}
