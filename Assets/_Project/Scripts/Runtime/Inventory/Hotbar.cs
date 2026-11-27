using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Inventory
{
    /// <summary>Saved hotbar assignment (t108u). Empty list = default layout. Stacks (t243u) are the actual items
    /// parked on the bar so they do not use backpack space; missing in old saves.</summary>
    [Serializable]
    public sealed class HotbarData
    {
        public List<string> slots = new List<string>();
        public List<ItemStack> stacks = new List<ItemStack>();
    }

    /// <summary>
    /// Hotbar: 8 slots on keys 1-8. Slot 1 is the handheld scanner (fixed). Slots 2-8 are assigned by dragging items
    /// from the inventory; a dig tool on slot N is equipped with key N. The default layout matches the old fixed keys
    /// (2 shovel, 3 precision, 4 brush, 5 drill), so older saves behave as before.
    /// Assigned stacks live on the hotbar itself and do not occupy backpack slots.
    /// </summary>
    public sealed class Hotbar
    {
        public const int Size = 8;
        public const string Key = "player.hotbar";
        public HotbarData Data = new HotbarData();
        public event Action Changed;
        readonly Func<string> _scannerId;
        readonly Func<IReadOnlyList<string>> _defaults;
        int _busy;
        void Begin() => _busy++;
        void End() { if (_busy > 0) _busy--; }

        public Hotbar(Func<string> scannerId, Func<IReadOnlyList<string>> defaultTools)
        {
            _scannerId = scannerId; _defaults = defaultTools;
        }

        public bool IsDefault => Data == null || Data.slots == null || Data.slots.Count == 0;

        /// <summary>Item id on slot i (0 = key 1), or null.</summary>
        public string At(int i)
        {
            if (i < 0 || i >= Size) return null;
            if (i == 0) return _scannerId?.Invoke();
            if (IsDefault) { var d = _defaults?.Invoke(); return d != null && i - 1 < d.Count ? d[i - 1] : null; }
            return i < Data.slots.Count ? NullIfEmpty(Data.slots[i]) : null;
        }

        /// <summary>The stack stored on slot i (never slot 0), or null if the slot is only an assignment.</summary>
        public ItemStack StackAt(int i)
        {
            if (i <= 0 || i >= Size || Data?.stacks == null || i >= Data.stacks.Count) return null;
            var s = Data.stacks[i];
            return s != null && !s.IsEmpty ? s : null;
        }

        public IEnumerable<ItemStack> StoredStacks
        {
            get
            {
                if (Data?.stacks == null) yield break;
                for (int i = 1; i < Size && i < Data.stacks.Count; i++)
                    if (Data.stacks[i] != null && !Data.stacks[i].IsEmpty) yield return Data.stacks[i];
            }
        }

        public int Count(string itemId)
        {
            int n = 0;
            foreach (var s in StoredStacks) if (s.itemId == itemId) n += s.count;
            return n;
        }

        public bool ContainsInstance(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return false;
            foreach (var s in StoredStacks) if (s.instanceId == instanceId) return true;
            return false;
        }

        static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        void Materialise()
        {
            if (!IsDefault) { while (Data.slots.Count < Size) Data.slots.Add(""); EnsureStacks(); return; }
            var l = new List<string>(Size);
            for (int i = 0; i < Size; i++) l.Add(At(i) ?? "");
            Data = new HotbarData { slots = l, stacks = Data != null && Data.stacks != null ? Data.stacks : new List<ItemStack>() };
            EnsureStacks();
        }

        void EnsureStacks()
        {
            if (Data == null) Data = new HotbarData();
            if (Data.stacks == null) Data.stacks = new List<ItemStack>();
            while (Data.stacks.Count < Size) Data.stacks.Add(new ItemStack());
        }

        void SetStack(int i, ItemStack stack)
        {
            EnsureStacks();
            if (stack == null || stack.IsEmpty) Data.stacks[i].Clear();
            else { Data.stacks[i].itemId = stack.itemId; Data.stacks[i].count = stack.count; Data.stacks[i].instanceId = stack.instanceId; }
        }

        /// <summary>Put an item on slot i (1..7; slot 0 is the scanner). Removes it from any other slot. False if refused.</summary>
        public bool Assign(int i, string itemId)
        {
            if (i <= 0 || i >= Size || string.IsNullOrEmpty(itemId)) return false;
            Materialise();
            for (int k = 1; k < Size; k++) if (Data.slots[k] == itemId) Data.slots[k] = "";
            Data.slots[i] = itemId;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Move the backpack stack at <paramref name="backpackIndex"/> onto hotbar slot i. Any stack already on
        /// that slot (or another slot of the same item) is placed back in the backpack. False if refused.</summary>
        public bool AssignFrom(int i, InventoryModel inv, int backpackIndex)
        {
            if (i <= 0 || i >= Size || inv == null) return false;
            if (backpackIndex < 0 || backpackIndex >= inv.Slots.Count || inv.Slots[backpackIndex].IsEmpty) return false;
            Begin();
            try
            {
            Materialise();
            var moving = inv.TakeAt(backpackIndex);
            if (moving == null || moving.IsEmpty) return false;

            // Return stacks evicted from this or any other slot of the same item.
            for (int k = 1; k < Size; k++)
            {
                bool evict = k == i || Data.slots[k] == moving.itemId;
                if (!evict) continue;
                var held = StackAt(k);
                if (held != null && !inv.TryPlace(held, backpackIndex))
                {
                    // Put the incoming stack back and leave the hotbar as it was.
                    inv.TryPlace(moving, backpackIndex);
                    return false;
                }
                if (held != null) SetStack(k, null);
                if (k != i) Data.slots[k] = "";
            }

            Data.slots[i] = moving.itemId;
            SetStack(i, moving);
            Changed?.Invoke();
            return true;
            }
            finally { End(); }
        }

        /// <summary>Move a backpack stack of each assigned item onto the bar when the slot has no stored stack yet.
        /// Safe to call repeatedly (inventory Changed, load, New Game).</summary>
        public void ShelveFrom(InventoryModel inv)
        {
            if (inv == null || _busy > 0) return;
            Begin();
            bool any = false;
            try
            {
            for (int i = 1; i < Size; i++)
            {
                string id = At(i);
                if (id == null || StackAt(i) != null) continue;
                int idx = -1;
                for (int j = 0; j < inv.Slots.Count; j++)
                    if (!inv.Slots[j].IsEmpty && inv.Slots[j].itemId == id) { idx = j; break; }
                if (idx < 0) continue;
                Materialise();
                var st = inv.TakeAt(idx);
                if (st == null || st.IsEmpty) continue;
                Data.slots[i] = id;
                SetStack(i, st);
                any = true;
            }
            if (any) Changed?.Invoke();
            }
            finally { End(); }
        }

        /// <summary>Return the stored stack to the backpack and clear the assignment. False if the backpack has no room.</summary>
        public bool TryReturnTo(int i, InventoryModel inv, int preferredIndex = -1)
        {
            if (i <= 0 || i >= Size || inv == null) return false;
            Begin();
            try
            {
                var held = StackAt(i);
                if (held != null && !inv.TryPlace(held, preferredIndex)) return false;
                Clear(i);
                return true;
            }
            finally { End(); }
        }

        public int Remove(string itemId, int amount)
        {
            if (amount <= 0 || string.IsNullOrEmpty(itemId)) return 0;
            EnsureStacks();
            int removed = 0;
            for (int i = Size - 1; i >= 1 && removed < amount; i--)
            {
                var s = i < Data.stacks.Count ? Data.stacks[i] : null;
                if (s == null || s.IsEmpty || s.itemId != itemId) continue;
                int take = Mathf.Min(s.count, amount - removed);
                s.count -= take; removed += take;
                if (s.count <= 0) s.Clear();
            }
            if (removed > 0) Changed?.Invoke();
            return removed;
        }

        public bool RemoveInstance(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return false;
            EnsureStacks();
            for (int i = 1; i < Size && i < Data.stacks.Count; i++)
            {
                var s = Data.stacks[i];
                if (s == null || s.IsEmpty || s.instanceId != instanceId) continue;
                s.Clear();
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        public void Clear(int i)
        {
            if (i <= 0 || i >= Size) return;
            Materialise();
            Data.slots[i] = "";
            SetStack(i, null);
            Changed?.Invoke();
        }

        /// <summary>Swap two slots (1..7), including stored stacks.</summary>
        public void Swap(int a, int b)
        {
            if (a <= 0 || b <= 0 || a >= Size || b >= Size || a == b) return;
            Materialise();
            (Data.slots[a], Data.slots[b]) = (Data.slots[b], Data.slots[a]);
            EnsureStacks();
            (Data.stacks[a], Data.stacks[b]) = (Data.stacks[b], Data.stacks[a]);
            Changed?.Invoke();
        }

        public void Set(HotbarData d)
        {
            Data = d ?? new HotbarData();
            if (Data.slots == null) Data.slots = new List<string>();
            if (Data.stacks == null) Data.stacks = new List<ItemStack>();
            Changed?.Invoke();
        }
    }
}
