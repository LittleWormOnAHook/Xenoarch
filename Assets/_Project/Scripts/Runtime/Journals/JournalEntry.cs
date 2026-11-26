using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Journals
{
    public enum JournalCategory { Basics, Digging, Scanning, Artifacts, Ship, Survival }

    /// <summary>Where the journal pickup is laid along the player's path (data; see Tools/Xenoarch/Journals/Place Journals).</summary>
    public enum JournalPlacement
    {
        None = 0,
        /// <summary>Aboard the ship, near the arrival spawn (spawned at runtime: the ship scene is not edited).</summary>
        Ship = 1,
        /// <summary>Beside the base landing zone of each surface scene (placed in the scene by the Editor menu).</summary>
        BaseLanding = 2,
        /// <summary>Just outside the fence of the dig site nearest the base zone (Editor menu).</summary>
        FirstDigSite = 3,
        /// <summary>Beside a site's landing zone (siteIndex = sites ordered by distance from base; Editor menu).</summary>
        SiteLanding = 4,
        /// <summary>A few metres from the cache nearest the base zone (caches are generated at runtime, so runtime spawn).</summary>
        NearCache = 5,
    }

    /// <summary>
    /// One in-world note (survey crew log) that teaches a mechanic. Lives in Resources/Journals; collected / read state is
    /// saved under "journals.collected". Pickups reference it by id.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Journal Entry", fileName = "Journal_")]
    public sealed class JournalEntry : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "journal.";
        public string title = "Untitled";
        [Tooltip("In-world author line, e.g. 'Survey Tech R. Okafor'.")] public string author = "";
        [Tooltip("In-world date / log line, e.g. 'Survey log 2187.04.12'.")] public string dateLine = "";
        public JournalCategory category = JournalCategory.Basics;
        [TextArea(6, 20)] public string body = "";
        [Header("Placement")]
        public JournalPlacement placement = JournalPlacement.BaseLanding;
        [Tooltip("Only on this body (body id, e.g. body.mars). Empty = on every body that has the placement.")] public string bodyId = "";
        [Tooltip("SiteLanding: which site (0 = nearest the base zone).")] public int siteIndex = 0;
        [Tooltip("Order in lists and among pickups at the same spot.")] public int order = 0;
    }
}
