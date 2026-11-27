using System;
using System.Collections.Generic;
using DMI.Xenoarch.Discovery;
using DMI.Xenoarch.Inventory;
using DMI.Xenoarch.Save;

namespace DMI.Xenoarch.Artifacts
{
    /// <summary>One recovery action (ledger entry). Never removed during a playthrough.</summary>
    [Serializable]
    public sealed class RecoveryRecord
    {
        public string instanceId;
        public string definitionId;
        /// <summary>missing | sold-unexpectedly | record-lost | research-interrupted</summary>
        public string reason;
        /// <summary>backpack | crate | lab-pickup | buy-back | replacement | job-cancelled</summary>
        public string method;
        public float time;
    }

    [Serializable]
    public sealed class RecoveryState
    {
        public List<RecoveryRecord> log = new List<RecoveryRecord>();
        /// <summary>Instances held by the research lab for pickup (no free slot anywhere when they were recovered).</summary>
        public List<string> pendingAtLab = new List<string>();
        /// <summary>Definitions that already got their one controlled replacement (record lost entirely).</summary>
        public List<string> replacementsIssued = new List<string>();
    }

    /// <summary>
    /// Phase 2 M1 runtime safety net for items mandatory progression still needs (Story Critical and not released, or a
    /// piece of an unsolved fragment puzzle). <see cref="Ensure"/> is idempotent and cheap; it runs after every load, when
    /// the lab opens, after a failed transfer and every few seconds of play. It only acts when something is wrong:
    /// <list type="bullet">
    /// <item><b>Missing</b> (record says carried/stored but no container has it: failed transfer, dropped inventory slot,
    /// interrupted load): the same instance id goes back to the backpack, else a ship crate, else the lab holds it for pickup.</item>
    /// <item><b>Sold unexpectedly</b> (Story Critical, unreleased, marked Sold by a path that bypassed the gate): bought back
    /// at the lab for the recorded sale price when affordable, same instance id.</item>
    /// <item><b>Record lost</b> (Story Critical, recovered before, unreleased, no record at all): one controlled replacement
    /// with id <c>recovered:&lt;definition id&gt;</c>, once per definition.</item>
    /// <item><b>Interrupted research</b> (the lab job points at a lost, sold or installed item): the job is cancelled.</item>
    /// </list>
    /// No duplicates: every grant first checks every container and the pickup list, and inventories refuse duplicate ids.
    /// No farming: protected items cannot be sold, recovery happens only for items the player never legitimately gave up,
    /// and unique finds stay in <c>uniquesFound</c> so sites never respawn them. State is saved under <see cref="Key"/>.
    /// </summary>
    public sealed class ArtifactRecoveryService
    {
        public const string Key = "artifacts.recovery";
        public const string ReplacementPrefix = "recovered:";

        readonly ArtifactService _artifacts;
        readonly ArtifactPuzzleService _puzzles;
        readonly ArtifactEligibility _eligibility;
        readonly DiscoveryService _discovery;
        readonly InventoryModel _backpack;
        readonly Func<IEnumerable<InventoryModel>> _crates;
        public RecoveryState State { get; private set; } = new RecoveryState();

        /// <summary>Future containers (rover cargo, exchange escrow, museum loans): true if the instance is held there.</summary>
        public Func<string, bool> HeldElsewhere = _ => false;
        /// <summary>All artifact definitions (for lost-record replacement). Wired by GameSession.</summary>
        public Func<IEnumerable<ArtifactDefinition>> Definitions = () => Array.Empty<ArtifactDefinition>();
        /// <summary>Recorded sale price of an instance (0 if unknown) and a way to charge it. Wired to the economy.</summary>
        public Func<string, int> SalePrice = _ => 0;
        public Func<int, string, bool> Charge = (_, __) => false;
        public Func<float> Clock = () => 0f;
        /// <summary>Player-facing message after a recovery (the runtime shows it as a toast).</summary>
        public event Action<string> Notice;
        public event Action Changed;

        public ArtifactRecoveryService(ArtifactService artifacts, ArtifactPuzzleService puzzles, ArtifactEligibility eligibility, DiscoveryService discovery,
            InventoryModel backpack, Func<IEnumerable<InventoryModel>> artifactCrates, SaveService saves)
        {
            _artifacts = artifacts; _puzzles = puzzles; _eligibility = eligibility; _discovery = discovery; _backpack = backpack; _crates = artifactCrates;
            saves?.Register(new JsonStateSection<RecoveryState>(Key, 17, () => State, s => { State = s ?? new RecoveryState(); Changed?.Invoke(); }));
        }

        public bool IsPending(string instanceId) => !string.IsNullOrEmpty(instanceId) && State.pendingAtLab.Contains(instanceId);

        public bool InAnyContainer(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return false;
            if (_backpack != null && _backpack.ContainsInstance(instanceId)) return true;
            if (_crates != null) foreach (var c in _crates()) if (c != null && c.ContainsInstance(instanceId)) return true;
            return HeldElsewhere != null && HeldElsewhere(instanceId);
        }

        /// <summary>Runs every check; returns how many recovery actions were taken (0 when everything is fine).</summary>
        public int Ensure(string context = null)
        {
            if (_artifacts == null) return 0;
            int actions = 0;
            actions += CollectPending();

            // Snapshot: grants below can add records (replacements).
            var instances = new List<ArtifactInstance>(_artifacts.State.instances);
            foreach (var a in instances)
            {
                if (a == null || string.IsNullOrEmpty(a.instanceId)) continue;
                var d = _artifacts.Definition(a);
                if (d == null) continue;
                if (a.location == ArtifactLocation.Held || a.location == ArtifactLocation.Stored)
                {
                    if (!_eligibility.NeedsSafeguard(a) || InAnyContainer(a.instanceId) || IsPending(a.instanceId)) continue;
                    actions++;
                    Grant(a, "missing");
                }
                else if (a.location == ArtifactLocation.Sold && _eligibility.IsStoryProtected(d))
                {
                    if (InAnyContainer(a.instanceId) || IsPending(a.instanceId)) { a.location = ArtifactLocation.Held; continue; }
                    int price = SalePrice != null ? SalePrice(a.instanceId) : 0;
                    bool paid = price > 0 && Charge != null && Charge(price, a.definitionId);
                    a.location = ArtifactLocation.Held;
                    actions++;
                    Grant(a, "sold-unexpectedly", paid ? $"bought back for {price} cr" : null);
                }
            }

            // Story Critical definitions whose record vanished entirely: one controlled replacement each.
            if (Definitions != null)
                foreach (var d in Definitions())
                {
                    if (d == null || !_eligibility.IsStoryProtected(d) || State.replacementsIssued.Contains(d.Id)) continue;
                    if (_discovery == null || !_discovery.Has(Facts.Recovered(d.Id))) continue;
                    bool exists = false;
                    foreach (var a in _artifacts.State.instances) if (a != null && a.definitionId == d.Id) { exists = true; break; }
                    if (exists) continue;
                    var r = _artifacts.AddRestoredInstance(ReplacementPrefix + d.Id, d, 0.5f, null, null);
                    if (r == null) continue;
                    State.replacementsIssued.Add(d.Id);
                    actions++;
                    Grant(r, "record-lost", "controlled replacement");
                }

            // Interrupted research: the job's item is gone for good.
            var job = _artifacts.Job;
            if (job != null)
            {
                var ji = _artifacts.Get(job.instanceId);
                if (ji == null || ji.location == ArtifactLocation.Sold || ji.location == ArtifactLocation.Installed)
                {
                    _artifacts.Cancel();
                    Log(job.instanceId, ji?.definitionId, "research-interrupted", "job-cancelled");
                    actions++;
                }
            }

            if (_puzzles != null) foreach (var g in ArtifactPuzzleService.Groups()) _puzzles.RecheckSolved(g);
            if (actions > 0)
            {
                UnityEngine.Debug.Log($"[Xenoarch] Artifact recovery ({context ?? "check"}): {actions} action(s); ledger {State.log.Count}, awaiting lab pickup {State.pendingAtLab.Count}");
                Changed?.Invoke();
            }
            return actions;
        }

        /// <summary>Moves items held at the lab into the backpack / crates when there is room. Returns how many moved.</summary>
        public int CollectPending()
        {
            int moved = 0;
            for (int i = State.pendingAtLab.Count - 1; i >= 0; i--)
            {
                string id = State.pendingAtLab[i];
                var a = _artifacts.Get(id);
                if (a == null || InAnyContainer(id)) { State.pendingAtLab.RemoveAt(i); continue; }
                string where = Place(a);
                if (where == null) continue;
                State.pendingAtLab.RemoveAt(i);
                moved++;
                Notice?.Invoke($"Lab: {_artifacts.DisplayName(a)} collected into your {where}.");
            }
            if (moved > 0) Changed?.Invoke();
            return moved;
        }

        void Grant(ArtifactInstance a, string reason, string extra = null)
        {
            string name = _artifacts.DisplayName(a);
            string where = Place(a);
            if (where == null)
            {
                if (!State.pendingAtLab.Contains(a.instanceId)) State.pendingAtLab.Add(a.instanceId);
                Log(a.instanceId, a.definitionId, reason, extra != null && extra.StartsWith("bought") ? "buy-back" : extra != null ? "replacement" : "lab-pickup");
                Notice?.Invoke($"Recovery: {name} is safe at the research lab. Make room, then collect it there.");
                return;
            }
            Log(a.instanceId, a.definitionId, reason, extra != null && extra.StartsWith("bought") ? "buy-back" : extra != null ? "replacement" : where);
            Notice?.Invoke($"Recovery: {name} was returned to your {where}" + (extra != null ? $" ({extra})." : "."));
        }

        /// <summary>Puts the instance in the backpack, else the first crate with room. Null if nowhere has room.</summary>
        string Place(ArtifactInstance a)
        {
            if (InAnyContainer(a.instanceId)) return "inventory";
            if (_backpack != null && _backpack.AddInstance(a.definitionId, a.instanceId)) { a.location = ArtifactLocation.Held; return "backpack"; }
            if (_crates != null) foreach (var c in _crates()) if (c != null && c.AddInstance(a.definitionId, a.instanceId)) { a.location = ArtifactLocation.Held; return "ship's artifact crate"; }
            return null;
        }

        void Log(string id, string defId, string reason, string method)
        {
            State.log.Add(new RecoveryRecord { instanceId = id, definitionId = defId, reason = reason, method = method, time = Clock != null ? Clock() : 0f });
            UnityEngine.Debug.Log($"[Xenoarch] Artifact recovery: {defId} ({id}) {reason} -> {method}");
        }
    }
}
