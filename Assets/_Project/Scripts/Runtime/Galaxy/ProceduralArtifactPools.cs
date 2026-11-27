using System;
using System.Collections.Generic;
using DMI.Xenoarch.Items;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>One entry of a weighted procedural artifact pool (future v2+). Order in the list is part of the contract.</summary>
    [Serializable]
    public struct WeightedArtifactEntry
    {
        public string artifactId;
        public int weight;
        /// <summary>Content tag (common fragment, ancient tablet, relic, tech component, civilization, rare, location clue).</summary>
        public string tag;
        public WeightedArtifactEntry(string id, int weight, string tag) { artifactId = id; this.weight = weight; this.tag = tag; }
    }

    /// <summary>
    /// Phase 2 M1: versioned procedural artifact pools. Generated dig sites draw from an explicit, ordered, frozen id list
    /// instead of whatever the ItemRegistry happens to contain, so adding artifacts never reshuffles existing generated
    /// sites. Rules:
    /// <list type="bullet">
    /// <item><b>v1 is frozen.</b> It is exactly what the t186u registry filter produced (verified by the Galaxy Snapshot
    /// baseline): only <c>artifact.unknown_lump</c>. Never edit, reorder or filter it; existing sites draw
    /// <c>pool[r.Next(pool.Count)]</c> from it in the generator's original RNG order.</item>
    /// <item><b>New content goes in a new version</b> (weighted v2) drawn from its own independent RNG stream
    /// (<see cref="ProceduralGalaxy.Stream"/>), for new spawns/POIs only, so v1 sites are unaffected.</item>
    /// </list>
    /// </summary>
    public static class ProceduralArtifactPools
    {
        /// <summary>Content version of procedural generation. Bump only together with a new pool/stream; never change v1.</summary>
        public const int ContentVersion = 1;

        /// <summary>Frozen procedural artifact pool v1 (explicit order). DO NOT EDIT.</summary>
        public static readonly IReadOnlyList<string> V1 = Array.AsReadOnly(new[] { "artifact.unknown_lump" });

        /// <summary>Reserved for the weighted v2 pool (M3/M9): common fragments, ancient tablets, unidentified relics,
        /// technological components, civilization-specific artifacts, rare discoveries, location clues. Empty in M1.</summary>
        public static readonly IReadOnlyList<WeightedArtifactEntry> V2 = Array.AsReadOnly(new WeightedArtifactEntry[0]);

        /// <summary>Pool used by the generator for a content version (v1 for every current generated site).</summary>
        public static IList<string> ForVersion(int version)
        {
            if (version != 1) throw new ArgumentOutOfRangeException(nameof(version), $"No procedural artifact pool v{version} yet");
            return new List<string>(V1);
        }

        /// <summary>Deterministic weighted pick (explicit list order, integer weights). Null if the pool is empty.</summary>
        public static string PickWeighted(IReadOnlyList<WeightedArtifactEntry> pool, Random r)
        {
            if (pool == null || r == null) return null;
            int total = 0;
            foreach (var e in pool) if (e.weight > 0 && !string.IsNullOrEmpty(e.artifactId)) total += e.weight;
            if (total <= 0) return null;
            int roll = r.Next(total);
            foreach (var e in pool)
            {
                if (e.weight <= 0 || string.IsNullOrEmpty(e.artifactId)) continue;
                if (roll < e.weight) return e.artifactId;
                roll -= e.weight;
            }
            return null;
        }

        /// <summary>The pre-M1 (t186u) registry filter, kept ONLY so tooling can prove v1 equals what it produced:
        /// common to rare, no install target, no fragment group, no set, in ItemRegistry order.</summary>
        public static List<string> LegacyRegistryFilter(IItemLookup items)
        {
            var l = new List<string>();
            if (items is ItemRegistry reg && reg.Entries != null)
                foreach (var e in reg.Entries)
                    if (e is DMI.Xenoarch.Artifacts.ArtifactDefinition a && a.rarity <= DMI.Xenoarch.Artifacts.ArtifactRarity.Rare
                        && a.installTarget == DMI.Xenoarch.Artifacts.TechInstallTarget.None && string.IsNullOrEmpty(a.fragmentOf) && string.IsNullOrEmpty(a.setId))
                        l.Add(a.Id);
            return l;
        }
    }
}
