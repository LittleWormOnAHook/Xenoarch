using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Crafting;
using DMI.Xenoarch.Discovery;
using DMI.Xenoarch.Save;
using UnityEngine;

namespace DMI.Xenoarch.Objectives
{
    /// <summary>
    /// Phase 2 M2 expedition tracker. Reads DiscoveryService facts (and live item / recipe counts) and keeps no copy
    /// of chain state. The Waystations chain is the first main objective; optional open nodes are sides (up to 2 on
    /// the HUD). Save section objectives.state holds only UI prefs (hide optional, already-notified ids).
    /// </summary>
    public sealed class ObjectiveService
    {
        public const string Key = "objectives.state";
        public const string WaystationsChain = "chain.moon_mars";
        public const int MaxHudSides = 2;

        readonly DiscoveryService _discovery;
        readonly GameSession _session;
        readonly List<ObjectiveDefinition> _authored = new List<ObjectiveDefinition>();
        readonly HashSet<string> _notified = new HashSet<string>();
        bool _prime = true;

        public ObjectiveState State { get; private set; } = new ObjectiveState();
        public event Action Changed;
        /// <summary>(id, title) when a tracked line or node completes for the first time this save.</summary>
        public event Action<string, string> Advanced;

        public bool HideOptional
        {
            get => State.hideOptional;
            set { if (State.hideOptional == value) return; State.hideOptional = value; EnsureFocus(); Changed?.Invoke(); }
        }

        /// <summary>Stable id of the focused objective. Empty or unknown values resolve to the main expedition.
        /// Changing focus never writes discovery facts or completion state.</summary>
        public string FocusedId
        {
            get
            {
                var main = Main();
                string resolved = ResolveFocusId(State.focusedId, main);
                return string.IsNullOrEmpty(resolved) && main != null ? main.id : resolved;
            }
        }

        public bool IsFocused(string id) => !string.IsNullOrEmpty(id) && id == FocusedId;

        public ObjectiveView Focused()
        {
            string id = FocusedId;
            if (string.IsNullOrEmpty(id)) return Main();
            foreach (var v in All()) if (v != null && v.id == id) return v;
            return Main();
        }

        /// <summary>Select the focused objective. Only the main expedition or a currently listed (visible, incomplete) side is accepted.</summary>
        public void SetFocus(string id)
        {
            var main = Main();
            string resolved = ResolveFocusId(id, main);
            if (string.IsNullOrEmpty(resolved) && main != null) resolved = main.id;
            if (State.focusedId == resolved) return;
            State.focusedId = resolved ?? "";
            Changed?.Invoke();
        }

        void EnsureFocus()
        {
            var main = Main();
            string resolved = ResolveFocusId(State.focusedId, main);
            if (string.IsNullOrEmpty(resolved) && main != null) resolved = main.id;
            if (State.focusedId == resolved) return;
            State.focusedId = resolved ?? "";
        }

        string ResolveFocusId(string want, ObjectiveView main)
        {
            string mainId = main != null ? main.id : null;
            if (string.IsNullOrEmpty(want) || want == mainId) return mainId;
            foreach (var v in Sides()) if (v != null && v.id == want) return want;
            return mainId;
        }

        /// <summary>World target of the focused objective's current incomplete line, if one can be derived. Null if none.</summary>
        public ObjectiveDestination Destination()
        {
            var v = Focused();
            if (v == null) return null;
            foreach (var l in v.lines)
            {
                if (l == null || l.complete) continue;
                var d = DestinationFromFact(l.factKey);
                if (d != null && d.Valid) return d;
            }
            return null;
        }

        public static ObjectiveDestination DestinationFromFact(string factKey)
        {
            if (string.IsNullOrEmpty(factKey)) return null;
            const string siteP = "site.revealed:", bodyP = "body.visited:", chamberP = "chamber.opened:";
            if (factKey.StartsWith(siteP, StringComparison.Ordinal))
                return new ObjectiveDestination { siteId = factKey.Substring(siteP.Length) };
            if (factKey.StartsWith(bodyP, StringComparison.Ordinal))
                return new ObjectiveDestination { bodyId = factKey.Substring(bodyP.Length) };
            if (factKey.StartsWith(chamberP, StringComparison.Ordinal))
            {
                string id = factKey.Substring(chamberP.Length);
                var chambers = DMI.Xenoarch.Excavation.ChamberDefinition.All();
                if (chambers != null)
                    foreach (var ch in chambers)
                        if (ch != null && ch.Id == id && !string.IsNullOrEmpty(ch.hostSiteId))
                            return new ObjectiveDestination { siteId = ch.hostSiteId };
                if (id.StartsWith("chamber.", StringComparison.Ordinal))
                    return new ObjectiveDestination { siteId = "site." + id.Substring("chamber.".Length) };
            }
            return null;
        }

        public ObjectiveService(DiscoveryService discovery, GameSession session, SaveService saves)
        {
            _discovery = discovery;
            _session = session;
            var loaded = Resources.LoadAll<ObjectiveDefinition>("Objectives");
            if (loaded != null) foreach (var o in loaded) if (o != null) _authored.Add(o);
            saves?.Register(new JsonStateSection<ObjectiveState>(Key, 25, () => Capture(), Restore));
            if (_discovery != null) _discovery.Changed += () => Evaluate(true);
            if (_session?.Inventory != null) _session.Inventory.Changed += () => Evaluate(false);
            if (_session?.Holds != null) foreach (var h in _session.Holds.Values) if (h != null) h.Changed += () => Evaluate(false);
            if (_session?.Crafting != null) _session.Crafting.Crafted += _ => Evaluate(false);
        }

        ObjectiveState Capture()
        {
            State.notified.Clear();
            foreach (var id in _notified) State.notified.Add(id);
            return State;
        }

        void Restore(ObjectiveState s)
        {
            State = s ?? new ObjectiveState();
            _notified.Clear();
            if (State.notified != null) foreach (var id in State.notified) if (!string.IsNullOrEmpty(id)) _notified.Add(id);
            _prime = true;
            Changed?.Invoke();
        }

        public void Evaluate(bool notify = true)
        {
            if (_prime) { SeedNotified(); _prime = false; notify = false; }
            if (notify) NotifyNewCompletions();
            Changed?.Invoke();
        }

        void SeedNotified()
        {
            foreach (var v in All())
            {
                foreach (var l in v.lines) if (l.complete && !string.IsNullOrEmpty(l.id)) _notified.Add(l.id);
                if (v.complete && !string.IsNullOrEmpty(v.id)) _notified.Add("done:" + v.id);
            }
        }

        void NotifyNewCompletions()
        {
            foreach (var v in All())
            {
                if (!v.open && !v.complete) continue;
                foreach (var l in v.lines)
                {
                    if (!l.complete || string.IsNullOrEmpty(l.id) || !_notified.Add(l.id)) continue;
                    Advanced?.Invoke(l.id, l.label);
                }
                if (v.complete && !string.IsNullOrEmpty(v.id) && _notified.Add("done:" + v.id))
                    Advanced?.Invoke("done:" + v.id, v.title + " complete");
            }
        }

        public ObjectiveView Main()
        {
            foreach (var v in Build()) if (v.kind == ObjectiveKind.Main) return v;
            return null;
        }

        public List<ObjectiveView> Sides(int max = MaxHudSides)
        {
            var list = new List<ObjectiveView>();
            foreach (var v in Build())
            {
                if (v.kind != ObjectiveKind.Side || !v.open || v.complete) continue;
                if (v.optional && HideOptional) continue;
                list.Add(v);
                if (list.Count >= max) break;
            }
            return list;
        }

        /// <summary>Journal listing: main chain plus every revealed side, including completed and hidden optional ones.</summary>
        public List<ObjectiveView> All() => Build();

        List<ObjectiveView> Build()
        {
            var list = new List<ObjectiveView>();
            var seenChains = new HashSet<string>();
            ObjectiveView main = null;

            foreach (var def in _authored)
            {
                if (def == null || string.IsNullOrEmpty(def.bindChainId)) continue;
                CollectChain(def.bindChainId, def, list, ref main);
                seenChains.Add(def.bindChainId);
            }

            if (main == null)
            {
                var chain = FindChain(WaystationsChain) ?? FirstChain();
                if (chain != null && seenChains.Add(chain.Id))
                    CollectChain(chain.Id, null, list, ref main);
            }
            if (main != null) list.Insert(0, main);

            if (_discovery != null)
            {
                foreach (var chain in _discovery.Chains)
                {
                    if (chain == null || !seenChains.Add(chain.Id)) continue;
                    CollectChain(chain.Id, null, list, ref main);
                }
            }

            foreach (var def in _authored)
            {
                if (def == null || !string.IsNullOrEmpty(def.bindChainId)) continue;
                var v = FromAuthored(def);
                if (v != null) list.Add(v);
            }
            return list;
        }

        void CollectChain(string chainId, ObjectiveDefinition def, List<ObjectiveView> sides, ref ObjectiveView main)
        {
            var chain = FindChain(chainId);
            if (chain == null) return;
            var view = new ObjectiveView
            {
                id = def != null ? def.Id : chain.Id,
                title = def != null ? def.TitleOrId : (string.IsNullOrEmpty(chain.title) ? chain.Id : chain.title),
                kind = def != null ? def.kind : (chain.Id == WaystationsChain || chain == FirstChain() ? ObjectiveKind.Main : ObjectiveKind.Side),
                optional = def != null && def.optional,
                subtitle = def != null ? def.description : null,
            };

            bool anyOpen = false, allRequired = true, anyRequired = false;
            var done = _discovery.Progress(chain.Id).completed;
            foreach (var n in chain.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.id)) continue;
                bool isDone = done.Contains(n.id);
                bool prereq = PrereqsMet(n, done);
                if (!n.optional)
                {
                    anyRequired = true;
                    if (!isDone) allRequired = false;
                }
                if (!isDone && prereq) anyOpen = true;
                if (!prereq && !isDone) continue;
                if (n.optional)
                {
                    sides.Add(OptionalNodeView(chain, n, isDone, prereq));
                    continue;
                }
                view.lines.Add(FactLine(chain.Id + ":" + n.id, n.title, n.factKey, n.threshold, isDone));
            }

            if (def != null) foreach (var extra in AuthoredLines(def)) view.lines.Add(extra);
            view.complete = anyRequired && allRequired;
            view.open = anyOpen || view.complete || view.lines.Count > 0;
            if (view.kind == ObjectiveKind.Main && main == null) main = view;
            else if (view.open) sides.Add(view);
        }

        ObjectiveView OptionalNodeView(DiscoveryChainDefinition chain, DiscoveryNode n, bool isDone, bool open)
        {
            var v = new ObjectiveView
            {
                id = chain.Id + ":" + n.id,
                title = n.title,
                subtitle = string.IsNullOrEmpty(chain.title) ? chain.Id : chain.title,
                kind = ObjectiveKind.Side,
                optional = true,
                complete = isDone,
                open = open || isDone,
            };
            v.lines.Add(FactLine(v.id, n.title, n.factKey, n.threshold, isDone));
            return v;
        }

        ObjectiveView FromAuthored(ObjectiveDefinition def)
        {
            if (!ReqsMet(def.requireFacts)) return null;
            var v = new ObjectiveView
            {
                id = def.Id,
                title = def.TitleOrId,
                subtitle = def.description,
                kind = def.kind,
                optional = def.optional,
            };
            bool all = true, any = false;
            foreach (var line in AuthoredLines(def))
            {
                v.lines.Add(line);
                any = true;
                if (!line.complete) all = false;
            }
            v.complete = any && all;
            v.open = any;
            return v.open ? v : null;
        }

        IEnumerable<ObjectiveLineView> AuthoredLines(ObjectiveDefinition def)
        {
            if (def.lines == null) yield break;
            int i = 0;
            foreach (var line in def.lines)
            {
                if (line == null) continue;
                i++;
                yield return EvaluateLine(def.Id + ":" + i, line);
            }
        }

        ObjectiveLineView EvaluateLine(string id, ObjectiveLine line)
        {
            var v = new ObjectiveLineView { id = id, label = line.label, prompt = line.prompt };
            switch (line.kind)
            {
                case ObjectiveLineKind.ItemCount:
                    int have = _session?.Stock != null && !string.IsNullOrEmpty(line.itemId) ? _session.Stock.Count(line.itemId) : 0;
                    v.complete = have >= line.itemCount;
                    v.progress = have + " / " + line.itemCount;
                    if (string.IsNullOrEmpty(v.label)) v.label = line.itemId;
                    if (string.IsNullOrEmpty(v.prompt) && !v.complete) v.prompt = "Gather the required materials.";
                    break;
                case ObjectiveLineKind.Recipe:
                    var recipe = FindRecipe(line.recipeId);
                    v.complete = recipe != null && _session?.Crafting != null && _session.Crafting.Known(recipe);
                    if (string.IsNullOrEmpty(v.label)) v.label = recipe != null ? recipe.Title : line.recipeId;
                    if (string.IsNullOrEmpty(v.prompt) && !v.complete) v.prompt = "Discover the recipe.";
                    break;
                default:
                    float value = _discovery != null ? _discovery.Value(line.factKey) : 0f;
                    v.complete = _discovery != null && _discovery.Has(line.factKey, line.factThreshold);
                    v.progress = FormatProgress(value, line.factThreshold);
                    v.factKey = line.factKey;
                    if (string.IsNullOrEmpty(v.label)) v.label = line.factKey;
                    if (string.IsNullOrEmpty(v.prompt) && !v.complete) v.prompt = ContextualPrompt.For(line.factKey, value, line.factThreshold, Context());
                    break;
            }
            return v;
        }

        ObjectiveLineView FactLine(string id, string title, string factKey, float threshold, bool isDone)
        {
            float value = _discovery != null ? _discovery.Value(factKey) : 0f;
            bool done = isDone || (_discovery != null && _discovery.Has(factKey, threshold));
            return new ObjectiveLineView
            {
                id = id,
                label = title,
                complete = done,
                progress = FormatProgress(value, threshold),
                prompt = done ? null : ContextualPrompt.For(factKey, value, threshold, Context()),
                factKey = factKey,
            };
        }

        CraftRecipe FindRecipe(string id)
        {
            if (string.IsNullOrEmpty(id) || _session?.Crafting == null) return null;
            foreach (var r in _session.Crafting.Recipes) if (r != null && r.Id == id) return r;
            return null;
        }

        bool ReqsMet(List<string> facts)
        {
            if (facts == null || facts.Count == 0) return true;
            if (_discovery == null) return false;
            foreach (var f in facts) if (!string.IsNullOrEmpty(f) && !_discovery.Has(f)) return false;
            return true;
        }

        DiscoveryChainDefinition FindChain(string id)
        {
            if (_discovery == null || string.IsNullOrEmpty(id)) return null;
            foreach (var c in _discovery.Chains) if (c != null && c.Id == id) return c;
            return null;
        }

        DiscoveryChainDefinition FirstChain()
        {
            if (_discovery == null) return null;
            foreach (var c in _discovery.Chains) if (c != null) return c;
            return null;
        }

        static bool PrereqsMet(DiscoveryNode n, List<string> done)
        {
            if (n.prerequisites == null || n.prerequisites.Count == 0) return true;
            int have = 0; foreach (var id in n.prerequisites) if (done.Contains(id)) have++;
            return n.mode switch { PrereqMode.Any => have > 0, PrereqMode.Count => have >= n.count, _ => have == n.prerequisites.Count };
        }

        static string FormatProgress(float have, float threshold)
        {
            if (threshold <= 0f || Mathf.Approximately(threshold, 1f)) return null;
            if (threshold <= 1f) return $"{Mathf.Clamp01(have):P0} / {threshold:P0}";
            return $"{have:0} / {threshold:0}";
        }

        (bool aboard, bool surface) Context()
        {
            var scene = _session?.Location?.sceneName;
            bool aboard = _session != null && _session.AboardShip;
            bool surface = scene == SceneNames.MoonExcavation01 || scene == SceneNames.MarsExcavation01 || scene == SceneNames.ProceduralSurface;
            return (aboard, surface);
        }
    }

    /// <summary>Short hint for the current objective line, based on the fact key and where the player is.</summary>
    public static class ContextualPrompt
    {
        public static string For(string factKey, float value, float threshold, (bool aboard, bool surface) ctx)
        {
            if (string.IsNullOrEmpty(factKey)) return null;
            if (factKey.StartsWith("artifact.recovered:", StringComparison.Ordinal))
                return ctx.surface ? "Search the excavation site with your scanner and dig tools." : "Land at the site and recover the find.";
            if (factKey.StartsWith("civ.identified:", StringComparison.Ordinal) ||
                factKey.StartsWith("translation:", StringComparison.Ordinal) ||
                factKey.StartsWith("research:", StringComparison.Ordinal))
                return ctx.aboard ? "Use the research lab on the ship." : "Return to the ship and use the research lab.";
            if (factKey.StartsWith("site.revealed:", StringComparison.Ordinal))
            {
                if (value < Mathf.Min(threshold, 2f) - 0.5f)
                    return ctx.aboard ? "Scan the body from orbit at the orbital scanner." : "Return to the ship and scan from orbit.";
                return ctx.surface ? "Pinpoint the site with the handheld scanner." : "Land and pinpoint the site with the handheld scanner.";
            }
            if (factKey.StartsWith("tech.installed:", StringComparison.Ordinal))
                return ctx.aboard ? "Install the device at the research lab." : "Return to the ship and install the device at the research lab.";
            if (factKey.StartsWith("chamber.opened:", StringComparison.Ordinal))
                return ctx.surface ? "Search the site for a buried chamber." : "Land at the site and search for a buried chamber.";
            if (factKey.StartsWith("body.visited:", StringComparison.Ordinal))
                return "Plot a course at the Nav terminal and fly it from the helm.";
            if (factKey.StartsWith("nav.system:", StringComparison.Ordinal) || factKey == "tech.navdrive")
                return ctx.aboard ? "Use the Nav terminal to travel." : "Return to the ship to travel.";
            return null;
        }
    }
}