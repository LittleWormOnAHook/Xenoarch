using System;
using UnityEngine;

namespace DMI.Xenoarch.Economy
{
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
        [Tooltip("Selling an artifact to a buyer (small: museums pay in reputation too).")] public int artifactSold = 1;
    }
}
