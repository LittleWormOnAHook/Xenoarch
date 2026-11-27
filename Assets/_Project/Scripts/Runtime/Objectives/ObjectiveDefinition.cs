using System;
using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Objectives
{
    public enum ObjectiveKind { Main, Side }
    public enum ObjectiveLineKind { Fact, ItemCount, Recipe }

    /// <summary>One tracked requirement: a discovery fact, an item count (backpack + crates), or a known recipe.</summary>
    [Serializable]
    public sealed class ObjectiveLine
    {
        public string label;
        public ObjectiveLineKind kind;
        public string factKey;
        public float factThreshold = 1f;
        public string itemId;
        [Min(1)] public int itemCount = 1;
        public string recipeId;
        [TextArea(1, 3)] public string prompt;
    }

    /// <summary>
    /// Authored objective (Resources/Objectives). Optional: bind a discovery chain so fact lines are read live from
    /// DiscoveryService (no copy of chain state). Extra item/recipe lines are allowed.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Objectives/Objective", fileName = "Objective_")]
    public sealed class ObjectiveDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "objective.";
        public string title;
        [TextArea(2, 4)] public string description;
        public ObjectiveKind kind = ObjectiveKind.Side;
        public bool optional;
        [Tooltip("If set, fact lines come from this discovery chain (e.g. chain.moon_mars). Chain progress is never stored here.")]
        public string bindChainId;
        [Tooltip("All of these facts must be known before the objective becomes active (empty = active when its first line is open).")]
        public List<string> requireFacts = new List<string>();
        public List<ObjectiveLine> lines = new List<ObjectiveLine>();
        public string TitleOrId => string.IsNullOrEmpty(title) ? Id : title;
    }

    [Serializable]
    public sealed class ObjectiveState
    {
        public bool hideOptional;
        public List<string> notified = new List<string>();
    }

    public sealed class ObjectiveLineView
    {
        public string id, label, progress, prompt;
        public bool complete;
    }

    public sealed class ObjectiveView
    {
        public string id, title, subtitle;
        public ObjectiveKind kind;
        public bool optional, complete, open;
        public readonly List<ObjectiveLineView> lines = new List<ObjectiveLineView>();
        public string Prompt
        {
            get
            {
                foreach (var l in lines) if (!l.complete && !string.IsNullOrEmpty(l.prompt)) return l.prompt;
                return null;
            }
        }
    }
}