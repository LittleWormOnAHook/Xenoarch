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
        /// <summary>Ship storage, one inventory per crate role (Artifacts / Equipment / General). Slots grow with
        /// <c>ShipState.holdLevel</c>. Crates with the same role share an inventory.</summary>
        public readonly System.Collections.Generic.Dictionary<DMI.Xenoarch.Ship.StorageRole, InventoryModel> Holds = new System.Collections.Generic.Dictionary<DMI.Xenoarch.Ship.StorageRole, InventoryModel>();
        public InventoryModel Hold(DMI.Xenoarch.Ship.StorageRole role) => Holds[role];
        public WorldPersistence World { get; }
        public SaveService Saves { get; }
        public ExplorationService Exploration { get; }
        public DMI.Xenoarch.Discovery.DiscoveryService Discovery { get; }
        /// <summary>Random valuable caches on planets (handheld-scanner finds, not part of the discovery chain).</summary>
        public DMI.Xenoarch.Valuables.ValuableService Valuables { get; }
        public DMI.Xenoarch.Journals.JournalService Journals { get; }
        public DMI.Xenoarch.Artifacts.ArtifactService Artifacts { get; }
        /// <summary>Fragment puzzles (e.g. the Vael star map that reveals Proxima Centauri).</summary>
        public DMI.Xenoarch.Artifacts.ArtifactPuzzleService Puzzles { get; }
        public DMI.Xenoarch.Excavation.ExcavationService Excavation { get; }
        /// <summary>Dig site maps: fenced site codes (Ma1), refined-scan spots (Ma11) and their dig state.</summary>
        public DMI.Xenoarch.Excavation.DigSiteMapsService DigMaps { get; }
        public DMI.Xenoarch.Materials.MaterialsService Materials { get; }
        /// <summary>Backpack + the three ship crates as one stock (refiner and crafting take from it, backpack first).</summary>
        public ItemPool Stock { get; }
        public DMI.Xenoarch.Crafting.CraftingService Crafting { get; }
        public bool AboardShip => Location != null && Location.sceneName == SceneNames.ShipStarter;
        public PlayerLocationState Location { get; internal set; } = new PlayerLocationState();
        public float PlaySeconds { get; set; }

        /// <summary>Hotbar assignment (keys 1-8), saved; missing in old saves = default layout.</summary>
        public Hotbar Hotbar { get; }
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
            Hotbar = new Hotbar(() => Config.surfaceScanner != null ? Config.surfaceScanner.itemId : "item.tool.handheld_scanner",
                () => { var l = new System.Collections.Generic.List<string>(); foreach (var t in DMI.Xenoarch.Excavation.DigTools.All) l.Add(t.itemId); l.Add(DMI.Xenoarch.Excavation.DepthProbeDefinition.Load().itemId); return l; });
            Saves.Register(new JsonStateSection<HotbarData>(Hotbar.Key, 0, () => Hotbar.Data, v => Hotbar.Set(v)));
            Saves.Register(World);
            Exploration = new ExplorationService(config.galaxy, config.starterShip, config.siteRegistry, Saves);
            Exploration.Clock = () => PlaySeconds;
            Discovery = new DMI.Xenoarch.Discovery.DiscoveryService(config.discoveryChains, Exploration, Saves) { Clock = () => PlaySeconds };
            Artifacts = new DMI.Xenoarch.Artifacts.ArtifactService(items, config.civilizations, Discovery, Saves) { Clock = () => PlaySeconds, TranslationCap = config.labTranslationCap };
            Puzzles = new DMI.Xenoarch.Artifacts.ArtifactPuzzleService(Artifacts, Discovery, Saves) { Clock = () => PlaySeconds };
            Excavation = new DMI.Xenoarch.Excavation.ExcavationService(Saves);
            DigMaps = new DMI.Xenoarch.Excavation.DigSiteMapsService(Saves);
            Materials = new DMI.Xenoarch.Materials.MaterialsService(items, Inventory, config.refineRecipes, Saves);
            Valuables = new DMI.Xenoarch.Valuables.ValuableService(Saves);
            Journals = new DMI.Xenoarch.Journals.JournalService(Saves) { Clock = () => PlaySeconds };
            foreach (DMI.Xenoarch.Ship.StorageRole role in System.Enum.GetValues(typeof(DMI.Xenoarch.Ship.StorageRole)))
            {
                var hold = new InventoryModel(items, DMI.Xenoarch.Ship.ShipHold.SlotsFor(config.starterShip, 0));
                Holds[role] = hold;
                Saves.Register(new DMI.Xenoarch.Ship.ShipHold.Section(role, hold, () => DMI.Xenoarch.Ship.ShipHold.SlotsFor(config.starterShip, Exploration.Ship.holdLevel)));
            }
            Stock = new ItemPool(items, Inventory, HoldOrder);
            Puzzles.Owns = id => Inventory.ContainsInstance(id) || System.Linq.Enumerable.Any(Holds.Values, h => h.ContainsInstance(id));
            Materials.Pool = Stock;
            Crafting = new DMI.Xenoarch.Crafting.CraftingService(items, Stock, config.craftRecipes)
            {
                HasFact = k => Discovery.Has(k),
                AboardShip = () => AboardShip,
                UpgradeLevel = UpgradeLevel,
                UpgradeMax = UpgradeMax,
                ApplyUpgrade = ApplyUpgrade,
            };
            Artifacts.HasReagents = n => Materials.ReagentUnits() >= n;
            Artifacts.PayReagents = n => Materials.PayReagents(n);
            Exploration.Gate = key => Discovery.Has(key);
            Exploration.SiteRevealed += (id, level) => Discovery.Publish(DMI.Xenoarch.Discovery.Facts.Site(id), level);

            // Phase 1D: credits + notoriety, and the deeds that earn notoriety (each one-off, keyed).
            Economy = new DMI.Xenoarch.Economy.EconomyService(config.economy, Saves) { Clock = () => PlaySeconds };
            var eco = config.economy ?? new DMI.Xenoarch.Economy.EconomySettings();
            Artifacts.Recovered += (a, d) =>
            {
                var r = eco.recoverByRarity; int i = (int)d.rarity;
                Economy.Award(r != null && r.Length > 0 ? r[Math.Min(i, r.Length - 1)] : 0, "recover:" + a.instanceId, "recovered " + d.DisplayNameOrId);
            };
            Artifacts.ResearchCompleted += (a, op, msg) =>
            {
                if (a == null) return;
                if (op == DMI.Xenoarch.Artifacts.ResearchOp.CivilizationIdentification) Economy.Award(eco.civilizationIdentified, "civ:" + a.instanceId, "civilization identified");
                else if (op == DMI.Xenoarch.Artifacts.ResearchOp.Translation) Economy.Award(eco.translationPass, null, "translation");
                else Economy.Award(eco.researchOp, "op:" + op + ":" + a.instanceId, op.ToString());
            };
            Exploration.SiteRevealed += (id, level) => { if (level >= 3) Economy.Award(eco.sitePinpointed, "site:" + id, "site pinpointed " + id); };
            Valuables.Collected += id => Economy.Award(eco.cacheCollected, "cache:" + id, "cache " + id);
        }

        public DMI.Xenoarch.Economy.EconomyService Economy { get; private set; }

        System.Collections.Generic.IEnumerable<(InventoryModel, Func<ItemCategory, bool>)> HoldOrder()
        {
            foreach (var role in new[] { DMI.Xenoarch.Ship.StorageRole.General, DMI.Xenoarch.Ship.StorageRole.Equipment, DMI.Xenoarch.Ship.StorageRole.Artifacts })
            {
                var r = role;
                if (Holds.TryGetValue(r, out var h)) yield return (h, c => DMI.Xenoarch.Ship.ShipHold.Accepts(r, c));
            }
        }

        public const string UpgradeHold = "ship.hold", UpgradeHandheldScanner = "scanner.handheld", UpgradeOrbitalScanner = "scanner.orbital",
            UpgradeDepthProbe = DMI.Xenoarch.Excavation.DepthProbeDefinition.UpgradeId;

        public int UpgradeLevel(string id) => id switch
        {
            UpgradeHold => Exploration.Ship.holdLevel,
            UpgradeHandheldScanner => Exploration.Ship.handheldScannerLevel,
            UpgradeOrbitalScanner => Exploration.Ship.orbitalRangeLevel,
            UpgradeDepthProbe => Exploration.Ship.depthProbeLevel,
            _ => 0,
        };

        static int Top(System.Array a) => a != null ? Math.Max(0, a.Length - 1) : 0;

        public int UpgradeMax(string id) => id switch
        {
            UpgradeHold => Top(Config.starterShip?.holdSlotsByLevel),
            UpgradeHandheldScanner => Top(Config.surfaceScanner?.rangeByLevel),
            UpgradeOrbitalScanner => Top(Config.starterShip?.orbitalScanRadiusByLevel),
            UpgradeDepthProbe => DMI.Xenoarch.Excavation.DepthProbeDefinition.Load().MaxLevel,
            _ => 0,
        };

        /// <summary>Raises an upgrade one level (crafting / future trading). False at max or unknown.</summary>
        public bool ApplyUpgrade(string id)
        {
            if (UpgradeLevel(id) >= UpgradeMax(id)) return false;
            var s = Exploration.Ship;
            switch (id)
            {
                case UpgradeHold:
                    s.holdLevel++;
                    foreach (var h in Holds.Values) h.SetCapacity(DMI.Xenoarch.Ship.ShipHold.SlotsFor(Config.starterShip, s.holdLevel));
                    return true;
                case UpgradeHandheldScanner: s.handheldScannerLevel++; return true;
                case UpgradeOrbitalScanner: s.orbitalRangeLevel++; return true;
                case UpgradeDepthProbe: s.depthProbeLevel++; return true;
            }
            return false;
        }

        /// <summary>Resets every section and grants the starting kit. Does not load a scene.</summary>
        public void StartNewGame()
        {
            Saves.ResetAllToNewGame();
            PlaySeconds = 0f;
            foreach (var s in Config.startingItems)
                if (s != null && s.item != null) Inventory.Add(s.item.Id, s.count);
            Location = new PlayerLocationState { sceneName = Config.firstGameplayScene, spawnId = Config.firstSpawnId };
            ApplyDevLoadout();
        }

        /// <summary>DEV / TESTING (XenoarchConfig.devTestingLoadout): makes sure every dig tool and the handheld scanner are
        /// in the inventory. Only adds missing ones; turning the flag off stops it (items already granted stay).</summary>
        public void ApplyDevLoadout()
        {
            if (Config == null || !Config.devTestingLoadout) return;
            var granted = new System.Text.StringBuilder();
            void Give(string id)
            {
                if (string.IsNullOrEmpty(id) || Inventory.Count(id) > 0) return;
                if (Inventory.Add(id, 1) == 0) granted.Append(granted.Length > 0 ? ", " : "").Append(id);
            }
            foreach (var t in DMI.Xenoarch.Excavation.DigTools.All) Give(t.itemId);
            Give(Config.surfaceScanner != null ? Config.surfaceScanner.itemId : "item.tool.handheld_scanner");
            Give(DMI.Xenoarch.Excavation.DepthProbeDefinition.Load().itemId);
            if (granted.Length > 0) Debug.Log($"[Xenoarch] DEV testing loadout (XenoarchConfig.devTestingLoadout): granted {granted}");
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
