using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.World
{
    /// <summary>
    /// Decorative surface rocks (Poly Haven CC0 models in Assets/Xenoarch_Free, prefabs in Resources/World/Rocks).
    /// Per-body counts, sizes and tint; bodies without an entry use <see cref="fallback"/> tinted from the body's map
    /// colour, so new planets get rocks with no extra setup. Placement is runtime + seeded (see RockScatterDirector).
    /// ZEN-25: rock sizes come from <see cref="sizeClasses"/> (Large / Medium / Small / Tiny, weights + size ranges +
    /// collider + cull size); slope / cliff clusters gather mixed sizes at the foot of steep ground.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/World/Rock Scatter Profile")]
    public sealed class RockScatterProfile : ScriptableObject
    {
        public const string ResourcePath = "World/RockScatterProfile";

        [Serializable]
        public sealed class RockKind
        {
            public GameObject prefab;
            [Min(0f)] public float weight = 1f;
            [Tooltip("Legacy size range (m), used only when no size classes are set; prefabs are normalised to 1 m.")] public Vector2 size = new Vector2(0.6f, 2.2f);
            [Tooltip("Heavy model (e.g. boulder_01, 124k tris): only used for size classes that allow large models.")] public bool largeOnly;
        }

        [Serializable]
        public sealed class SizeClass
        {
            public string name = "Medium";
            [Min(0f)] public float weight = 1f;
            [Tooltip("Largest horizontal size range (m).")] public Vector2 size = new Vector2(1f, 2f);
            [Tooltip("Keep the convex mesh collider (off for tiny pebbles: cheaper, walk over them).")] public bool collider = true;
            [Tooltip("Cull below this share of screen height (higher = culled sooner).")] [Range(0.001f, 0.1f)] public float cullScreenSize = 0.02f;
            [Tooltip("Sunk into the ground by this share of the size (plus up to 0.1 random).")] [Range(0f, 0.6f)] public float sink = 0.2f;
            [Tooltip("May use largeOnly models (boulder_01).")] public bool allowLargeModels;
        }

        [Serializable]
        public sealed class BodyRocks
        {
            public string bodyId;
            [Tooltip("Scattered rocks over the body's surface map rectangle.")] [Min(0)] public int count = 160;
            [Tooltip("Share of big scattered rocks that get a small group (2-4) around them.")] [Range(0f, 1f)] public float clusterShare = 0.35f;
            [Tooltip("Multiplies the base colour of the rock materials.")] public Color tint = Color.white;
            [Min(0.1f)] public float sizeMultiplier = 1f;
            [Tooltip("Steepest ground a scattered rock may sit on (deg).")] public float maxSlope = 30f;
            [Tooltip("Rock clusters at the foot of cliffs and on slopes.")] [Min(0)] public int slopeClusters = 40;
            [Tooltip("Rocks per slope / cliff cluster (min, max).")] public Vector2Int clusterRocks = new Vector2Int(4, 8);
            [Tooltip("Ground at least this steep (deg) counts as a cliff: its cluster goes to the foot of it.")] public float cliffSlope = 38f;
            [Tooltip("Steepest ground a cluster rock may sit on (deg).")] public float clusterMaxSlope = 45f;
        }

        public List<RockKind> rocks = new List<RockKind>();
        public List<SizeClass> sizeClasses = DefaultClasses();
        public List<BodyRocks> bodies = new List<BodyRocks>();
        [Tooltip("Used for bodies with no entry; tint = lerp(white, body map colour, fallbackTintFromMap).")] public BodyRocks fallback = new BodyRocks { bodyId = "*", count = 380 };
        [Range(0f, 1f)] public float fallbackTintFromMap = 0.5f;

        [Header("Clearances (m, added to each object's own radius)")]
        public float siteClearance = 8f;
        public float zoneClearance = 12f;
        public float baseClearance = 30f;
        public float cacheClearance = 5f;
        public float nodeClearance = 4f;
        [Tooltip("Half-width of the walking corridor kept clear between the base zone and each site zone.")] public float pathHalfWidth = 6f;
        [Tooltip("Gap between scattered rocks (scaled down for small rocks and inside clusters).")] public float rockSpacing = 3f;
        [Tooltip("Legacy sink, used only when no size classes are set.")] [Range(0f, 0.6f)] public float sink = 0.2f;
        [Tooltip("Legacy cull size, used only when no size classes are set.")] [Range(0.001f, 0.1f)] public float cullScreenSize = 0.02f;

        public static List<SizeClass> DefaultClasses() => new List<SizeClass>
        {
            new SizeClass { name = "Large", weight = 0.06f, size = new Vector2(2.2f, 3.8f), collider = true, cullScreenSize = 0.008f, sink = 0.25f, allowLargeModels = true },
            new SizeClass { name = "Medium", weight = 0.2f, size = new Vector2(1.0f, 2.2f), collider = true, cullScreenSize = 0.012f, sink = 0.22f },
            new SizeClass { name = "Small", weight = 0.34f, size = new Vector2(0.4f, 1.0f), collider = true, cullScreenSize = 0.02f, sink = 0.2f },
            new SizeClass { name = "Tiny", weight = 0.4f, size = new Vector2(0.12f, 0.4f), collider = false, cullScreenSize = 0.035f, sink = 0.15f },
        };

        public BodyRocks For(string bodyId, Color mapColor)
        {
            foreach (var b in bodies) if (b != null && b.bodyId == bodyId) return b;
            return new BodyRocks
            {
                bodyId = bodyId, count = fallback.count, clusterShare = fallback.clusterShare, sizeMultiplier = fallback.sizeMultiplier,
                maxSlope = fallback.maxSlope, tint = Color.Lerp(Color.white, mapColor, fallbackTintFromMap),
                slopeClusters = fallback.slopeClusters, clusterRocks = fallback.clusterRocks, cliffSlope = fallback.cliffSlope, clusterMaxSlope = fallback.clusterMaxSlope,
            };
        }
    }
}