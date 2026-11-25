using System;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Save;
using DMI.Xenoarch.World;
using UnityEngine;

namespace DMI.Xenoarch.Core
{
    /// <summary>Where the player is. Saved as section "player.location".</summary>
    [Serializable]
    public sealed class PlayerLocationState
    {
        public string sceneName;
        public string spawnId;
        public bool hasPose;
        public Vector3 position;
        public float yaw;
    }

    /// <summary>
    /// All runtime game state for one playthrough plus the services that operate on it. Plain C# (composition root
    /// is <see cref="XenoarchRuntime"/>), so systems receive what they need instead of reaching into singletons.
    /// Static definitions (ScriptableObjects) are referenced; player/world state is separate and serialisable.
    /// </summary>
    public sealed class GameSession
    {
        public XenoarchConfig Config { get; }
        public IItemLookup Items { get; }
        public InventoryModel Inventory { get; }
        public WorldPersistence World { get; }
        public SaveService Saves { get; }
        public ExplorationService Exploration { get; }
        public PlayerLocationState Location { get; internal set; } = new PlayerLocationState();
        public float PlaySeconds { get; set; }

        public const string LocationKey = "player.location";
        public const string InventoryKey = "player.inventory";
        public const string WorldKey = "world.persistence";

        public GameSession(XenoarchConfig config, IItemLookup items, ISaveStorage storage)
        {
            Config = config != null ? config : throw new ArgumentNullException(nameof(config));
            Items = items ?? throw new ArgumentNullException(nameof(items));
            Inventory = new InventoryModel(items, config.inventoryCapacity);
            World = new WorldPersistence(WorldKey, 10);
            Saves = new SaveService(storage);

            Saves.Register(new JsonStateSection<PlayerLocationState>(LocationKey, 0, () => Location, v => Location = v));
            Saves.Register(new InventorySection(InventoryKey, 0, Inventory));
            Saves.Register(World);
            Exploration = new ExplorationService(config.galaxy, config.starterShip, config.siteRegistry, Saves);
        }

        /// <summary>Resets every section and grants the starting kit. Does not load a scene.</summary>
        public void StartNewGame()
        {
            Saves.ResetAllToNewGame();
            PlaySeconds = 0f;
            foreach (var s in Config.startingItems)
                if (s != null && s.item != null) Inventory.Add(s.item.Id, s.count);
            Location = new PlayerLocationState { sceneName = Config.firstGameplayScene, spawnId = Config.firstSpawnId };
        }

        sealed class InventorySection : ISaveSection
        {
            readonly InventoryModel _model;
            public string Key { get; }
            public int RestoreOrder { get; }
            public InventorySection(string key, int order, InventoryModel model) { Key = key; RestoreOrder = order; _model = model; }
            public string CaptureJson() => JsonUtility.ToJson(_model.Capture());
            public void RestoreJson(string json) => _model.Restore(JsonUtility.FromJson<InventorySaveData>(json));
            public void ResetToNewGame() => _model.Clear();
        }
    }
}
