using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// Phase 1D buried chamber (data in Resources/Excavation/Chambers). Dig the host site down to its floor at
    /// <see cref="entranceCell"/> to uncover a sealed door; opening it leads (short fade) into a tunnel and a room
    /// <see cref="depthBelow"/> m under the site. The room floor is a diggable bed of soil (<see cref="dirtDepth"/> + the
    /// bedrock layer) with its own finds, saved like any dig site under <see cref="FloorSiteId"/>. Built at runtime by
    /// <see cref="ChamberDirector"/>; scene files are not changed.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Excavation/Buried Chamber", fileName = "Chamber_")]
    public sealed class ChamberDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "chamber.";
        public const string ResourceFolder = "Excavation/Chambers";
        public static ChamberDefinition[] All() => Resources.LoadAll<ChamberDefinition>(ResourceFolder);

        public string displayName = "Sealed chamber";
        [Tooltip("Dig site whose floor hides the door.")] public string hostSiteId;
        [Tooltip("Host grid column (x, z) where the door lies on the site floor.")] public Vector2Int entranceCell = new Vector2Int(6, 6);
        [Tooltip("Door width / length (m).")] public float doorSize = 1.4f;
        [Header("Layout (m)")]
        [Tooltip("How far below the host site floor the chamber is built.")] public float depthBelow = 24f;
        [Tooltip("Yaw of the tunnel (deg, world).")] public float yaw;
        public float tunnelLength = 10f, tunnelWidth = 2.4f, tunnelHeight = 3f;
        [Tooltip("Side of the square room = the diggable floor.")] public float roomSize = 7f;
        [Tooltip("Diggable soil on the room floor (the grid adds a bedrock layer below).")] public float dirtDepth = 2f;
        [Tooltip("Clear height above the soil.")] public float headroom = 3.4f;
        [Range(0.1f, 0.5f)] public float cellSize = 0.25f;
        [Header("Finds in the room floor")]
        public List<SiteArtifactSpawn> artifacts = new List<SiteArtifactSpawn>();
        [Header("Look")]
        public Color wallColor = new Color(0.33f, 0.24f, 0.2f);
        public Color glyphColor = new Color(0.35f, 0.9f, 0.95f);
        [TextArea] public string openedMessage = "The seal gives way. A tunnel leads down into the dark.";

        /// <summary>Id of the room-floor dig site (saves, excavation state).</summary>
        public string FloorSiteId => Id + ".floor";
        /// <summary>ExcavationGrid depth that yields <see cref="dirtDepth"/> diggable metres (ForSite adds 0.75 m + a bedrock layer).</summary>
        public float GridDepth => Mathf.Max(0.25f, dirtDepth - 0.75f);
    }
}
