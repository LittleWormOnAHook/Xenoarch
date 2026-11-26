using System;
using System.Collections.Generic;
using DMI.Xenoarch.Discovery;
using DMI.Xenoarch.Items;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Artifacts
{
    public enum ArtifactLocation { Held, Stored, Sold, Installed }

    [Serializable]
    public sealed class ArtifactInstance
    {
        public string instanceId;
        public string definitionId;
        [Range(0f, 1f)] public float condition = 1f;
        public string siteId;
        public string bodyId;
        public float discoveredAt;
        public List<string> opsDone = new List<string>();
        [Range(0f, 1f)] public float translation;
        public int translationRuns;
        public ArtifactLocation location = ArtifactLocation.Held;

        public bool Done(ResearchOp op) => opsDone.Contains(op.ToString());
        public bool Identified => Done(ResearchOp.Identification);
    }

    [Serializable] public sealed class CivilizationKnowledge { public string civId; public bool identified; [Range(0f, 1f)] public float language; }

    [Serializable]
    public sealed class ResearchJob
    {
        public string instanceId;
        public ResearchOp op;
        public float elapsed;
        public float duration;
    }

    [Serializable]
    public sealed class ArtifactState
    {
        public List<ArtifactInstance> instances = new List<ArtifactInstance>();
        public List<CivilizationKnowledge> civilizations = new List<CivilizationKnowledge>();
        public List<string> uniquesFound = new List<string>();
        public bool hasJob;
        public ResearchJob job = new ResearchJob();
    }

    /// <summary>
    /// Artifact instances, civilization knowledge and the ship lab's research queue (one job at a time, Mk I lab).
    /// Research runs on play time wherever the player is. Results are published as discovery facts.
    /// </summary>
    public sealed class ArtifactService
    {
        public const string Key = "artifacts.instances";
        readonly IItemLookup _items;
        readonly List<CivilizationDefinition> _civs = new List<CivilizationDefinition>();
        readonly DiscoveryService _discovery;
        public ArtifactState State { get; private set; } = new ArtifactState();
        public float TranslationCap = 0.6f;
        /// <summary>Lab reagent payment (wired to the materials loop by GameSession). Null = free (tests, tools).</summary>
        public Func<int, bool> HasReagents, PayReagents;
        public Func<float> Clock = () => 0f;
        public event Action Changed;
        public event Action<ArtifactInstance, ResearchOp, string> ResearchCompleted;

        public ArtifactService(IItemLookup items, IEnumerable<CivilizationDefinition> civs, DiscoveryService discovery, SaveService saves)
        {
            _items = items; _discovery = discovery;
            if (civs != null) foreach (var c in civs) if (c != null) _civs.Add(c);
            saves?.Register(new JsonStateSection<ArtifactState>(Key, 15, () => State, s => { State = s ?? new ArtifactState(); Changed?.Invoke(); }));
        }

        // ------------------------------------------------------------------ queries
        public ArtifactDefinition Definition(string id) => _items != null && _items.TryGetItem(id, out var d) ? d as ArtifactDefinition : null;
        public ArtifactDefinition Definition(ArtifactInstance a) => a != null ? Definition(a.definitionId) : null;
        public ArtifactInstance Get(string instanceId) { foreach (var a in State.instances) if (a.instanceId == instanceId) return a; return null; }
        public bool UniqueFound(string defId) => State.uniquesFound.Contains(defId);

        public CivilizationDefinition Civilization(string civId) { foreach (var c in _civs) if (c.Id == civId) return c; return null; }

        public CivilizationKnowledge Knowledge(string civId)
        {
            foreach (var k in State.civilizations) if (k.civId == civId) return k;
            var n = new CivilizationKnowledge { civId = civId };
            State.civilizations.Add(n);
            return n;
        }

        public string DisplayName(ArtifactInstance a)
        {
            var d = Definition(a);
            if (d == null) return a?.definitionId ?? "?";
            return a.Identified ? d.DisplayNameOrId : d.unidentifiedName;
        }

        public float Value(ArtifactInstance a)
        {
            var d = Definition(a);
            if (d == null) return 0f;
            float research = 0.05f * a.opsDone.Count + 0.5f * a.translation;
            float sig = 1f + 0.5f * d.historicalSignificance + 0.75f * d.technologicalSignificance;
            return d.baseValue * ArtifactDefinition.RarityMultiplier(d.rarity) * Mathf.Max(0.05f, a.condition) * (1f + research) * sig;
        }

        // ------------------------------------------------------------------ recovery
        /// <summary>Creates the instance record for a recovered artifact (caller adds it to the inventory).</summary>
        public ArtifactInstance Recover(ArtifactDefinition def, float condition, string siteId, string bodyId)
        {
            if (def == null) return null;
            var a = new ArtifactInstance
            {
                instanceId = Guid.NewGuid().ToString("N"), definitionId = def.Id, condition = Mathf.Clamp01(condition),
                siteId = siteId, bodyId = bodyId, discoveredAt = Clock(),
            };
            State.instances.Add(a);
            if (def.rarity == ArtifactRarity.Unique && !State.uniquesFound.Contains(def.Id)) State.uniquesFound.Add(def.Id);
            _discovery?.Publish(Facts.Recovered(def.Id));
            // Collections: distinct members found so far (facts keep the max, so duplicates never count twice).
            if (!string.IsNullOrEmpty(def.setId)) _discovery?.Publish(Facts.SetProgress(def.setId), DistinctFound(d => d.setId == def.setId));
            if (!string.IsNullOrEmpty(def.fragmentOf)) _discovery?.Publish(Facts.Fragments(def.fragmentOf), DistinctFound(d => d.fragmentOf == def.fragmentOf));
            Changed?.Invoke();
            return a;
        }

        /// <summary>Distinct artifact definitions recovered that match <paramref name="match"/>.</summary>
        public int DistinctFound(Func<ArtifactDefinition, bool> match)
        {
            var seen = new HashSet<string>();
            foreach (var i in State.instances)
            {
                if (i == null || seen.Contains(i.definitionId)) continue;
                var d = Definition(i);
                if (d != null && match(d)) seen.Add(i.definitionId);
            }
            return seen.Count;
        }

        // ------------------------------------------------------------------ research
        public bool Busy => State.hasJob;
        public ResearchJob Job => State.hasJob ? State.job : null;

        public bool CanStart(ArtifactInstance a, ResearchOp op, out string reason)
        {
            reason = null;
            var d = Definition(a);
            if (d == null) { reason = "Unknown artifact."; return false; }
            if (Busy) { reason = "Lab busy."; return false; }
            if (!d.researchOps.Contains(op) && !(op == ResearchOp.Translation && d.HasTranslation)) { reason = "Not applicable."; return false; }
            if (op == ResearchOp.Translation && !d.HasTranslation) { reason = "No text to translate."; return false; }
            if (!ResearchRules.Repeatable(op) && a.Done(op)) { reason = "Done."; return false; }
            var pre = ResearchRules.Prerequisite(op);
            if (pre.HasValue && !a.Done(pre.Value)) { reason = $"Needs {ResearchRules.Label(pre.Value)}."; return false; }
            if (op == ResearchOp.Translation && a.translation >= TranslationCap - 1e-3f) { reason = $"Lab limit reached ({TranslationCap:P0}). Needs a better translator."; return false; }
            if (op == ResearchOp.Restoration && a.condition >= 0.999f) { reason = "Already pristine."; return false; }
            int cost = ResearchRules.ReagentCost(op);
            if (cost > 0 && HasReagents != null && !HasReagents(cost)) { reason = $"Needs {cost} lab reagent{(cost == 1 ? "" : "s")} (refine some at the refiner)."; return false; }
            return true;
        }

        public bool Start(ArtifactInstance a, ResearchOp op, out string reason)
        {
            if (!CanStart(a, op, out reason)) return false;
            int cost = ResearchRules.ReagentCost(op);
            if (cost > 0 && PayReagents != null && !PayReagents(cost)) { reason = "Not enough lab reagents."; return false; }
            State.hasJob = true;
            State.job = new ResearchJob { instanceId = a.instanceId, op = op, elapsed = 0f, duration = ResearchRules.Duration(op) };
            Changed?.Invoke();
            return true;
        }

        public void Cancel() { State.hasJob = false; Changed?.Invoke(); }

        /// <summary>Advance the lab job; completes it (and applies effects) when its duration is reached.</summary>
        public void Tick(float dt)
        {
            if (!State.hasJob || dt <= 0f) return;
            var j = State.job;
            j.elapsed += dt;
            if (j.elapsed < j.duration) return;
            State.hasJob = false;
            var a = Get(j.instanceId);
            if (a == null) { Changed?.Invoke(); return; }
            string result = Complete(a, j.op);
            ResearchCompleted?.Invoke(a, j.op, result);
            Changed?.Invoke();
        }

        /// <summary>Applies an operation's effects immediately (used by Tick; also by tests).</summary>
        public string Complete(ArtifactInstance a, ResearchOp op)
        {
            var d = Definition(a);
            if (d == null) return "";
            if (!ResearchRules.Repeatable(op) && !a.Done(op)) a.opsDone.Add(op.ToString());
            string result;
            switch (op)
            {
                case ResearchOp.Cleaning: a.condition = Mathf.Min(1f, a.condition + 0.02f); result = "Surface cleaned."; break;
                case ResearchOp.Identification: result = $"Identified: {d.DisplayNameOrId} ({d.artifactCategory}, {d.rarity})."; break;
                case ResearchOp.MaterialAnalysis: result = $"Material: {d.material}."; break;
                case ResearchOp.AgeEstimation: result = $"Age: {d.ageEstimate}."; break;
                case ResearchOp.CivilizationIdentification:
                {
                    var civ = Civilization(d.civilizationId);
                    var k = Knowledge(d.civilizationId); k.identified = true;
                    result = civ != null ? $"Origin: {civ.displayName}. Not of Earth." : "Origin unknown.";
                    _discovery?.Publish(Facts.Civilization(d.civilizationId));
                    break;
                }
                case ResearchOp.Restoration: a.condition = Mathf.Min(1f, a.condition + 0.2f); result = $"Restored to {a.condition:P0}."; break;
                case ResearchOp.Translation:
                {
                    var k = Knowledge(d.civilizationId);
                    float before = a.translation;
                    a.translation = Mathf.Min(TranslationCap, a.translation + ResearchRules.TranslationStep(k.language, a.condition));
                    a.translationRuns++;
                    k.language = Mathf.Min(1f, k.language + 0.15f);
                    if (!a.Done(op)) a.opsDone.Add(op.ToString());
                    result = $"Translation {before:P0} -> {a.translation:P0}.";
                    _discovery?.Publish(Facts.Translation(d.Id), a.translation);
                    break;
                }
                default: result = "Technology analysed."; break;
            }
            _discovery?.Publish(Facts.Research(d.Id, op));
            return result;
        }

        /// <summary>Translation segments revealed so far.</summary>
        public IEnumerable<TranslationSegment> RevealedText(ArtifactInstance a)
        {
            var d = Definition(a);
            if (d == null || !d.HasTranslation) yield break;
            foreach (var s in d.translation) if (s != null && a.translation >= s.threshold - 1e-4f) yield return s;
        }
    }
}
