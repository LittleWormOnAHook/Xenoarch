using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.World
{
    /// <summary>
    /// Decorative surface rocks (Poly Haven CC0 models in Assets/Xenoarch_Free, prefabs in Resources/World/Rocks).
    /// Per-body counts, sizes and tint; bodies without an entry use <see cref="fallback"/> tinted from the body's map
    /// colour, so new planets get rocks with no extra setup. Placement is runtime + seeded (see RockScatterDirector).
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
            [Tooltip("Largest horizontal size range (m); prefabs are normalised to 1 m.")] public Vector2 size = new Vector2(0.6f, 2.2f);
        }

        [Serializable]
        public sealed class BodyRocks
        {
            public string bodyId;
            [Tooltip("Rocks over the body's surface map rectangle.")] [Min(0)] public int count = 160;
            [Tooltip("Share of rocks placed as small clusters (2-4) around a seed rock.")] [Range(0f, 1f)] public float clusterShare = 0.35f;
            [Tooltip("Multiplies the base colour of the rock materials.")] public Color tint = Color.white;
            [Min(0.1f)] public float sizeMultiplier = 1f;
            [Tooltip("Steepest ground a rock may sit on (deg).")] public float maxSlope = 30f;
        }

        public List<RockKind> rocks = new List<RockKind>();
        public List<BodyRocks> bodies = new List<BodyRocks>();
        [Tooltip("Used for bodies with no entry; tint = lerp(white, body map colour, fallbackTintFromMap).")] public BodyRocks fallback = new BodyRocks { bodyId = "*", count = 140 };
        [Range(0f, 1f)] public float fallbackTintFromMap = 0.5f;

        [Header("Clearances (m, added to each object's own radius)")]
        public float siteClearance = 8f;
        public float zoneClearance = 12f;
        public float baseClearance = 30f;
        public float cacheClearance = 5f;
        public float nodeClearance = 4f;
        [Tooltip("Half-width of the walking corridor kept clear between the base zone and each site zone.")] public float pathHalfWidth = 6f;
        public float rockSpacing = 3f;
        [Tooltip("Rocks are sunk into the ground by this share of their size.")] [Range(0f, 0.6f)] public float sink = 0.2f;
        [Tooltip("Cull rocks smaller than this share of the screen height.")] [Range(0.001f, 0.1f)] public float cullScreenSize = 0.02f;

        public BodyRocks For(string bodyId, Color mapColor)
        {
            foreach (var b in bodies) if (b != null && b.bodyId == bodyId) return b;
            return new BodyRocks
            {
                bodyId = bodyId, count = fallback.count, clusterShare = fallback.clusterShare, sizeMultiplier = fallback.sizeMultiplier,
                maxSlope = fallback.maxSlope, tint = Color.Lerp(Color.white, mapColor, fallbackTintFromMap),
            };
        }
    }
}
