using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Discovery
{
    public enum PrereqMode { All, Any, Count }

    /// <summary>
    /// A chain node completes when its fact reaches the threshold and its prerequisites hold. Node "types" from the
    /// design doc are fact keys (see <see cref="Facts"/>): artifact recovered, research op done, translation, civ
    /// identified, site revealed, body visited, plain flags set by rewards.
    /// </summary>
    [Serializable]
    public sealed class DiscoveryNode
    {
        public string id;
        public string title;
        public string factKey;
        public float threshold = 1f;
        public List<string> prerequisites = new List<string>();
        public PrereqMode mode = PrereqMode.All;
        [Min(1)] public int count = 1;
        public bool optional;
        public DiscoveryReward reward = new DiscoveryReward();
    }

    [Serializable]
    public sealed class DiscoveryReward
    {
        [TextArea(2, 4)] public string journal;
        public string setFact;
        public string revealSiteId;
        public SiteRevealLevel revealLevel = SiteRevealLevel.OrbitalAnomaly;
        public string discoverBodyId;
        public int scannerLevelBonus;
        [Tooltip("Optional: star system unlocked in the Nav dropdown, e.g. \"sys.proxima_centauri\" (publishes nav.system.<id>).")]
        public string unlockSystemId;
        [Tooltip("Optional: more dig sites revealed by decoded coordinates (same reveal level as above).")]
        public List<string> revealSiteIds = new List<string>();
    }

    /// <summary>Fact key builders (one place, so producers and chain data agree).</summary>
    public static class Facts
    {
        public static string Recovered(string artifactId) => "artifact.recovered:" + artifactId;
        public static string Research(string artifactId, Artifacts.ResearchOp op) => $"research:{artifactId}:{op}";
        public static string Translation(string artifactId) => "translation:" + artifactId;
        public static string Civilization(string civId) => "civ.identified:" + civId;
        public static string Site(string siteId) => "site.revealed:" + siteId;
        public static string Visited(string bodyId) => "body.visited:" + bodyId;
        /// <summary>Distinct members of a collectible artifact set recovered (value = count).</summary>
        public static string SetProgress(string setId) => "artifact.set:" + setId;
        /// <summary>Distinct pieces of a fragment group recovered (value = count).</summary>
        public static string Fragments(string groupId) => "artifact.fragments:" + groupId;
        /// <summary>Unlocks a star system in the Nav terminal's system dropdown (same key as ExplorationService.NavUnlockFact).</summary>
        public static string NavSystem(string systemId) => ExplorationService.NavUnlockFact(systemId);
    }

    [Serializable] public sealed class FactEntry { public string key; public float value; }
    [Serializable] public sealed class ChainProgress { public string chainId; public List<string> completed = new List<string>(); public float completedAt = -1f; }
    [Serializable] public sealed class JournalEntry { public float time; public string chainId; public string nodeId; public string text; }

    [Serializable]
    public sealed class DiscoveryState
    {
        public List<FactEntry> facts = new List<FactEntry>();
        public List<ChainProgress> chains = new List<ChainProgress>();
        public List<JournalEntry> journal = new List<JournalEntry>();
    }

    /// <summary>
    /// Event-driven chain evaluator. Systems publish idempotent facts (max value kept); every publish re-evaluates the
    /// chains until stable and applies rewards once. State never resets on reload (section "discovery.chains").
    /// </summary>
    public sealed class DiscoveryService
    {
        public const string Key = "discovery.chains";
        readonly List<DiscoveryChainDefinition> _chains;
        readonly ExplorationService _exploration;
        readonly Dictionary<string, float> _facts = new Dictionary<string, float>();
        public DiscoveryState State { get; private set; } = new DiscoveryState();
        public Func<float> Clock = () => 0f;
        public event Action<JournalEntry> JournalAdded;
        public event Action Changed;
        bool _evaluating;

        public IReadOnlyList<DiscoveryChainDefinition> Chains => _chains;

        public DiscoveryService(IEnumerable<DiscoveryChainDefinition> chains, ExplorationService exploration, SaveService saves)
        {
            _chains = new List<DiscoveryChainDefinition>();
            if (chains != null) foreach (var c in chains) if (c != null) _chains.Add(c);
            _exploration = exploration;
            saves?.Register(new JsonStateSection<DiscoveryState>(Key, 20, () => Capture(), Restore));
        }

        DiscoveryState Capture()
        {
            State.facts.Clear();
            foreach (var kv in _facts) State.facts.Add(new FactEntry { key = kv.Key, value = kv.Value });
            return State;
        }

        void Restore(DiscoveryState s)
        {
            State = s ?? new DiscoveryState();
            _facts.Clear();
            foreach (var f in State.facts) if (f != null && !string.IsNullOrEmpty(f.key)) _facts[f.key] = f.value;
            Changed?.Invoke();
        }

        public float Value(string key) => key != null && _facts.TryGetValue(key, out var v) ? v : 0f;
        public bool Has(string key, float threshold = 1f) => Value(key) >= threshold - 1e-4f;

        /// <summary>Record a fact (keeps the max). Returns true if it changed anything.</summary>
        public bool Publish(string key, float value = 1f)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (_facts.TryGetValue(key, out var old) && old >= value) return false;
            _facts[key] = value;
            Evaluate();
            Changed?.Invoke();
            return true;
        }

        public ChainProgress Progress(string chainId)
        {
            foreach (var c in State.chains) if (c.chainId == chainId) return c;
            var p = new ChainProgress { chainId = chainId };
            State.chains.Add(p);
            return p;
        }

        public bool IsComplete(string chainId, string nodeId) => Progress(chainId).completed.Contains(nodeId);

        public void Evaluate()
        {
            if (_evaluating) return; // rewards publish facts; the outer loop picks them up
            _evaluating = true;
            try
            {
                bool any = true; int guard = 0;
                while (any && guard++ < 32)
                {
                    any = false;
                    foreach (var chain in _chains)
                    {
                        var prog = Progress(chain.Id);
                        foreach (var n in chain.nodes)
                        {
                            if (n == null || string.IsNullOrEmpty(n.id) || prog.completed.Contains(n.id)) continue;
                            if (!PrereqsMet(n, prog) || !Has(n.factKey, n.threshold)) continue;
                            prog.completed.Add(n.id);
                            Apply(chain, n);
                            any = true;
                        }
                        if (prog.completedAt < 0f && AllRequiredDone(chain, prog)) prog.completedAt = Clock();
                    }
                }
            }
            finally { _evaluating = false; }
        }

        static bool PrereqsMet(DiscoveryNode n, ChainProgress p)
        {
            if (n.prerequisites == null || n.prerequisites.Count == 0) return true;
            int done = 0; foreach (var id in n.prerequisites) if (p.completed.Contains(id)) done++;
            return n.mode switch { PrereqMode.Any => done > 0, PrereqMode.Count => done >= n.count, _ => done == n.prerequisites.Count };
        }

        static bool AllRequiredDone(DiscoveryChainDefinition c, ChainProgress p)
        {
            foreach (var n in c.nodes) if (n != null && !n.optional && !p.completed.Contains(n.id)) return false;
            return c.nodes.Count > 0;
        }

        void Apply(DiscoveryChainDefinition chain, DiscoveryNode n)
        {
            var r = n.reward;
            Debug.Log($"[Xenoarch] Discovery: {chain.Id}/{n.id} complete ({n.title})");
            if (r == null) return;
            if (!string.IsNullOrEmpty(r.setFact)) { _facts[r.setFact] = Mathf.Max(Value(r.setFact), 1f); }
            if (_exploration != null)
            {
                if (!string.IsNullOrEmpty(r.discoverBodyId)) _exploration.Body(r.discoverBodyId).discovered = true;
                if (!string.IsNullOrEmpty(r.revealSiteId)) _exploration.Reveal(r.revealSiteId, (int)r.revealLevel);
                if (r.scannerLevelBonus != 0) _exploration.Ship.scannerLevel += r.scannerLevelBonus;
                if (r.revealSiteIds != null) foreach (var sid in r.revealSiteIds) if (!string.IsNullOrEmpty(sid)) _exploration.Reveal(sid, (int)r.revealLevel);
            }
            if (!string.IsNullOrEmpty(r.unlockSystemId)) { string k = Facts.NavSystem(r.unlockSystemId); _facts[k] = Mathf.Max(Value(k), 1f); }
            if (!string.IsNullOrEmpty(r.journal))
            {
                var e = new JournalEntry { time = Clock(), chainId = chain.Id, nodeId = n.id, text = r.journal };
                State.journal.Add(e);
                JournalAdded?.Invoke(e);
            }
        }
    }
}
