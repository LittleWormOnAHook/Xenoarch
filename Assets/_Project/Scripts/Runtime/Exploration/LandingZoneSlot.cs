using UnityEngine;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>
    /// One preplaced landing zone (built by Tools/Xenoarch/Landing/Build Landing Zones as a child of a
    /// <see cref="LandingZone"/> marker): the landed Mark2Shuttle and the player's "shuttle" arrival spawn. Hand-adjust
    /// both freely in the scene; nothing moves them at edit time again.
    /// </summary>
    public sealed class LandingZoneSlot : MonoBehaviour
    {
        [Tooltip("Dig site this zone serves (site.* id). Empty = base zone.")] public string siteId = "";
        [Tooltip("The preplaced shuttle (its board interaction returns to the ship).")] public GameObject shuttle;
        [Tooltip("The player's arrival spawn beside the shuttle.")] public Player.PlayerSpawnPoint spawn;
    }
}
