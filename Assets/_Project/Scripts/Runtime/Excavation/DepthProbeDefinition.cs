using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>One Depth Probe upgrade tier (index = GameSession upgrade level "scanner.depth_probe").</summary>
    [Serializable]
    public sealed class DepthProbeTier
    {
        public string label = "Mk I";
        [Tooltip("Buried objects tracked at once (1-3), each with its own readout and surface marker.")] [Range(1, 3)] public int tracks = 1;
        [Tooltip("Horizontal reach from the probed point (m).")] public float range = 1.5f;
        [Tooltip("Deepest object it can sense below the probed surface (m).")] public float maxDepth = 1.2f;
        [Tooltip("Coarse tier: report depth bands only (no metre figure).")] public bool bandsOnly = true;
        [Tooltip("Depth error (m, +/-) on the metre figure; stable per object.")] public float noise = 0.1f;
        [Tooltip("Top tier: draws a hologram scan volume with a wire blob per tracked object.")] public bool hologram;
    }

    /// <summary>
    /// The third scanner: a handheld Depth Probe for inside a pit. Point it at the dug surface and pulse / hold LMB: it
    /// reports roughly how far below that point the nearest buried object (artifact or cache) lies, and which way it is
    /// offset, without saying what it is. Data in Resources/Excavation (first DepthProbeDefinition found).
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Depth Probe", fileName = "DepthProbe")]
    public sealed class DepthProbeDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "probe.";
        public string itemId = "item.tool.depth_probe";
        public string displayName = "Depth Probe";
        [Tooltip("Upgrade tiers; index 0 = base. Upgrades (crafting) raise the tier.")]
        public List<DepthProbeTier> tiers = new List<DepthProbeTier>
        {
            new DepthProbeTier { label = "Mk I", tracks = 1, range = 1.5f, maxDepth = 1.2f, bandsOnly = true, noise = 0.12f },
            new DepthProbeTier { label = "Mk II", tracks = 2, range = 2.5f, maxDepth = 2f, bandsOnly = false, noise = 0.08f },
            new DepthProbeTier { label = "Mk III", tracks = 3, range = 3.5f, maxDepth = 3f, bandsOnly = false, noise = 0.03f },
            new DepthProbeTier { label = "Mk IV Holo", tracks = 3, range = 4.5f, maxDepth = 4.5f, bandsOnly = false, noise = 0.02f, hologram = true },
        };
        [Tooltip("Band edges (m) for coarse readings: < first, first-second, ..., deep.")] public float[] bandEdges = { 0.10f, 0.30f, 0.60f };
        [Tooltip("Under this depth (m) the probe warns: very close, switch to the brush.")] public float veryClose = 0.12f;
        [Tooltip("Offsets smaller than this (m) read as 'straight down'.")] public float straightDown = 0.15f;
        [Tooltip("How far the probe reaches to the aimed surface (m).")] public float reach = 5f;
        [Tooltip("Seconds between readings while LMB is held.")] public float holdInterval = 0.25f;
        [Tooltip("Seconds a reading stays on screen after release.")] public float readingSeconds = 6f;

        public const string UpgradeId = "scanner.depth_probe";
        public const string ResourceFolder = "Excavation";
        static DepthProbeDefinition _cached;
        public static DepthProbeDefinition Load()
        {
            if (_cached != null) return _cached;
            var all = Resources.LoadAll<DepthProbeDefinition>(ResourceFolder);
            _cached = all != null && all.Length > 0 ? all[0] : CreateInstance<DepthProbeDefinition>();
            return _cached;
        }

        public DepthProbeTier Tier(int level) => tiers == null || tiers.Count == 0 ? new DepthProbeTier() : tiers[Mathf.Clamp(level, 0, tiers.Count - 1)];
        public int MaxLevel => tiers != null ? Mathf.Max(0, tiers.Count - 1) : 0;

        public string Band(float depth)
        {
            var e = bandEdges ?? new float[0];
            if (e.Length == 0) return $"~{depth:0.0} m";
            if (depth < e[0]) return $"under {e[0] * 100f:0} cm";
            for (int i = 1; i < e.Length; i++) if (depth < e[i]) return $"{e[i - 1] * 100f:0}-{e[i] * 100f:0} cm";
            return $"deep (over {e[e.Length - 1] * 100f:0} cm)";
        }
    }
}
