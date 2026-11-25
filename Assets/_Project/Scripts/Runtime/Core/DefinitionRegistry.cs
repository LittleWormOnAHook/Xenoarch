using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Core
{
    /// <summary>
    /// Id -> definition lookup for one definition type. One registry asset per type keeps content additive:
    /// new content = new asset + registry entry, no code changes.
    /// </summary>
    public abstract class DefinitionRegistry<T> : ScriptableObject where T : DefinitionAsset
    {
        [SerializeField] private List<T> entries = new List<T>();
        private Dictionary<string, T> _map;

        public IReadOnlyList<T> Entries => entries;

        private void OnEnable() => _map = null;

        public bool TryGet(string id, out T definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(id)) return false;
            EnsureMap();
            return _map.TryGetValue(id, out definition) && definition != null;
        }

        public T Get(string id) => TryGet(id, out var d) ? d : null;

        /// <summary>Tooling hook: replace the entry list (asset builders, tests).</summary>
        public void SetEntries(IEnumerable<T> items)
        {
            entries = new List<T>(items);
            _map = null;
        }

        /// <summary>Returns human-readable problems: missing entries, invalid or duplicate ids.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var seen = new HashSet<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) { problems.Add($"{name}: entry {i} is null"); continue; }
                if (!StableId.IsValidDefinitionId(e.Id)) problems.Add($"{name}: '{e.name}' has invalid id '{e.Id}'");
                else if (!seen.Add(e.Id)) problems.Add($"{name}: duplicate id '{e.Id}'");
            }
            return problems;
        }

        private void EnsureMap()
        {
            if (_map != null) return;
            _map = new Dictionary<string, T>();
            foreach (var e in entries)
                if (e != null && !string.IsNullOrEmpty(e.Id) && !_map.ContainsKey(e.Id)) _map.Add(e.Id, e);
        }
    }
}
