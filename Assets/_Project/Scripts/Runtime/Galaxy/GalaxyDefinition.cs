using System.Collections.Generic;
using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>Root of the galaxy data. Handcrafted systems are listed; generated ones derive ids from (parent, seed, index).</summary>
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Galaxy", fileName = "Galaxy")]
    public sealed class GalaxyDefinition : DefinitionAsset
    {
        protected override string DefaultIdPrefix => "galaxy.";
        public int seed = 1337;
        public List<StarSystemDefinition> systems = new List<StarSystemDefinition>();
        /// <summary>t186u: systems generated at runtime from the galaxy seed (<see cref="ProceduralGalaxy"/>). Never serialized.</summary>
        [System.NonSerialized] public List<StarSystemDefinition> generatedSystems = new List<StarSystemDefinition>();
        /// <summary>Authored systems, then generated ones.</summary>
        public IEnumerable<StarSystemDefinition> AllSystems
        {
            get
            {
                if (systems != null) foreach (var s in systems) if (s != null) yield return s;
                if (generatedSystems != null) foreach (var s in generatedSystems) if (s != null) yield return s;
            }
        }
        /// <summary>t186u: several generated planets share one landing scene; scene lookups prefer this body (the ship's current body).</summary>
        public static System.Func<string> BodyHint;

        public StarSystemDefinition FindSystem(string id)
        {
            foreach (var s in AllSystems) if (s.Id == id) return s;
            return null;
        }

        public CelestialBodyDefinition FindBody(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in AllSystems) { var b = s.FindBody(id); if (b != null) return b; }
            return null;
        }

        public StarSystemDefinition SystemOf(string bodyId)
        {
            foreach (var s in AllSystems) if (s.FindBody(bodyId) != null) return s;
            return null;
        }

        public CelestialBodyDefinition FindBodyByScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            string hint = BodyHint != null ? BodyHint() : null;
            if (!string.IsNullOrEmpty(hint)) { var hb = FindBody(hint); if (hb != null && hb.sceneName == sceneName) return hb; }
            foreach (var s in AllSystems) foreach (var b in s.AllBodies) if (b.sceneName == sceneName) return b;
            return null;
        }

        /// <summary>Deterministic id for generated content: same (parent, galaxy seed, index) gives the same id every session.</summary>
        public static string GeneratedId(string prefix, string parentId, int galaxySeed, int index)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in parentId ?? "") h = (h ^ ch) * 16777619;
                h = (h ^ (uint)galaxySeed) * 16777619;
                h = (h ^ (uint)index) * 16777619;
                return $"{prefix}{h:x8}";
            }
        }
    }
}
