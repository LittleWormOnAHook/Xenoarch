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

        public StarSystemDefinition FindSystem(string id)
        {
            foreach (var s in systems) if (s != null && s.Id == id) return s;
            return null;
        }

        public CelestialBodyDefinition FindBody(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in systems) { var b = s != null ? s.FindBody(id) : null; if (b != null) return b; }
            return null;
        }

        public StarSystemDefinition SystemOf(string bodyId)
        {
            foreach (var s in systems) if (s != null && s.FindBody(bodyId) != null) return s;
            return null;
        }

        public CelestialBodyDefinition FindBodyByScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            foreach (var s in systems) if (s != null) foreach (var b in s.bodies) if (b != null && b.sceneName == sceneName) return b;
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
