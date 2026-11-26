using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Valuables
{
    public enum LootTier { Common = 0, Uncommon = 1, Rare = 2, SuperRare = 3 }

    [Serializable] public sealed class LootEntry { public string itemId; [Min(1)] public int min = 1; [Min(1)] public int max = 1; }

    [Serializable]
    public sealed class LootTierEntry
    {
        public LootTier tier;
        [Tooltip("Relative chance of a cache rolling this tier.")] [Min(0f)] public float weight = 1f;
        [Tooltip("One of these is picked (equal chance) per roll.")] public List<LootEntry> items = new List<LootEntry>();
        [Tooltip("Extra rolls from the same tier list.")] [Min(0)] public int extraRolls;
    }

    /// <summary>
    /// Loot for random valuable caches (not part of the discovery chain): weighted tiers up to super rare, each with an
    /// item list. Pure data; rolls are deterministic per cache seed so a cache always holds the same thing.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Valuables/Loot Table", fileName = "Loot_")]
    public sealed class LootTableDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "loot.";
        public List<LootTierEntry> tiers = new List<LootTierEntry>();

        public LootTier RollTier(System.Random rng)
        {
            float total = 0f; foreach (var t in tiers) if (t != null) total += t.weight;
            if (total <= 0f) return LootTier.Common;
            double r = rng.NextDouble() * total;
            foreach (var t in tiers) { if (t == null) continue; r -= t.weight; if (r <= 0) return t.tier; }
            return tiers[tiers.Count - 1].tier;
        }

        public List<(string itemId, int count)> Roll(LootTier tier, System.Random rng)
        {
            var result = new List<(string, int)>();
            var t = tiers.Find(e => e != null && e.tier == tier);
            if (t == null || t.items.Count == 0) return result;
            for (int i = 0; i <= t.extraRolls; i++)
            {
                var e = t.items[rng.Next(t.items.Count)];
                if (e == null || string.IsNullOrEmpty(e.itemId)) continue;
                result.Add((e.itemId, rng.Next(e.min, Mathf.Max(e.min, e.max) + 1)));
            }
            return result;
        }
    }

    [Serializable]
    public struct ValuableSiteSettings
    {
        [Tooltip("Random valuable caches per landing region.")] public int perBody;
        [Tooltip("Minimum distance between caches (m).")] public float spacing;
        [Tooltip("Keep this far from dig grids and landing zones (m).")] public float clearance;
        public float minDepth, maxDepth;
        [Tooltip("Steepest ground a cache may be buried under (deg).")] public float maxSlope;
        [Tooltip("Extra caches around each landing zone (on top of perBody, which spreads over the whole surface map).")] public int perLandingZone;
        [Tooltip("Ring around a landing zone for its caches (min, max m).")] public Vector2 zoneRing;
        public static ValuableSiteSettings Default => new ValuableSiteSettings { perBody = 7, spacing = 40f, clearance = 18f, minDepth = 0.5f, maxDepth = 1.0f, maxSlope = 28f, perLandingZone = 1, zoneRing = new Vector2(30f, 110f) };
    }

    /// <summary>One generated cache: deterministic from (body, galaxy seed, index) and the terrain.</summary>
    public struct ValuableSite { public string id; public Vector3 position; public float depth; public int seed; }

    [Serializable] public sealed class CacheState { public string id; public bool revealed, collected; public int tier = -1; }
    [Serializable] public sealed class CacheData { public List<CacheState> caches = new List<CacheState>(); }

    /// <summary>State of random valuable caches (revealed by the handheld scanner, collected by digging). Saved.</summary>
    public sealed class ValuableService
    {
        public const string Key = "world.caches";
        public CacheData Data { get; private set; } = new CacheData();
        public event Action Changed;

        public ValuableService(Save.SaveService saves) { saves?.Register(new Section(this)); }

        public CacheState State(string id)
        {
            foreach (var c in Data.caches) if (c.id == id) return c;
            var s = new CacheState { id = id }; Data.caches.Add(s); return s;
        }
        public bool IsRevealed(string id) { foreach (var c in Data.caches) if (c.id == id) return c.revealed; return false; }
        public bool IsCollected(string id) { foreach (var c in Data.caches) if (c.id == id) return c.collected; return false; }
        public void Reveal(string id) { var s = State(id); if (s.revealed) return; s.revealed = true; Changed?.Invoke(); }
        public void Collect(string id, LootTier tier) { var s = State(id); bool first = !s.collected; s.collected = true; s.tier = (int)tier; Changed?.Invoke(); if (first) Collected?.Invoke(id); }
        /// <summary>Phase 1D: first collection of a cache (notoriety hook).</summary>
        public event Action<string> Collected;

        /// <summary>Spread caches over a region on walkable ground, away from dig grids / landing zones and each other.</summary>
        public static List<ValuableSite> Generate(string bodyId, int galaxySeed, Rect region, ValuableSiteSettings s,
            Exploration.LandingZonePicker.HeightFn height, IList<(Vector3 centre, float radius)> avoid, IList<Vector3> zones = null)
        {
            var list = new List<ValuableSite>();
            if (s.perBody <= 0 || region.width < 10f) return list;
            int seed = 17; foreach (char ch in bodyId ?? "") seed = seed * 31 + ch; seed ^= galaxySeed;
            var rng = new System.Random(seed);
            float margin = Mathf.Min(20f, region.width * 0.1f);
            for (int tries = 0; tries < s.perBody * 60 && list.Count < s.perBody; tries++)
            {
                float x = Mathf.Lerp(region.xMin + margin, region.xMax - margin, (float)rng.NextDouble());
                float z = Mathf.Lerp(region.yMin + margin, region.yMax - margin, (float)rng.NextDouble());
                float depth = Mathf.Lerp(s.minDepth, s.maxDepth, (float)rng.NextDouble());
                int cacheSeed = rng.Next();
                if (!height(x, z, out float y, out var n) || Vector3.Angle(n, Vector3.up) > s.maxSlope) continue;
                var p = new Vector3(x, y, z);
                bool ok = true;
                foreach (var c in list) if ((Flat(c.position) - Flat(p)).sqrMagnitude < s.spacing * s.spacing) { ok = false; break; }
                if (ok && avoid != null) foreach (var a in avoid) if ((Flat(a.centre) - Flat(p)).magnitude < a.radius + s.clearance) { ok = false; break; }
                if (!ok) continue;
                list.Add(new ValuableSite { id = $"cache.{bodyId?.Replace("body.", "")}.{list.Count:00}", position = p, depth = depth, seed = cacheSeed });
            }
            // Around each landing zone (ids continue after the map-wide ones; same spacing / clearance rules).
            if (zones != null && s.perLandingZone > 0)
            {
                float rMin = Mathf.Max(s.clearance, s.zoneRing.x), rMax = Mathf.Max(rMin + 5f, s.zoneRing.y);
                foreach (var zc in zones)
                {
                    int want = s.perLandingZone, got = 0;
                    for (int tries = 0; tries < want * 60 && got < want; tries++)
                    {
                        float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Lerp(rMin, rMax, (float)rng.NextDouble());
                        float depth = Mathf.Lerp(s.minDepth, s.maxDepth, (float)rng.NextDouble());
                        int cacheSeed = rng.Next();
                        float x = zc.x + Mathf.Cos(a) * r, z = zc.z + Mathf.Sin(a) * r;
                        if (!height(x, z, out float y, out var n) || Vector3.Angle(n, Vector3.up) > s.maxSlope) continue;
                        var p = new Vector3(x, y, z);
                        bool ok = true;
                        foreach (var c in list) if ((Flat(c.position) - Flat(p)).sqrMagnitude < s.spacing * s.spacing) { ok = false; break; }
                        if (ok && avoid != null) foreach (var av in avoid) if ((Flat(av.centre) - Flat(p)).magnitude < av.radius + s.clearance) { ok = false; break; }
                        if (!ok) continue;
                        list.Add(new ValuableSite { id = $"cache.{bodyId?.Replace("body.", "")}.{list.Count:00}", position = p, depth = depth, seed = cacheSeed });
                        got++;
                    }
                }
            }
            return list;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        sealed class Section : Save.ISaveSection
        {
            readonly ValuableService _s;
            public Section(ValuableService s) { _s = s; }
            public string Key => ValuableService.Key;
            public int RestoreOrder => 20;
            public string CaptureJson() => JsonUtility.ToJson(_s.Data);
            public void RestoreJson(string json) { _s.Data = JsonUtility.FromJson<CacheData>(json) ?? new CacheData(); _s.Changed?.Invoke(); }
            public void ResetToNewGame() { _s.Data = new CacheData(); _s.Changed?.Invoke(); }
        }
    }
}
