using System;
using System.Collections.Generic;
using DMI.Xenoarch.Items;
using UnityEngine;

namespace DMI.Xenoarch.Core
{
    [Serializable]
    public sealed class StartingItem
    {
        public ItemDefinition item;
        [Min(1)] public int count = 1;
    }

    /// <summary>Global project configuration. Lives at Resources/XenoarchConfig so the runtime can start from any scene.</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Core/Xenoarch Config", fileName = "XenoarchConfig")]
    public sealed class XenoarchConfig : ScriptableObject
    {
        public const string ResourcePath = "XenoarchConfig";

        [Header("Content")]
        public ItemRegistry itemRegistry;
        public DMI.Xenoarch.Galaxy.GalaxyDefinition galaxy;
        public DMI.Xenoarch.Galaxy.ShipDefinition starterShip;
        public DMI.Xenoarch.Galaxy.SiteRegistry siteRegistry;
        public DMI.Xenoarch.Galaxy.SurfaceScannerDefinition surfaceScanner;
        [Tooltip("Loot tiers for random valuable caches (common .. super rare).")] public DMI.Xenoarch.Valuables.LootTableDefinition valuableLoot;
        public DMI.Xenoarch.Valuables.ValuableSiteSettings valuableSites = DMI.Xenoarch.Valuables.ValuableSiteSettings.Default;
        [Header("Artifacts and discovery (Phase 1C)")]
        public List<DMI.Xenoarch.Artifacts.CivilizationDefinition> civilizations = new List<DMI.Xenoarch.Artifacts.CivilizationDefinition>();
        public List<DMI.Xenoarch.Discovery.DiscoveryChainDefinition> discoveryChains = new List<DMI.Xenoarch.Discovery.DiscoveryChainDefinition>();
        [Range(0.1f, 1f)] [Tooltip("Max translation the Mk I ship lab reaches (better translators raise it later).")] public float labTranslationCap = 0.6f;
        [Tooltip("Ship refiner recipes (Materials/RefineRecipe).")] public List<DMI.Xenoarch.Materials.RefineRecipe> refineRecipes = new List<DMI.Xenoarch.Materials.RefineRecipe>();
        [Tooltip("Layered ground per body: depth bands of raw materials that digging yields (Materials/StrataProfile).")] public List<DMI.Xenoarch.Materials.StrataProfile> strata = new List<DMI.Xenoarch.Materials.StrataProfile>();
        [Tooltip("Crafting recipes (Crafting/CraftRecipe).")] public List<DMI.Xenoarch.Crafting.CraftRecipe> craftRecipes = new List<DMI.Xenoarch.Crafting.CraftRecipe>();
        [Tooltip("Environment for scenes not tied to a body (fallback).")] public DMI.Xenoarch.Galaxy.EnvironmentProfile defaultEnvironment;
        [Tooltip("Deep-space sky used aboard the ship when the current body has no orbit skybox.")] public Material defaultSpaceSkybox;
        [Tooltip("Aboard the ship, bodies with an Orbit View show the real planet sphere outside the window under the Milky Way sky instead of their painted orbit skybox (off = the kit's orbit skyboxes as before).")]
        public bool preferOrbitViews = true;
        [Tooltip("Aboard the ship: multiplier on the scene's authored ambient light (darker space; 1 = as authored).")]
        [Range(0.2f, 1.5f)] public float shipAmbientScale = 0.7f;

        [Header("New game")]
        public string firstGameplayScene = SceneNames.ShipStarter;
        public string firstSpawnId = PlayerSpawnIds.Default;
        [Min(1)] public int inventoryCapacity = 24;
        public List<StartingItem> startingItems = new List<StartingItem>();
        [Tooltip("DEV / TESTING ONLY: every dig tool (shovel, precision tool, brush, powered drill, ...) and the handheld " +
                 "scanner are put in the inventory on New Game, Continue and Load if missing. Untick for release builds.")]
        public bool devTestingLoadout = true;

        [Header("Scenes")]
        public string mainMenuScene = SceneNames.MainMenu;
        [Range(0f, 2f)] public float fadeDuration = 0.35f;

        [Header("Saves")]
        public string saveFolderName = "Saves";
        public string quickSaveSlot = "quick";

        [Header("Development")]
        [Tooltip("F5 quick save, F9 quick load in gameplay scenes.")]
        public bool enableDebugHotkeys = true;
    }

    public static class SceneNames
    {
        public const string Bootstrap = "Bootstrap";
        public const string MainMenu = "MainMenu";
        public const string ShipStarter = "Ship_Starter";
        public const string TradeHubSol = "TradeHub_Sol";
        public const string MoonExcavation01 = "Moon_Excavation_01";
        public const string MarsExcavation01 = "Mars_Excavation_01";

        public static readonly string[] All = { Bootstrap, MainMenu, ShipStarter, TradeHubSol, MoonExcavation01, MarsExcavation01 };
    }

    public static class PlayerSpawnIds
    {
        public const string Default = "default";
    }
}
