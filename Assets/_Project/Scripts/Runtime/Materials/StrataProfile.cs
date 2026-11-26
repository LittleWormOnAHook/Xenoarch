using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Materials
{
    /// <summary>One depth band of a body's ground: digging through it yields this material.</summary>
    [Serializable]
    public sealed class StrataBand
    {
        public string label = "layer";
        [Tooltip("Top of the band below the surface (m).")] [Min(0f)] public float minDepth;
        [Tooltip("Bottom of the band below the surface (m).")] [Min(0f)] public float maxDepth = 1f;
        [Tooltip("Material item id (MaterialDefinition) found in this band.")] public string itemId;
        [Tooltip("Cubic metres of dug ground per unit found (lower = richer).")] [Min(0.001f)] public float volumePerUnit = 0.08f;
        [Tooltip("Chance a full unit's worth of digging actually holds one (rare metals < 1).")] [Range(0f, 1f)] public float chance = 1f;
    }

    /// <summary>
    /// Layered ground of one or more bodies: depth bands of raw materials (regolith on top, metal ores deeper, gold and
    /// platinoids deepest). Excavation strokes yield the materials of the bands they cut through; the refiner turns the
    /// ores into rare metal ingots.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Materials/Strata Profile", fileName = "Strata_")]
    public sealed class StrataProfile : ScriptableObject
    {
        public List<string> bodyIds = new List<string>();
        public List<StrataBand> bands = new List<StrataBand>();

        public IEnumerable<StrataBand> BandsAt(float depth)
        {
            foreach (var b in bands) if (b != null && depth >= b.minDepth && depth < b.maxDepth) yield return b;
        }

        public static StrataProfile For(IEnumerable<StrataProfile> all, string bodyId)
        {
            if (all == null || string.IsNullOrEmpty(bodyId)) return null;
            foreach (var p in all) if (p != null && p.bodyIds.Contains(bodyId)) return p;
            return null;
        }
    }

    /// <summary>
    /// Turns dug volume into materials. Volume per (site, band) accumulates across strokes (saved with the materials
    /// state); every <see cref="StrataBand.volumePerUnit"/> rolls <see cref="StrataBand.chance"/> for one unit.
    /// </summary>
    public static class StrataYield
    {
        /// <summary>Applies dug cells (y index + removed fraction) at a site; returns materials found (id, count).</summary>
        public static List<(string id, int n)> Dig(MaterialsService m, StrataProfile profile, string siteId, float cell, IEnumerable<(int y, float frac)> cuts, System.Random rng, float yieldMultiplier = 1f)
        {
            var found = new List<(string, int)>();
            if (m == null || profile == null || cuts == null) return found;
            float vol = cell * cell * cell * Mathf.Max(0f, yieldMultiplier); // scaled volume accumulates, so fractions carry over
            var gained = new Dictionary<string, int>();
            foreach (var (y, frac) in cuts)
            {
                float depth = (y + 0.5f) * cell;
                foreach (var b in profile.BandsAt(depth))
                {
                    if (string.IsNullOrEmpty(b.itemId)) continue;
                    string key = siteId + "|" + b.itemId + "|" + b.minDepth.ToString("0.##");
                    float acc = m.AddStrataVolume(key, vol * frac);
                    while (acc >= b.volumePerUnit)
                    {
                        acc = m.AddStrataVolume(key, -b.volumePerUnit);
                        if (rng.NextDouble() <= b.chance) { gained.TryGetValue(b.itemId, out var g); gained[b.itemId] = g + 1; }
                    }
                }
            }
            foreach (var kv in gained) found.Add((kv.Key, kv.Value));
            return found;
        }
    }
}
