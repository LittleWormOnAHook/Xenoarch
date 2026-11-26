using System;
using UnityEngine;

namespace DMI.Xenoarch.Economy
{
    /// <summary>What reaching a notoriety rank unlocks (applied once the rank is reached, also for loaded saves).</summary>
    [Serializable]
    public sealed class RankUnlock
    {
        [Tooltip("Rank index (0 = Unknown Explorer).")] public int rank = 1;
        [Tooltip("Discovery fact set at this rank (chains / gates can use it). Every rank also publishes notoriety.rank = rank.")] public string setFact;
        [Tooltip("Optional star system unlocked in the Nav dropdown, e.g. \"sys.barnards_star\" (design decision pending).")] public string unlockSystemId;
        [Tooltip("Shown with the rank-up message.")] public string message;
    }

    /// <summary>Phase 1D tunables: credits, notoriety ranks and awards (config.economy). Defaults are placeholders to balance later.</summary>
    [Serializable]
    public sealed class EconomySettings
    {
        [Tooltip("Credits at the start of a new game (and for saves from before Phase 1D).")] public int startingCredits = 500;

        [Header("Notoriety")]
        [Tooltip("Score needed for each rank (index = rank). First entry must be 0.")] public int[] rankThresholds = { 0, 25, 75, 200, 450, 1000 };
        public string[] rankNames = { "Unknown Explorer", "Independent Excavator", "Recognized Archaeologist", "Renowned Explorer", "Galactic Authority", "Legendary Discoverer" };
        [Tooltip("Recovering an artifact: points by rarity (Common, Uncommon, Rare, Exceptional, Legendary, Unique).")] public int[] recoverByRarity = { 2, 4, 8, 15, 25, 40 };
        [Tooltip("Pinpointing an archaeological site.")] public int sitePinpointed = 5;
        [Tooltip("Identifying a civilization.")] public int civilizationIdentified = 15;
        [Tooltip("Each completed translation pass.")] public int translationPass = 3;
        [Tooltip("Any other completed research operation.")] public int researchOp = 1;
        [Tooltip("Digging up a valuable cache.")] public int cacheCollected = 2;
        [Tooltip("Installing a functional alien device.")] public int techInstalled = 20;
        [Tooltip("Opening a buried chamber.")] public int chamberOpened = 10;
        [Tooltip("Rank rewards. Defaults only announce the vendor rank gates; no star system is unlocked by rank until that is decided.")]
        public RankUnlock[] rankUnlocks =
        {
            new RankUnlock { rank = 1, setFact = "notoriety.unlock.supplier_tier1", message = "Equipment Supplier: circuits, precision tool, depth probe and scanner upgrades now offered." },
            new RankUnlock { rank = 2, setFact = "notoriety.unlock.supplier_tier2", message = "Equipment Supplier: sensor arrays, powered drill and depth probe upgrades now offered." },
            new RankUnlock { rank = 3, setFact = "notoriety.unlock.renowned", message = "Buyers pay more for your finds (rank bonus)." },
        };
        [Tooltip("Selling an artifact to a buyer (small: museums pay in reputation too).")] public int artifactSold = 1;
    }
}
