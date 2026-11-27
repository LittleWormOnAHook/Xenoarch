using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Items;
using UnityEngine;

namespace DMI.Xenoarch.Inventory
{
    [Serializable]
    public sealed class InventorySaveData
    {
        public int capacity;
        public List<ItemStack> slots = new List<ItemStack>();
    }

    /// <summary>
    /// Slot inventory as plain C# (no MonoBehaviour, no UI). Adapted from the ASH ASHInventory / Genesis InventorySystem
    /// two-pass add (top up existing stacks, then empty slots) and remove-across-stacks logic; rewritten for item ids
    /// (save-friendly) and unique instances (artifacts) that can never be duplicated.
    /// </summary>
    public sealed class InventoryModel
    {
        private readonly IItemLookup _lookup;
        private readonly List<ItemStack> _slots;

        public int Capacity => _slots.Count;
        public IReadOnlyList<ItemStack> Slots => _slots;
        public event Action Changed;

        public InventoryModel(IItemLookup lookup, int capacity)
        {
            _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
            _slots = new List<ItemStack>(Mathf.Max(1, capacity));
            for (int i = 0; i < Mathf.Max(1, capacity); i++) _slots.Add(new ItemStack());
        }

        /// <summary>Grow (or shrink, dropping only trailing empty slots) to <paramref name="capacity"/> slots. Used by
        /// upgradable containers (ship hold). Never discards items.</summary>
        public void SetCapacity(int capacity)
        {
            capacity = Mathf.Max(1, capacity);
            while (_slots.Count < capacity) _slots.Add(new ItemStack());
            while (_slots.Count > capacity && _slots[_slots.Count - 1].IsEmpty) _slots.RemoveAt(_slots.Count - 1);
            Changed?.Invoke();
        }

        public int FreeSlots { get { int n = 0; foreach (var s in _slots) if (s.IsEmpty) n++; return n; } }

        public int Count(string itemId)
        {
            int n = 0;
            foreach (var s in _slots) if (!s.IsEmpty && s.itemId == itemId) n += s.count;
            return n;
        }

        public bool ContainsInstance(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return false;
            foreach (var s in _slots) if (!s.IsEmpty && s.instanceId == instanceId) return true;
            return false;
        }

        /// <summary>Adds up to <paramref name="amount"/>. Returns how many could NOT be added. Unique items get fresh instance ids.</summary>
        public int Add(string itemId, int amount)
        {
            if (amount <= 0) return 0;
            if (!_lookup.TryGetItem(itemId, out var def)) { Debug.LogWarning($"[Xenoarch] Inventory: unknown item '{itemId}'"); return amount; }

            int remaining = amount;
            if (def.uniqueInstance)
            {
                while (remaining > 0 && AddInstanceInternal(def, StableId.NewInstanceId())) remaining--;
                if (remaining != amount) Changed?.Invoke();
                return remaining;
            }

            int max = def.EffectiveMaxStack;
            // Pass 1: top up existing stacks.
            foreach (var s in _slots)
            {
                if (remaining <= 0) break;
                if (s.IsEmpty || s.itemId != itemId || s.count >= max) continue;
                int add = Mathf.Min(max - s.count, remaining);
                s.count += add; remaining -= add;
            }
            // Pass 2: empty slots.
            foreach (var s in _slots)
            {
                if (remaining <= 0) break;
                if (!s.IsEmpty) continue;
                int add = Mathf.Min(max, remaining);
                s.itemId = itemId; s.count = add; s.instanceId = null; remaining -= add;
            }
            if (remaining != amount) Changed?.Invoke();
            return remaining;
        }

        /// <summary>Adds one unique instance with a known id (recovered artifact). Refuses duplicates.</summary>
        public bool AddInstance(string itemId, string instanceId)
        {
            if (!_lookup.TryGetItem(itemId, out var def)) return false;
            if (!def.uniqueInstance) { Debug.LogWarning($"[Xenoarch] Inventory: '{itemId}' is not a unique-instance item"); return false; }
            bool ok = AddInstanceInternal(def, instanceId);
            if (ok) Changed?.Invoke();
            return ok;
        }

        private bool AddInstanceInternal(ItemDefinition def, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || ContainsInstance(instanceId)) return false;
            foreach (var s in _slots)
            {
                if (!s.IsEmpty) continue;
                s.itemId = def.Id; s.count = 1; s.instanceId = instanceId;
                return true;
            }
            return false;
        }

        /// <summary>Phase 2 M1: optional guard for bulk removal by item id (crafting, refining, stack sales). Returns false for
        /// a unique instance that must not be consumed (protected artifacts); those slots are skipped. Null = no guard.</summary>
        public Func<string, bool> CanBulkRemoveInstance;

        /// <summary>Removes up to <paramref name="amount"/> (from the last stacks first). Returns how many were removed.</summary>
        public int Remove(string itemId, int amount)
        {
            if (amount <= 0) return 0;
            int removed = 0;
            for (int i = _slots.Count - 1; i >= 0 && removed < amount; i--)
            {
                var s = _slots[i];
                if (s.IsEmpty || s.itemId != itemId) continue;
                if (!string.IsNullOrEmpty(s.instanceId) && CanBulkRemoveInstance != null && !CanBulkRemoveInstance(s.instanceId)) continue;
                int take = Mathf.Min(s.count, amount - removed);
                s.count -= take; removed += take;
                if (s.count <= 0) s.Clear();
            }
            if (removed > 0) Changed?.Invoke();
            return removed;
        }

        public bool RemoveInstance(string instanceId)
        {
            foreach (var s in _slots)
            {
                if (s.IsEmpty || s.instanceId != instanceId) continue;
                s.Clear();
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>Swap two backpack slots (drag and drop inside the inventory).</summary>
        public void Swap(int a, int b)
        {
            if (a == b || a < 0 || b < 0 || a >= _slots.Count || b >= _slots.Count) return;
            (_slots[a], _slots[b]) = (_slots[b], _slots[a]);
            Changed?.Invoke();
        }

        public void Clear()
        {
            foreach (var s in _slots) s.Clear();
            Changed?.Invoke();
        }

        public InventorySaveData Capture()
        {
            var data = new InventorySaveData { capacity = _slots.Count };
            foreach (var s in _slots) data.slots.Add(s.Clone());
            return data;
        }

        /// <summary>Restores slots. Unknown item ids and duplicate instance ids are dropped with a warning (never duplicated).</summary>
        public void Restore(InventorySaveData data)
        {
            foreach (var s in _slots) s.Clear();
            if (data?.slots == null) { Changed?.Invoke(); return; }
            var seen = new HashSet<string>();
            int n = Mathf.Min(_slots.Count, data.slots.Count);
            for (int i = 0; i < n; i++)
            {
                var src = data.slots[i];
                if (src == null || src.IsEmpty) continue;
                if (!_lookup.TryGetItem(src.itemId, out var def)) { Debug.LogWarning($"[Xenoarch] Inventory restore: unknown item '{src.itemId}' dropped"); continue; }
                if (def.uniqueInstance)
                {
                    if (string.IsNullOrEmpty(src.instanceId) || !seen.Add(src.instanceId)) { Debug.LogWarning($"[Xenoarch] Inventory restore: duplicate/missing instance of '{src.itemId}' dropped"); continue; }
                    _slots[i].itemId = src.itemId; _slots[i].count = 1; _slots[i].instanceId = src.instanceId;
                }
                else
                {
                    _slots[i].itemId = src.itemId; _slots[i].count = Mathf.Clamp(src.count, 1, def.EffectiveMaxStack); _slots[i].instanceId = null;
                }
            }
            if (data.slots.Count > _slots.Count) Debug.LogWarning($"[Xenoarch] Inventory restore: {data.slots.Count - _slots.Count} slot(s) beyond capacity ignored");
            Changed?.Invoke();
        }
    }
}
