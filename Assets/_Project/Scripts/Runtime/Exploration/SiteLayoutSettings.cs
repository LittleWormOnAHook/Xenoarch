using System;
using UnityEngine;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>How the editor spreads dig sites and their landing zones over a planet's terrain (data only).</summary>
    [Serializable]
    public sealed class SiteLayoutSettings
    {
        [Tooltip("Minimum distance between dig sites, and from the base landing zone (m).")] public float spacing = 280f;
        [Tooltip("Farthest a dig site may be from the base landing zone (m).")] public float maxFromBase = 520f;
        [Tooltip("Keep this far inside the terrain edges (m).")] public float edgeMargin = 180f;
        [Tooltip("Steepest ground for a site / landing zone (deg).")] public float maxSlope = 9f;
        [Tooltip("Ground within this radius must be flat (m).")] public float flatRadius = 14f;
        [Tooltip("Max height difference inside flatRadius (m).")] public float maxRelief = 1.6f;
        [Tooltip("Landing zone distance from its dig site's edge (m).")] public float landingOffset = 22f;
        [Tooltip("Candidate grid step (m).")] public float gridStep = 16f;
    }
}
