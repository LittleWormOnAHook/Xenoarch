using UnityEngine;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>
    /// Hand-editable landing spot for the shuttle in a planet scene. One per dig site (<see cref="siteId"/>), plus an
    /// optional base zone (empty siteId) used when no site was chosen. Setup places these once by auto-pick and never
    /// moves them again: drag them anywhere in edit mode (the shuttle is snapped to the terrain height at runtime,
    /// rotation Y = shuttle heading). Sites without a marker are auto-picked at runtime.
    /// </summary>
    public sealed class LandingZone : MonoBehaviour
    {
        [Tooltip("Dig site this zone serves (site.* id). Empty = base landing zone.")] public string siteId = "";
        [Tooltip("Shuttle footprint radius shown by the gizmo (m).")] public float footprint = 8f;
        [Tooltip("Where the player steps out, relative to the shuttle (local, m). Zero = beside the shuttle, facing the site.")] public Vector3 playerOffset = Vector3.zero;
        [HideInInspector] public Vector3 siteHint;
        [Tooltip("Set by setup when it auto-placed this marker. Setup only re-picks a marker that is still exactly at its auto-picked spot and was a relaxed (steep) pick; once you move it, it is yours.")]
        [HideInInspector] public bool autoPlaced;
        [HideInInspector] public bool relaxedPick;
        [HideInInspector] public Vector3 autoPosition;
        public bool UntouchedRelaxedAuto => autoPlaced && relaxedPick && (transform.position - autoPosition).sqrMagnitude < 1e-4f;
        [HideInInspector] public bool hasSiteHint;

        public static LandingZone Find(string siteId)
        {
            siteId ??= "";
            foreach (var z in FindObjectsByType<LandingZone>(FindObjectsInactive.Exclude))
                if ((z.siteId ?? "") == siteId) return z;
            return null;
        }

        void OnDrawGizmos()
        {
            var p = transform.position;
            Gizmos.color = new Color(0.3f, 1f, 0.55f, 0.9f);
            const int seg = 40;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Gizmos.DrawLine(p + new Vector3(Mathf.Sin(a0), 0.1f, Mathf.Cos(a0)) * footprint, p + new Vector3(Mathf.Sin(a1), 0.1f, Mathf.Cos(a1)) * footprint);
            }
            var fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Gizmos.DrawLine(p + Vector3.up * 0.2f, p + Vector3.up * 0.2f + fwd * footprint * 1.2f);
            Gizmos.DrawWireCube(p + Vector3.up * 1.5f, new Vector3(1.5f, 3f, 1.5f));
            if (hasSiteHint) { Gizmos.color = new Color(1f, 0.6f, 0.15f, 0.6f); Gizmos.DrawLine(p + Vector3.up * 0.5f, siteHint + Vector3.up * 0.5f); }
#if UNITY_EDITOR
            UnityEditor.Handles.Label(p + Vector3.up * 3.5f, string.IsNullOrEmpty(siteId) ? "Landing zone (base)" : $"Landing zone: {siteId}");
#endif
        }

#if UNITY_EDITOR
        [ContextMenu("Snap to terrain")]
        void SnapToTerrain()
        {
            var y = DMI.Xenoarch.Excavation.ExcavationSite.TerrainHeight(transform.position);
            if (!y.HasValue) return;
            UnityEditor.Undo.RecordObject(transform, "Snap landing zone");
            var p = transform.position; p.y = y.Value; transform.position = p;
        }
#endif
    }
}
