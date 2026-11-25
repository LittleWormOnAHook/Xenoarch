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
        [Tooltip("Environment for scenes not tied to a body (fallback).")] public DMI.Xenoarch.Galaxy.EnvironmentProfile defaultEnvironment;

        [Header("New game")]
        public string firstGameplayScene = SceneNames.ShipStarter;
        public string firstSpawnId = PlayerSpawnIds.Default;
        [Min(1)] public int inventoryCapacity = 24;
        public List<StartingItem> startingItems = new List<StartingItem>();

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
