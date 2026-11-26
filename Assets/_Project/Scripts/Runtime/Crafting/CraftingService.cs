using System;
using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using UnityEngine;

namespace DMI.Xenoarch.Crafting
{
    /// <summary>
    /// Hand / ship crafting, after ASH's flow: MissingReason (gate, busy, upgrade order, ingredients, station, space),
    /// a timed craft, then ingredients are re-checked and consumed, and the output (or upgrade) is applied. Ingredients
    /// come from the item pool (backpack, then ship crates); the product goes to the backpack (overflow to a crate).
    /// </summary>
    public sealed class CraftingService
    {
        public IReadOnlyList<CraftRecipe> Recipes => _recipes;
        public ItemPool Pool { get; set; }
        public Func<string, bool> HasFact = _ => true;
        /// <summary>True while the player is aboard the ship (ship-station recipes).</summary>
        public Func<bool> AboardShip = () => false;
        /// <summary>Upgrade hooks: (upgradeId) -> current level, and apply(upgradeId) -> success.</summary>
        public Func<string, int> UpgradeLevel = _ => 0;
        public Func<string, int> UpgradeMax = _ => 0;
        public Func<string, bool> ApplyUpgrade = _ => false;

        public CraftRecipe Current { get; private set; }
        public float Progress01 => Current == null ? 0f : _duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / _duration);
        public bool Busy => Current != null;
        public event Action<CraftRecipe> Crafted;
        public event Action<CraftRecipe, string> Failed;

        readonly IItemLookup _items;
        readonly List<CraftRecipe> _recipes = new List<CraftRecipe>();
        float _elapsed, _duration;

        public CraftingService(IItemLookup items, ItemPool pool, IEnumerable<CraftRecipe> recipes)
        {
            _items = items; Pool = pool;
            if (recipes != null) foreach (var r in recipes) if (r != null) _recipes.Add(r);
        }

        public string Name(string id) => _items != null && _items.TryGetItem(id, out var d) ? d.DisplayNameOrId : id;
        public bool Known(CraftRecipe r) => r != null && (string.IsNullOrEmpty(r.unlockFact) || HasFact(r.unlockFact));
        public bool HasIngredients(CraftRecipe r) => r != null && r.ingredients.All(i => Pool.Count(i.itemId) >= i.count);

        /// <summary>Why a recipe can't be crafted right now (null = craftable).</summary>
        public string MissingReason(CraftRecipe r)
        {
            if (r == null || (!r.IsUpgrade && string.IsNullOrEmpty(r.outputItemId))) return "Invalid recipe";
            if (!Known(r)) return "Not discovered yet";
            if (Busy) return "Busy";
            if (r.IsUpgrade)
            {
                int have = UpgradeLevel(r.upgradeId);
                if (have >= r.upgradeLevel) return "Already installed";
                if (have < r.upgradeLevel - 1) return "Install the previous upgrade first";
                if (r.upgradeLevel > UpgradeMax(r.upgradeId)) return "Max level";
            }
            foreach (var i in r.ingredients)
            {
                int have = Pool.Count(i.itemId);
                if (have < i.count) return $"Needs {i.count} {Name(i.itemId)} (have {have})";
            }
            if (r.station == CraftStation.Ship && !AboardShip()) return "Needs the ship's workshop (craft aboard)";
            if (!r.IsUpgrade && !HasSpace(r)) return "No space in backpack or crates";
            return null;
        }

        bool HasSpace(CraftRecipe r)
        {
            var snap = Pool.Capture();
            foreach (var i in r.ingredients) Pool.Remove(i.itemId, i.count); // the slots they free count
            bool ok = Pool.Add(r.outputItemId, r.outputAmount) == 0;
            Pool.Restore(snap);
            return ok;
        }

        public bool TryCraft(CraftRecipe r)
        {
            var why = MissingReason(r);
            if (why != null) { Failed?.Invoke(r, why); return false; }
            Current = r; _elapsed = 0f; _duration = r.craftSeconds;
            if (_duration <= 0f) Finish();
            return true;
        }

        public void Cancel() { Current = null; _elapsed = 0f; }

        public void Tick(float dt)
        {
            if (Current == null) return;
            _elapsed += Mathf.Max(0f, dt);
            if (_elapsed >= _duration) Finish();
        }

        void Finish()
        {
            var r = Current; Current = null;
            if (!HasIngredients(r)) { Failed?.Invoke(r, "Missing materials"); return; }
            var snap = Pool.Capture();
            foreach (var i in r.ingredients) Pool.Remove(i.itemId, i.count);
            if (r.IsUpgrade)
            {
                if (!ApplyUpgrade(r.upgradeId)) { Pool.Restore(snap); Failed?.Invoke(r, "Upgrade could not be installed"); return; }
            }
            else if (Pool.Add(r.outputItemId, r.outputAmount) > 0) { Pool.Restore(snap); Failed?.Invoke(r, "No space"); return; }
            Crafted?.Invoke(r);
        }
    }
}
