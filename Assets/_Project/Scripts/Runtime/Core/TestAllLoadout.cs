using System.Collections.Generic;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Discovery;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using UnityEngine;

namespace DMI.Xenoarch.Core
{
    /// <summary>
    /// DEV / TESTING helper for the main-menu "New Test All" button. Applied once after
    /// <see cref="GameSession.StartNewGame"/>; it does not change normal New Game.
    /// Grants are data-driven from the item registry, craft recipes, galaxy and upgrade tables
    /// so newly added content is included automatically.
    /// </summary>
    public static class TestAllLoadout
    {
        public static void Apply(GameSession session)
        {
            if (session == null) return;

            // Upgrades first so crate capacity is already at max when the holds are filled.
            var upgradeIds = new System.Collections.Generic.List<string>
            {
                GameSession.UpgradeHold, GameSession.UpgradeHandheldScanner, GameSession.UpgradeOrbitalScanner, GameSession.UpgradeDepthProbe,
                GameSession.UpgradeEmergencyShuttle, GameSession.UpgradeEmergencyRover
            };
            if (session.Crafting != null)
                foreach (var r in session.Crafting.Recipes)
                    if (r != null && r.IsUpgrade && !upgradeIds.Contains(r.upgradeId))
                        upgradeIds.Add(r.upgradeId);
            foreach (var id in upgradeIds)
                while (session.ApplyUpgrade(id)) { }

            UnlockTravelAndRecipes(session);
            InstallAvailableTech(session);
            session.GrantStarterVehicles();

            FillPlayerInventory(session);
            FillCrates(session);

            Debug.Log("[Xenoarch] New Test All: unlocked travel/tech/recipes, maxed upgrades, granted tools/equipment and filled every stackable item in the backpack and crates to max (250).");
        }

        static void UnlockTravelAndRecipes(GameSession session)
        {
            var discovery = session.Discovery;
            var exploration = session.Exploration;
            if (discovery == null || exploration == null) return;

            if (exploration.Galaxy != null)
            {
                foreach (var sys in exploration.Galaxy.AllSystems)
                {
                    if (sys == null) continue;
                    if (!string.IsNullOrEmpty(sys.revealFact)) discovery.Publish(sys.revealFact);
                    discovery.Publish(Facts.NavSystem(sys.Id));
                    foreach (var b in sys.AllBodies)
                        if (b != null) exploration.Body(b.Id).discovered = true;
                }
            }

            foreach (var dest in exploration.NavDestinations())
                discovery.Publish(Facts.NavSystem(dest.id));

            discovery.Publish(ProceduralGalaxy.NavDriveFact);
            exploration.NotifyChanged();

            if (session.Crafting != null)
                foreach (var r in session.Crafting.Recipes)
                    if (r != null && !string.IsNullOrEmpty(r.unlockFact))
                        discovery.Publish(r.unlockFact);

            var unlocks = session.Economy != null && session.Economy.Settings != null ? session.Economy.Settings.rankUnlocks : null;
            if (unlocks != null)
            {
                foreach (var u in unlocks)
                {
                    if (u == null) continue;
                    if (!string.IsNullOrEmpty(u.setFact)) discovery.Publish(u.setFact);
                    if (!string.IsNullOrEmpty(u.unlockSystemId)) discovery.Publish(Facts.NavSystem(u.unlockSystemId));
                }
            }
        }

        static void InstallAvailableTech(GameSession session)
        {
            if (session.Artifacts == null) return;
            foreach (var def in AllItems(session.Items))
            {
                if (def is ArtifactDefinition a && a.installTarget != TechInstallTarget.None)
                    session.Artifacts.ForceInstallForTest(a);
            }
        }

        static void FillPlayerInventory(GameSession session)
        {
            var inv = session.Inventory;
            var tools = new List<ItemDefinition>();
            var resources = new List<ItemDefinition>();
            foreach (var def in AllItems(session.Items))
            {
                if (def.IsStackable) resources.Add(def);
                else if (def.category == ItemCategory.Tool || def.category == ItemCategory.Equipment)
                    tools.Add(def);
            }

            foreach (var d in tools)
            {
                if (inv.Count(d.Id) > 0) continue;
                if (inv.FreeSlots <= 0) inv.SetCapacity(inv.Capacity + 1);
                if (inv.Add(d.Id, 1) != 0)
                    Debug.LogWarning($"[Xenoarch] New Test All: could not add {d.Id} to the backpack.");
            }
            session.Hotbar.ShelveFrom(inv);

            int extra = 0;
            foreach (var d in resources)
                if (inv.Count(d.Id) <= 0) extra++;
                else
                {
                    // A full stack may still need a new slot if the existing one is already full.
                    int room = 0;
                    foreach (var s in inv.Slots)
                        if (!s.IsEmpty && s.itemId == d.Id) room += System.Math.Max(0, d.EffectiveMaxStack - s.count);
                    if (d.EffectiveMaxStack - inv.Count(d.Id) > room) extra++;
                }
            if (extra > inv.FreeSlots) inv.SetCapacity(inv.Capacity + extra - inv.FreeSlots);

            foreach (var d in resources)
            {
                int want = d.EffectiveMaxStack - inv.Count(d.Id);
                if (want > 0 && inv.Add(d.Id, want) != 0)
                    Debug.LogWarning($"[Xenoarch] New Test All: backpack could not take a full stack of {d.Id}.");
            }
        }

        static void FillCrates(GameSession session)
        {
            var resources = new List<ItemDefinition>();
            foreach (var def in AllItems(session.Items))
                if (def.IsStackable)
                    resources.Add(def);
            if (resources.Count == 0) return;

            foreach (var hold in session.Holds.Values)
            {
                if (hold.Capacity < resources.Count) hold.SetCapacity(resources.Count);
                foreach (var d in resources)
                {
                    int want = d.EffectiveMaxStack - hold.Count(d.Id);
                    if (want > 0) hold.Add(d.Id, want);
                }
                int guard = 0;
                while (hold.FreeSlots > 0 && guard++ < 10000)
                {
                    bool added = false;
                    foreach (var d in resources)
                    {
                        if (hold.FreeSlots <= 0) break;
                        int left = hold.Add(d.Id, d.EffectiveMaxStack);
                        if (left < d.EffectiveMaxStack) added = true;
                    }
                    if (!added) break;
                }
            }
        }

        static IEnumerable<ItemDefinition> AllItems(IItemLookup items)
        {
            if (items is ItemRegistry reg && reg.Entries != null)
                foreach (var e in reg.Entries)
                    if (e != null && !string.IsNullOrEmpty(e.Id)) yield return e;
        }
    }
}