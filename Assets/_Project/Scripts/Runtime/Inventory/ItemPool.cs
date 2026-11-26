using System;
using System.Collections.Generic;
using DMI.Xenoarch.Items;

namespace DMI.Xenoarch.Inventory
{
    /// <summary>
    /// Several inventories read as one stock: the backpack first, then the ship's storage crates. Machines and crafting
    /// count and take ingredients from all of them (backpack first) and put products in the backpack, overflowing into
    /// the first crate that accepts the item's category.
    /// </summary>
    public sealed class ItemPool
    {
        readonly IItemLookup _items;
        readonly InventoryModel _primary;
        readonly Func<IEnumerable<(InventoryModel inv, Func<ItemCategory, bool> accepts)>> _extra;

        public ItemPool(IItemLookup items, InventoryModel primary, Func<IEnumerable<(InventoryModel, Func<ItemCategory, bool>)>> extra = null)
        { _items = items; _primary = primary; _extra = extra; }

        public InventoryModel Primary => _primary;

        public IEnumerable<InventoryModel> All()
        {
            yield return _primary;
            if (_extra != null) foreach (var e in _extra()) if (e.inv != null) yield return e.inv;
        }

        public int Count(string itemId) { int n = 0; foreach (var i in All()) n += i.Count(itemId); return n; }

        /// <summary>Removes up to <paramref name="amount"/>, backpack first. Returns how many were removed.</summary>
        public int Remove(string itemId, int amount)
        {
            int left = amount;
            foreach (var i in All()) { if (left <= 0) break; left -= i.Remove(itemId, left); }
            return amount - left;
        }

        /// <summary>Distinct item ids held anywhere in the pool.</summary>
        public IEnumerable<string> Ids()
        {
            var seen = new HashSet<string>();
            foreach (var i in All()) foreach (var s in i.Slots) if (!s.IsEmpty && seen.Add(s.itemId)) yield return s.itemId;
        }

        /// <summary>Adds to the backpack, then to crates that accept the category. Returns what did not fit.</summary>
        public int Add(string itemId, int amount)
        {
            int left = _primary.Add(itemId, amount);
            if (left <= 0 || _extra == null) return left;
            var cat = _items != null && _items.TryGetItem(itemId, out var d) ? d.category : ItemCategory.Misc;
            foreach (var e in _extra()) { if (left <= 0) break; if (e.inv != null && (e.accepts == null || e.accepts(cat))) left = e.inv.Add(itemId, left); }
            return left;
        }

        public List<InventorySaveData> Capture() { var l = new List<InventorySaveData>(); foreach (var i in All()) l.Add(i.Capture()); return l; }
        public void Restore(List<InventorySaveData> snap) { int k = 0; foreach (var i in All()) { if (k < snap.Count) i.Restore(snap[k]); k++; } }

        /// <summary>All-or-nothing add of several stacks (rolls back on failure).</summary>
        public bool TryAddAll(IEnumerable<(string id, int n)> stacks)
        {
            var snap = Capture();
            foreach (var s in stacks) if (Add(s.id, s.n) > 0) { Restore(snap); return false; }
            return true;
        }
    }
}
