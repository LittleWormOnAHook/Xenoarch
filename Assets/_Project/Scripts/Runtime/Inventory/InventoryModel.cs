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

        /// <summary>Optional extra stacks that count as owned (hotbar). Ignored by Capacity, FreeSlots and Add.</summary>
        public Func<IEnumerable<ItemStack>> ExtraStacks;
        /// <summary>Removes up to amount from the extra store; returns how many were removed.</summary>
        public Func<string, int, int> ExtraRemove;
        public Func<string, bool> ExtraRemoveInstance;

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
            if (ExtraStacks != null) foreach (var s in ExtraStacks()) if (s != null && !s.IsEmpty && s.itemId == itemId) n += s.count;
            return n;
        }

        public bool ContainsInstance(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return false;
            foreach (var s in _slots) if (!s.IsEmpty && s.instanceId == instanceId) return true;
            if (ExtraStacks != null) foreach (var s in ExtraStacks()) if (s != null && !s.IsEmpty && s.instanceId == instanceId) return true;
            return false;
        }

        /// <summary>Adds up to <paramref name="amount"/>. Returns how many could NOT be added. Unique items get fresh instance ids.
        /// Optional origin and grade are stored on new stacks and only merge with stacks that share the same metadata
        /// (older saves have empty origin and grade 0, so they still stack together).</summary>
        public int Add(string itemId, int amount) => Add(itemId, amount, null, null, 0f);

        public int Add(string itemId, int amount, string originBodyId, string originSiteId, float grade)
        {
            if (amount <= 0) return 0;
            if (!_lookup.TryGetItem(itemId, out var def)) { Debug.LogWarning($"[Xenoarch] Inventory: unknown item '{itemId}'"); return amount; }

            int remaining = amount;
            if (def.uniqueInstance)
            {
                while (remaining > 0 && AddInstanceInternal(def, StableId.NewInstanceId(), originBodyId, originSiteId, grade)) remaining--;
                if (remaining != amount) Changed?.Invoke();
                return remaining;
            }

            int max = def.EffectiveMaxStack;
            var probe = new ItemStack { itemId = itemId, originBodyId = originBodyId, originSiteId = originSiteId, grade = grade };
            // Pass 1: top up existing stacks with the same origin / grade.
            foreach (var s in _slots)
            {
                if (remaining <= 0) break;
                if (s.IsEmpty || s.itemId != itemId || s.count >= max || !s.SameMeta(probe)) continue;
                int add = Mathf.Min(max - s.count, remaining);
                s.count += add; remaining -= add;
            }
            // Pass 2: empty slots.
            foreach (var s in _slots)
            {
                if (remaining <= 0) break;
                if (!s.IsEmpty) continue;
                int add = Mathf.Min(max, remaining);
                s.itemId = itemId; s.count = add; s.instanceId = null;
                s.originBodyId = originBodyId; s.originSiteId = originSiteId; s.grade = grade;
                remaining -= add;
            }
            if (remaining != amount) Changed?.Invoke();
            return remaining;
        }

        /// <summary>Adds a stack (or as much as will fit) keeping its origin and grade. Returns how many could not be added.</summary>
        public int AddStack(ItemStack stack)
        {
            if (stack == null || stack.IsEmpty) return 0;
            if (!string.IsNullOrEmpty(stack.instanceId))
                return AddInstance(stack.itemId, stack.instanceId) ? 0 : 1;
            return Add(stack.itemId, stack.count, stack.originBodyId, stack.originSiteId, stack.grade);
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

        private bool AddInstanceInternal(ItemDefinition def, string instanceId) =>
            AddInstanceInternal(def, instanceId, null, null, 0f);

        private bool AddInstanceInternal(ItemDefinition def, string instanceId, string originBodyId, string originSiteId, float grade)
        {
            if (string.IsNullOrEmpty(instanceId) || ContainsInstance(instanceId)) return false;
            foreach (var s in _slots)
            {
                if (!s.IsEmpty) continue;
                s.itemId = def.Id; s.count = 1; s.instanceId = instanceId;
                s.originBodyId = originBodyId; s.originSiteId = originSiteId; s.grade = grade;
                return true;
            }
            return false;
        }

        /// <summary>Phase 2 M1: optional guard for bulk removal by item id (crafting, refining, stack sales). Returns false for
        /// a unique instance that must not be consumed (protected artifacts); those slots are skipped. Null = no guard.</summary>
        public Func<string, bool> CanBulkRemoveInstance;

        /// <summary>Removes up to <paramref name="amount"/> (from the last stacks first, then the extra store). Returns how many were removed.</summary>
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
            if (removed < amount && ExtraRemove != null) removed += Math.Max(0, ExtraRemove(itemId, amount - removed));
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
            if (ExtraRemoveInstance != null && ExtraRemoveInstance(instanceId)) { Changed?.Invoke(); return true; }
            return false;
        }

        /// <summary>Takes the stack at <paramref name="index"/> and leaves the slot empty.</summary>
        public ItemStack TakeAt(int index)
        {
            if (index < 0 || index >= _slots.Count || _slots[index].IsEmpty) return new ItemStack();
            var c = _slots[index].Clone();
            _slots[index].Clear();
            Changed?.Invoke();
            return c;
        }

        /// <summary>Places a stack in a preferred empty slot, or the first empty slot. False if there is no room.</summary>
        public bool TryPlace(ItemStack stack, int preferredIndex = -1)
        {
            if (stack == null || stack.IsEmpty) return true;
            if (preferredIndex >= 0 && preferredIndex < _slots.Count && _slots[preferredIndex].IsEmpty)
            {
                _slots[preferredIndex].Assign(stack.Clone());
                Changed?.Invoke();
                return true;
            }
            foreach (var s in _slots)
            {
                if (!s.IsEmpty) continue;
                s.Assign(stack.Clone());
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


        /// <summary>Splits <paramref name="amount"/> off the stack at <paramref name="index"/> into another slot
        /// (same origin and grade). Amount must be between 1 and count-1. False if there is no room or the item cannot split.</summary>
        public bool SplitAt(int index, int amount)
        {
            if (index < 0 || index >= _slots.Count) return false;
            var s = _slots[index];
            if (s.IsEmpty || s.count <= 1 || amount <= 0 || amount >= s.count) return false;
            if (!_lookup.TryGetItem(s.itemId, out var def) || def.uniqueInstance || def.EffectiveMaxStack <= 1) return false;
            var part = s.Clone();
            part.count = amount;
            s.count -= amount;
            bool placed = false;
            int max = def.EffectiveMaxStack;
            var merged = new System.Collections.Generic.List<(ItemStack slot, int add)>();
            foreach (var o in _slots)
            {
                if (o == s || o.IsEmpty || o.itemId != s.itemId || !o.SameMeta(part) || o.count >= max) continue;
                int add = Mathf.Min(max - o.count, part.count);
                o.count += add; part.count -= add;
                merged.Add((o, add));
                if (part.count <= 0) { placed = true; break; }
            }
            if (!placed)
            {
                foreach (var o in _slots)
                {
                    if (!o.IsEmpty) continue;
                    o.Assign(part);
                    part.count = 0;
                    placed = true;
                    break;
                }
            }
            if (!placed)
            {
                foreach (var m in merged) m.slot.count -= m.add;
                s.count += amount;
                return false;
            }
            if (s.count <= 0) s.Clear();
            Changed?.Invoke();
            return true;
        }

        public InventorySaveData Capture()
        {
            var data = new InventorySaveData { capacity = _slots.Count };
            foreach (var s in _slots) data.slots.Add(s.Clone());
            return data;
        }

        /// <summary>Restores slots. Grows to the saved size when a save is larger than the current capacity (New Test All).
        /// Never shrinks below the constructed size, so older 24-slot saves pick up the extra empty slots. Unknown item ids
        /// and duplicate instance ids are dropped with a warning (never duplicated).</summary>
        public void Restore(InventorySaveData data)
        {
            int want = _slots.Count;
            if (data != null)
                want = Mathf.Max(want, Mathf.Max(data.capacity, data.slots != null ? data.slots.Count : 0));
            while (_slots.Count < want) _slots.Add(new ItemStack());
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
                    _slots[i].Assign(src);
                    _slots[i].count = 1;
                }
                else
                {
                    _slots[i].Assign(src);
                    _slots[i].count = Mathf.Clamp(src.count, 1, def.EffectiveMaxStack);
                    _slots[i].instanceId = null;
                }
            }
            Changed?.Invoke();
        }
    }
}
