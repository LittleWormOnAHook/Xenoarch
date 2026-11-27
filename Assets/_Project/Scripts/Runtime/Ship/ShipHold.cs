using System;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Ship
{
    /// <summary>What a Storage crate is for. Materials (resources) fit in every crate.</summary>
    public enum StorageRole { General = 0, Equipment = 1, Artifacts = 2 }

    /// <summary>
    /// Ship hold: one shared inventory behind every Storage crate aboard. Slots come from
    /// <see cref="ShipDefinition.holdSlotsByLevel"/> indexed by <c>ShipState.holdLevel</c>; <see cref="Upgrade"/> is the
    /// hook crafting / ship upgrades will call.
    /// </summary>
    public static class ShipHold
    {
        public const string Key = "ship.hold";

        /// <summary>Role from an object name: contains "Artifact" / "Equipment" / "General" (fallback General).</summary>
        public static StorageRole RoleFromName(string name)
        {
            var n = (name ?? "").ToLowerInvariant();
            if (n.Contains("artifact") || n.Contains("artefact")) return StorageRole.Artifacts;
            if (n.Contains("equipment")) return StorageRole.Equipment;
            return StorageRole.General;
        }

        /// <summary>Which items a crate of this role takes. Materials (Resource) go anywhere; General takes everything.</summary>
        public static bool Accepts(StorageRole role, Items.ItemCategory c)
        {
            if (role == StorageRole.General || c == Items.ItemCategory.Resource) return true;
            if (role == StorageRole.Equipment) return c == Items.ItemCategory.Tool || c == Items.ItemCategory.Equipment || c == Items.ItemCategory.Consumable || c == Items.ItemCategory.ShipModule || c == Items.ItemCategory.Misc;
            return c == Items.ItemCategory.Artifact || c == Items.ItemCategory.DataRecord || c == Items.ItemCategory.Valuable;
        }

        public static int SlotsFor(ShipDefinition ship, int level)
        {
            var t = ship != null ? ship.holdSlotsByLevel : null;
            if (t == null || t.Length == 0) return 24;
            return Mathf.Max(1, t[Mathf.Clamp(level, 0, t.Length - 1)]);
        }

        public static int Level => XenoarchRuntime.Session?.Exploration.Ship.holdLevel ?? 0;
        public static int MaxLevel { get { var t = XenoarchRuntime.Session?.Config.starterShip?.holdSlotsByLevel; return t != null ? Mathf.Max(0, t.Length - 1) : 0; } }

        /// <summary>Raise the hold one level (more slots). False at max level.</summary>
        public static bool Upgrade()
        {
            var s = XenoarchRuntime.Session;
            return s != null && s.ApplyUpgrade(GameSession.UpgradeHold);
        }

        /// <summary>Move a stack (or unique instance) between inventories. Returns how many moved.</summary>
        public static int Transfer(InventoryModel from, InventoryModel to, ItemStack stack)
        {
            if (stack == null || stack.IsEmpty || from == null || to == null) return 0;
            if (!string.IsNullOrEmpty(stack.instanceId))
            {
                string id = stack.itemId, inst = stack.instanceId;
                // Phase 2 M1: crate transfers go through the artifact eligibility gate (accessible moves are allowed).
                var session = XenoarchRuntime.Session;
                var art = session?.Artifacts.Get(inst);
                if (art != null)
                {
                    bool toCrate = session.Holds.ContainsValue(to);
                    var gate = session.Artifacts.Check(art, toCrate ? DMI.Xenoarch.Artifacts.ArtifactAction.MoveToShipStorage : DMI.Xenoarch.Artifacts.ArtifactAction.MoveToBackpack);
                    if (gate.IsBlocked) { DMI.Xenoarch.UI.Toast.Show(gate.Message, 5f); return 0; }
                }
                if (to.FreeSlots <= 0 || !from.RemoveInstance(inst)) return 0;
                if (!to.AddInstance(id, inst))
                {
                    // Failed transfer: put it back; if even that fails, recovery restores it (same instance id, no duplicate).
                    if (!from.AddInstance(id, inst)) { Debug.LogWarning($"[Xenoarch] Transfer of {inst} failed both ways; running recovery"); session?.Recovery.Ensure("transfer"); }
                    return 0;
                }
                return 1;
            }
            string item = stack.itemId; int n = stack.count;
            int left = to.Add(item, n);
            int moved = n - left;
            if (moved > 0) from.Remove(item, moved);
            return moved;
        }

        public sealed class Section : ISaveSection
        {
            readonly InventoryModel _hold; readonly Func<int> _slots; readonly StorageRole _role;
            public Section(StorageRole role, InventoryModel hold, Func<int> slots) { _role = role; _hold = hold; _slots = slots; }
            public string Key => $"{ShipHold.Key}.{_role.ToString().ToLowerInvariant()}";
            public int RestoreOrder => 30; // after the ship state (hold level)
            public string CaptureJson() => JsonUtility.ToJson(_hold.Capture());
            public void RestoreJson(string json) { _hold.SetCapacity(_slots()); _hold.Restore(JsonUtility.FromJson<InventorySaveData>(json)); }
            public void ResetToNewGame() { _hold.Clear(); _hold.SetCapacity(_slots()); }
        }
    }
}
