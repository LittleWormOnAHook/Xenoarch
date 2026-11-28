using System;
using System.Collections.Generic;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Exploration;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Save;
using DMI.Xenoarch.Ship;
using DMI.Xenoarch.UI;
using DMI.Xenoarch.Vehicles;
using UnityEngine;

namespace DMI.Xenoarch.Inventory
{
    /// <summary>
    /// Remote ship send plus shuttle and rover stashes (t302u / t303u). Ship crates can receive items from the surface.
    /// Shuttle (16 slots) and rover (8 slots) are usable when the player is nearby. Saved under their own keys so older
    /// saves just start with empty vehicle holds.
    /// </summary>
    public sealed class VehicleCargo
    {
        public const int ShuttleSlots = 16;
        public const int RoverSlots = 8;
        public const float NearbyRange = 12f;
        public const string ShuttleKey = "shuttle.hold";
        public const string RoverKey = "rover.hold";

        public InventoryModel Shuttle { get; }
        public InventoryModel Rover { get; }

        static VehicleCargo _current;
        public static VehicleCargo Current => _current ?? XenoarchRuntime.Session?.VehicleCargo;

        public VehicleCargo(IItemLookup items)
        {
            Shuttle = new InventoryModel(items, ShuttleSlots);
            Rover = new InventoryModel(items, RoverSlots);
        }

        public void Bind(GameSession session)
        {
            _current = this;
            session.Saves.Register(new Section(ShuttleKey, 31, Shuttle, ShuttleSlots));
            session.Saves.Register(new Section(RoverKey, 31, Rover, RoverSlots));
            var guard = session.Inventory.CanBulkRemoveInstance;
            if (guard != null)
            {
                Shuttle.CanBulkRemoveInstance = guard;
                Rover.CanBulkRemoveInstance = guard;
            }
        }

        public static bool OwnsInstance(GameSession s, string instanceId)
        {
            if (s?.VehicleCargo == null || string.IsNullOrEmpty(instanceId)) return false;
            return s.VehicleCargo.Shuttle.ContainsInstance(instanceId) || s.VehicleCargo.Rover.ContainsInstance(instanceId);
        }

        public static bool ShuttleNearby() => Near(ShuttleTransform());
        public static bool RoverNearby() => Near(RoverTransform());

        public static Transform ShuttleTransform()
        {
            var sh = ShuttleDirector.Instance?.Shuttle;
            return sh != null && sh.activeInHierarchy ? sh.transform : null;
        }

        public static Transform RoverTransform()
        {
            var r = RoverDirector.Instance?.Current;
            return r != null && r.isActiveAndEnabled ? r.transform : null;
        }

        static bool Near(Transform t)
        {
            if (t == null) return false;
            var rig = PlayerRigLocator.Find();
            if (rig == null || rig.Root == null) return false;
            return Vector3.Distance(rig.Root.position, t.position) <= NearbyRange;
        }

        /// <summary>Ship first (always, remote), then nearby shuttle, then nearby rover, each only if it can take the stack.</summary>
        public static List<CargoDest> Destinations(ItemStack stack, InventoryModel alreadyIn = null)
        {
            var list = new List<CargoDest>();
            var s = XenoarchRuntime.Session;
            if (s == null || stack == null || stack.IsEmpty) return list;
            s.Items.TryGetItem(stack.itemId, out var def);

            InventoryModel ship = BestShipHold(s, def, stack);
            if (ship != null && ship != alreadyIn)
                list.Add(new CargoDest { Id = "ship", Label = "Send to ship", Inventory = ship });

            var cargo = s.VehicleCargo;
            if (cargo != null)
            {
                if (ShuttleNearby() && cargo.Shuttle != alreadyIn)
                    list.Add(new CargoDest { Id = "shuttle", Label = "Stash in shuttle", Inventory = cargo.Shuttle });
                if (RoverNearby() && cargo.Rover != alreadyIn)
                    list.Add(new CargoDest { Id = "rover", Label = "Stash in rover", Inventory = cargo.Rover });
            }
            return list;
        }

        public static InventoryModel BestShipHold(GameSession s, ItemDefinition def, ItemStack stack)
        {
            if (s == null) return null;
            foreach (var role in new[] { StorageRole.General, StorageRole.Equipment, StorageRole.Artifacts })
            {
                if (!s.Holds.TryGetValue(role, out var h) || h == null) continue;
                if (def != null && !ShipHold.Accepts(role, def.category)) continue;
                if (!string.IsNullOrEmpty(stack?.instanceId))
                {
                    if (h.FreeSlots > 0) return h;
                }
                else if (h.FreeSlots > 0 || (stack != null && CanMerge(h, stack, def)))
                    return h;
            }
            return null;
        }

        static bool CanMerge(InventoryModel inv, ItemStack stack, ItemDefinition def)
        {
            if (inv == null || stack == null || def == null || !def.IsStackable) return false;
            int max = def.EffectiveMaxStack;
            foreach (var o in inv.Slots)
                if (!o.IsEmpty && o.itemId == stack.itemId && o.SameMeta(stack) && o.count < max) return true;
            return false;
        }

        /// <summary>Move as much of the stack as will fit. Returns how many moved. Uses artifact-accessible move gates.</summary>
        public static int Send(InventoryModel from, InventoryModel to, ItemStack stack, bool fromWorld = false)
        {
            if (stack == null || stack.IsEmpty || to == null) return 0;
            var session = XenoarchRuntime.Session;
            if (!string.IsNullOrEmpty(stack.instanceId))
            {
                var art = session?.Artifacts.Get(stack.instanceId);
                if (art != null)
                {
                    bool toShip = session.Holds != null && session.Holds.ContainsValue(to);
                    var action = toShip ? ArtifactAction.MoveToShipStorage : ArtifactAction.MoveToRoverCargo;
                    var gate = session.Artifacts.Check(art, action);
                    if (gate.IsBlocked) { Toast.Show(gate.Message, 5f); return 0; }
                }
                if (to.FreeSlots <= 0) return 0;
                if (!fromWorld)
                {
                    if (from == null || !from.RemoveInstance(stack.instanceId)) return 0;
                    if (!to.AddInstance(stack.itemId, stack.instanceId))
                    {
                        if (!from.AddInstance(stack.itemId, stack.instanceId))
                            session?.Recovery.Ensure("vehicle-cargo");
                        return 0;
                    }
                }
                else if (!to.AddInstance(stack.itemId, stack.instanceId)) return 0;
                return 1;
            }

            int n = stack.count;
            int left = to.Add(stack.itemId, n, stack.originBodyId, stack.originSiteId, stack.grade);
            int moved = n - left;
            if (moved > 0 && !fromWorld && from != null) from.Remove(stack.itemId, moved);
            return moved;
        }

        public static int SendToShip(InventoryModel from, ItemStack stack, bool fromWorld = false)
        {
            var s = XenoarchRuntime.Session;
            s.Items.TryGetItem(stack.itemId, out var def);
            var dest = BestShipHold(s, def, stack);
            if (dest == null) return 0;
            return Send(from, dest, stack, fromWorld);
        }

        public sealed class CargoDest
        {
            public string Id;
            public string Label;
            public InventoryModel Inventory;
        }

        sealed class Section : ISaveSection
        {
            readonly InventoryModel _hold; readonly int _slots;
            public string Key { get; }
            public int RestoreOrder { get; }
            public Section(string key, int order, InventoryModel hold, int slots)
            { Key = key; RestoreOrder = order; _hold = hold; _slots = slots; }
            public string CaptureJson() => JsonUtility.ToJson(_hold.Capture());
            public void RestoreJson(string json)
            {
                _hold.SetCapacity(_slots);
                if (!string.IsNullOrEmpty(json))
                    _hold.Restore(JsonUtility.FromJson<InventorySaveData>(json));
            }
            public void ResetToNewGame() { _hold.Clear(); _hold.SetCapacity(_slots); }
        }
    }
}
