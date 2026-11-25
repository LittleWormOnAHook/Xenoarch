using System;
using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Materials
{
    [Serializable] public sealed class NodeState { public string id; public float harvestedAt; }

    [Serializable]
    public sealed class RefinerJob { public string recipeId; public float elapsed, duration; public bool done; }

    [Serializable]
    public sealed class MaterialsState
    {
        public List<NodeState> nodes = new List<NodeState>();
        public bool hasJob;
        public RefinerJob job = new RefinerJob();
    }

    /// <summary>
    /// Materials loop (section "materials.state"): planet resource nodes (harvest + timed respawn), the ship refiner
    /// (one timed recipe at a time, inputs taken on start, outputs delivered on completion or when cargo has room),
    /// refuelling the ship from fuel items, and paying lab reagents.
    /// </summary>
    public sealed class MaterialsService
    {
        public const string Key = "materials.state";
        public MaterialsState State { get; private set; } = new MaterialsState();
        public IReadOnlyList<RefineRecipe> Recipes => _recipes;
        public event Action Changed;
        public event Action<RefineRecipe> RefineCompleted;

        readonly IItemLookup _items;
        readonly InventoryModel _inv;
        readonly List<RefineRecipe> _recipes = new List<RefineRecipe>();

        public MaterialsService(IItemLookup items, InventoryModel inventory, IEnumerable<RefineRecipe> recipes, SaveService saves)
        {
            _items = items; _inv = inventory;
            if (recipes != null) foreach (var r in recipes) if (r != null) _recipes.Add(r);
            saves?.Register(new JsonStateSection<MaterialsState>(Key, 13, () => State, s => { State = s ?? new MaterialsState(); Changed?.Invoke(); }));
        }

        public MaterialDefinition Material(string id) => _items != null && _items.TryGetItem(id, out var d) ? d as MaterialDefinition : null;
        public RefineRecipe Recipe(string id) => _recipes.FirstOrDefault(r => r.Id == id);
        public string Name(string id) { var d = _items != null && _items.TryGetItem(id, out var x) ? x : null; return d != null ? d.DisplayNameOrId : id; }

        // ------------------------------------------------------------------ nodes
        public bool NodeAvailable(string nodeId, float now, float respawnSeconds)
        {
            var n = State.nodes.FirstOrDefault(x => x.id == nodeId);
            return n == null || now - n.harvestedAt >= respawnSeconds;
        }

        public void MarkHarvested(string nodeId, float now)
        {
            var n = State.nodes.FirstOrDefault(x => x.id == nodeId);
            if (n == null) State.nodes.Add(n = new NodeState { id = nodeId });
            n.harvestedAt = now;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ refiner
        public bool Busy => State.hasJob;

        public bool CanRefine(RefineRecipe r, out string reason)
        {
            reason = null;
            if (r == null) { reason = "Unknown recipe."; return false; }
            if (Busy) { reason = State.job.done ? "Collect the finished batch first (cargo full)." : "Refiner busy."; return false; }
            foreach (var i in r.inputs)
                if (_inv.Count(i.itemId) < i.count) { reason = $"Needs {i.count} {Name(i.itemId)} (have {_inv.Count(i.itemId)})."; return false; }
            return true;
        }

        public bool StartRefine(RefineRecipe r, out string reason)
        {
            if (!CanRefine(r, out reason)) return false;
            foreach (var i in r.inputs) _inv.Remove(i.itemId, i.count);
            State.hasJob = true;
            State.job = new RefinerJob { recipeId = r.Id, elapsed = 0f, duration = r.seconds };
            Changed?.Invoke();
            return true;
        }

        /// <summary>Stops the running batch and returns its inputs.</summary>
        public void CancelRefine()
        {
            if (!State.hasJob) return;
            var r = Recipe(State.job.recipeId);
            if (r != null && !State.job.done) foreach (var i in r.inputs) _inv.Add(i.itemId, i.count);
            State.hasJob = false;
            Changed?.Invoke();
        }

        public void Tick(float dt)
        {
            if (!State.hasJob) return;
            var j = State.job;
            if (!j.done) { j.elapsed += Mathf.Max(0f, dt); if (j.elapsed >= j.duration) j.done = true; else return; }
            TryDeliver();
        }

        /// <summary>Puts a finished batch into cargo (all or nothing). False if cargo has no room yet.</summary>
        public bool TryDeliver()
        {
            if (!State.hasJob || !State.job.done) return false;
            var r = Recipe(State.job.recipeId);
            if (r == null) { State.hasJob = false; Changed?.Invoke(); return false; }
            var before = _inv.Capture();
            foreach (var o in r.outputs)
                if (_inv.Add(o.itemId, o.count) > 0) { _inv.Restore(before); return false; }
            State.hasJob = false;
            Changed?.Invoke();
            RefineCompleted?.Invoke(r);
            return true;
        }

        // ------------------------------------------------------------------ fuel
        IEnumerable<MaterialDefinition> Held(Func<MaterialDefinition, bool> filter)
        {
            var seen = new HashSet<string>();
            foreach (var s in _inv.Slots)
                if (!s.IsEmpty && seen.Add(s.itemId) && Material(s.itemId) is MaterialDefinition m && filter(m)) yield return m;
        }

        public float FuelInCargo() => Held(m => m.fuelValue > 0f).Sum(m => m.fuelValue * _inv.Count(m.Id));

        /// <summary>Fills the tank from fuel items (biggest first; the last item may overfill and is still used).</summary>
        public float Refuel(DMI.Xenoarch.Galaxy.ExplorationService x)
        {
            if (x == null) return 0f;
            float added = 0f;
            foreach (var m in Held(m => m.fuelValue > 0f).OrderByDescending(m => m.fuelValue).ToList())
                while (x.Ship.fuel < x.Ship.fuelCapacity - 0.01f && _inv.Count(m.Id) > 0)
                {
                    _inv.Remove(m.Id, 1);
                    float before = x.Ship.fuel;
                    x.Refuel(m.fuelValue);
                    added += x.Ship.fuel - before;
                }
            if (added > 0f) Changed?.Invoke();
            return added;
        }

        // ------------------------------------------------------------------ lab reagents
        public int ReagentUnits() => Held(m => m.reagentValue > 0).Sum(m => m.reagentValue * _inv.Count(m.Id));

        /// <summary>Consumes items worth at least <paramref name="units"/> reagent units (smallest first). False = not enough (nothing taken).</summary>
        public bool PayReagents(int units)
        {
            if (units <= 0) return true;
            if (ReagentUnits() < units) return false;
            int left = units;
            foreach (var m in Held(m => m.reagentValue > 0).OrderBy(m => m.reagentValue).ToList())
                while (left > 0 && _inv.Count(m.Id) > 0) { _inv.Remove(m.Id, 1); left -= m.reagentValue; }
            Changed?.Invoke();
            return true;
        }
    }
}
