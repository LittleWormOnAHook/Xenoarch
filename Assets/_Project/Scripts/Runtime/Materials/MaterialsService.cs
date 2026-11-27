using System;
using System.Collections.Generic;
using System.Linq;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Materials
{
    [Serializable] public sealed class StrataAcc { public string key; public float volume; }
    [Serializable] public sealed class NodeState { public string id; public float harvestedAt; }

    [Serializable]
    public sealed class RefinerJob
    {
        public string recipeId;
        public float elapsed, duration;
        public bool done;
        /// <summary>How many times the recipe runs in this slot. 0 in older data is treated as 1.</summary>
        public int batches = 1;
        /// <summary>Stable id for UI actions (not required for save identity).</summary>
        public int uid;
        public int Batches => Mathf.Max(1, batches);
    }

    [Serializable]
    public sealed class MaterialsState
    {
        public List<NodeState> nodes = new List<NodeState>();
        /// <summary>Dug volume carried over per (site|material|band) towards the next strata unit.</summary>
        public List<StrataAcc> strata = new List<StrataAcc>();
        /// <summary>Up to <see cref="MaterialsService.RefinerSlots"/> jobs, each occupying a slot until collected.</summary>
        public List<RefinerJob> jobs = new List<RefinerJob>();
        /// <summary>First active job (existing tests and older call sites). Not serialized.</summary>
        public RefinerJob job => jobs != null && jobs.Count > 0 ? jobs[0] : new RefinerJob();
        public bool hasJob => jobs != null && jobs.Count > 0;
    }

    /// <summary>
    /// Materials loop (section "materials.state"): planet resource nodes (harvest + timed respawn), the ship refiner
    /// (up to four timed jobs at once; each job is N recipe batches, inputs taken on start, outputs delivered on
    /// completion or when cargo has room), refuelling the ship from fuel items, and paying lab reagents.
    /// </summary>
    public sealed class MaterialsService
    {
        public const string Key = "materials.state";
        public const int RefinerSlots = 4;
        public MaterialsState State { get; private set; } = new MaterialsState();
        public IReadOnlyList<RefineRecipe> Recipes => _recipes;
        public event Action Changed;
        public event Action<RefineRecipe, int> RefineCompleted;

        readonly IItemLookup _items;
        readonly InventoryModel _inv;
        /// <summary>Where inputs come from and outputs go: backpack + ship storage crates (set by GameSession).</summary>
        public ItemPool Pool { get; set; }
        readonly List<RefineRecipe> _recipes = new List<RefineRecipe>();

        public MaterialsService(IItemLookup items, InventoryModel inventory, IEnumerable<RefineRecipe> recipes, SaveService saves)
        {
            _items = items; _inv = inventory; Pool = new ItemPool(items, inventory);
            if (recipes != null) foreach (var r in recipes) if (r != null) _recipes.Add(r);
            saves?.Register(new JsonStateSection<MaterialsState>(Key, 13, () => State, s =>
            {
                State = s ?? new MaterialsState();
                if (State.jobs == null) State.jobs = new List<RefinerJob>();
                Changed?.Invoke();
            }));
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

        // ------------------------------------------------------------------ strata
        /// <summary>Adds dug volume to a strata accumulator; returns the new total.</summary>
        public float AddStrataVolume(string key, float v)
        {
            var a = State.strata.FirstOrDefault(x => x.key == key);
            if (a == null) State.strata.Add(a = new StrataAcc { key = key });
            a.volume = Mathf.Max(0f, a.volume + v);
            return a.volume;
        }

        // ------------------------------------------------------------------ refiner
        int _nextJobUid;
        void NormalizeJobs() { if (State.jobs == null) State.jobs = new List<RefinerJob>(); }
        int NextJobUid()
        {
            NormalizeJobs();
            int max = _nextJobUid;
            foreach (var j in State.jobs) if (j.uid > max) max = j.uid;
            _nextJobUid = max;
            return ++_nextJobUid;
        }

        /// <summary>True when at least one slot has a job (running or waiting to be collected).</summary>
        public bool Busy { get { NormalizeJobs(); return State.jobs.Count > 0; } }
        public int JobCount { get { NormalizeJobs(); return State.jobs.Count; } }
        public int FreeSlotCount { get { NormalizeJobs(); return Mathf.Max(0, RefinerSlots - State.jobs.Count); } }
        public IReadOnlyList<RefinerJob> Jobs { get { NormalizeJobs(); return State.jobs; } }

        /// <summary>Complete batches the current stock can pay for (every input). 0 if the recipe has no inputs.</summary>
        public int AffordableBatches(RefineRecipe r)
        {
            if (r?.inputs == null || r.inputs.Count == 0) return 0;
            int n = int.MaxValue;
            foreach (var i in r.inputs)
            {
                if (i.count <= 0) continue;
                n = Mathf.Min(n, Pool.Count(i.itemId) / i.count);
            }
            return n == int.MaxValue ? 0 : Mathf.Max(0, n);
        }

        /// <summary>Affordable batches whose outputs would fit after the inputs are taken (Max). May be less than All.</summary>
        public int MaxFittingBatches(RefineRecipe r)
        {
            int afford = AffordableBatches(r);
            if (afford <= 0) return 0;
            int lo = 0, hi = afford;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (OutputsFit(r, mid)) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        bool OutputsFit(RefineRecipe r, int batches)
        {
            if (r == null || batches <= 0) return batches == 0;
            var snap = Pool.Capture();
            foreach (var i in r.inputs) Pool.Remove(i.itemId, i.count * batches);
            bool ok = Pool.TryAddAll(r.outputs.Select(o => (o.itemId, o.count * batches)));
            Pool.Restore(snap);
            return ok;
        }

        public bool CanRefine(RefineRecipe r, out string reason) => CanRefine(r, 1, out reason);

        public bool CanRefine(RefineRecipe r, int batches, out string reason)
        {
            reason = null;
            if (r == null) { reason = "Unknown recipe."; return false; }
            if (batches < 1) { reason = "Amount must be at least 1 batch."; return false; }
            NormalizeJobs();
            if (State.jobs.Count >= RefinerSlots)
            {
                reason = State.jobs.Exists(j => j.done)
                    ? "Collect a finished batch first (cargo full)."
                    : "All 4 refiner slots are busy.";
                return false;
            }
            foreach (var i in r.inputs)
            {
                int need = i.count * batches, have = Pool.Count(i.itemId);
                if (have < need) { reason = $"Needs {need} {Name(i.itemId)} (have {have})."; return false; }
            }
            return true;
        }

        public bool StartRefine(RefineRecipe r, out string reason) => StartRefine(r, 1, out reason);

        public bool StartRefine(RefineRecipe r, int batches, out string reason)
        {
            if (!CanRefine(r, batches, out reason)) return false;
            foreach (var i in r.inputs) Pool.Remove(i.itemId, i.count * batches);
            State.jobs.Add(new RefinerJob { recipeId = r.Id, elapsed = 0f, duration = r.seconds * batches, batches = batches, uid = NextJobUid() });
            Changed?.Invoke();
            return true;
        }

        /// <summary>Stops the first running (not yet finished) job and returns its inputs. Finished jobs must be collected.</summary>
        public void CancelRefine()
        {
            NormalizeJobs();
            int i = State.jobs.FindIndex(j => !j.done);
            if (i >= 0) CancelRefineAt(i);
        }

        public bool CancelRefineAt(int index)
        {
            NormalizeJobs();
            if (index < 0 || index >= State.jobs.Count) return false;
            return CancelJob(State.jobs[index]);
        }

        public bool CancelRefineUid(int uid)
        {
            NormalizeJobs();
            int i = State.jobs.FindIndex(j => j.uid == uid);
            return i >= 0 && CancelRefineAt(i);
        }

        bool CancelJob(RefinerJob job)
        {
            if (job.done) return false; // finished jobs must be collected, not discarded
            var r = Recipe(job.recipeId);
            if (r != null)
            {
                int n = job.Batches;
                foreach (var i in r.inputs) Pool.Add(i.itemId, i.count * n);
            }
            State.jobs.Remove(job);
            Changed?.Invoke();
            return true;
        }

        public void Tick(float dt)
        {
            NormalizeJobs();
            if (State.jobs.Count == 0) return;
            foreach (var j in State.jobs)
                if (!j.done) { j.elapsed += Mathf.Max(0f, dt); if (j.elapsed >= j.duration) j.done = true; }
            TryDeliverAll();
        }

        int TryDeliverAll()
        {
            int n = 0;
            for (int i = State.jobs.Count - 1; i >= 0; i--)
                if (TryDeliverAt(i)) n++;
            return n;
        }

        /// <summary>Puts every finished job that fits into cargo. False if nothing was delivered.</summary>
        public bool TryDeliver() => TryDeliverAll() > 0;

        /// <summary>Puts one finished job into cargo (all or nothing). False if it is not done or cargo has no room yet.</summary>
        public bool TryDeliverAt(int index)
        {
            NormalizeJobs();
            if (index < 0 || index >= State.jobs.Count || !State.jobs[index].done) return false;
            var job = State.jobs[index];
            var r = Recipe(job.recipeId);
            if (r == null) { State.jobs.RemoveAt(index); Changed?.Invoke(); return false; }
            int n = job.Batches;
            if (!Pool.TryAddAll(r.outputs.Select(o => (o.itemId, o.count * n)))) return false;
            State.jobs.RemoveAt(index);
            Changed?.Invoke();
            RefineCompleted?.Invoke(r, n);
            return true;
        }

        public bool TryDeliverUid(int uid)
        {
            NormalizeJobs();
            int i = State.jobs.FindIndex(j => j.uid == uid);
            return i >= 0 && TryDeliverAt(i);
        }

        // ------------------------------------------------------------------ fuel
        IEnumerable<MaterialDefinition> Held(Func<MaterialDefinition, bool> filter)
        {
            var seen = new HashSet<string>();
            foreach (var id in Pool.Ids())
                if (seen.Add(id) && Material(id) is MaterialDefinition m && filter(m)) yield return m;
        }

        public float FuelInCargo() => Held(m => m.fuelValue > 0f).Sum(m => m.fuelValue * Pool.Count(m.Id));

        /// <summary>Fills the tank from fuel items (biggest first; the last item may overfill and is still used).</summary>
        public float Refuel(DMI.Xenoarch.Galaxy.ExplorationService x)
        {
            if (x == null) return 0f;
            float added = 0f;
            foreach (var m in Held(m => m.fuelValue > 0f).OrderByDescending(m => m.fuelValue).ToList())
                while (x.Ship.fuel < x.Ship.fuelCapacity - 0.01f && Pool.Count(m.Id) > 0)
                {
                    Pool.Remove(m.Id, 1);
                    float before = x.Ship.fuel;
                    x.Refuel(m.fuelValue);
                    added += x.Ship.fuel - before;
                }
            if (added > 0f) Changed?.Invoke();
            return added;
        }

        /// <summary>Spends fuel items (biggest first) into a custom tank. <paramref name="accept"/> is given each
        /// item's fuel value and must return how much was actually added (0 = tank full, stop).</summary>
        public float RefuelCustom(Func<float, float> accept)
        {
            if (accept == null) return 0f;
            float added = 0f;
            foreach (var m in Held(m => m.fuelValue > 0f).OrderByDescending(m => m.fuelValue).ToList())
            {
                while (Pool.Count(m.Id) > 0)
                {
                    float got = accept(m.fuelValue);
                    if (got <= 0.01f) { if (added > 0f) Changed?.Invoke(); return added; }
                    Pool.Remove(m.Id, 1);
                    added += got;
                }
            }
            if (added > 0f) Changed?.Invoke();
            return added;
        }

        // ------------------------------------------------------------------ lab reagents
        public int ReagentUnits() => Held(m => m.reagentValue > 0).Sum(m => m.reagentValue * Pool.Count(m.Id));

        /// <summary>Consumes items worth at least <paramref name="units"/> reagent units (smallest first). False = not enough (nothing taken).</summary>
        public bool PayReagents(int units)
        {
            if (units <= 0) return true;
            if (ReagentUnits() < units) return false;
            int left = units;
            foreach (var m in Held(m => m.reagentValue > 0).OrderBy(m => m.reagentValue).ToList())
                while (left > 0 && Pool.Count(m.Id) > 0) { Pool.Remove(m.Id, 1); left -= m.reagentValue; }
            Changed?.Invoke();
            return true;
        }
    }
}
