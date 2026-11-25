using UnityEngine;

namespace DMI.Xenoarch.Core
{
    /// <summary>
    /// Base for all static, data-driven definitions (items, later celestial bodies, sites, artifacts, civilizations...).
    /// Definitions are immutable at runtime; player/world state references them only by <see cref="Id"/>.
    /// </summary>
    public abstract class DefinitionAsset : ScriptableObject, IHasStableId
    {
        [Tooltip("Stable, authored id. Never rename after content ships: saves reference it.")]
        [SerializeField] private string id;

        public string Id => id;

        /// <summary>Prefix used when generating a default id (e.g. "item.").</summary>
        protected virtual string DefaultIdPrefix => "def.";

        /// <summary>Editor/tooling hook (asset builders, tests). Not for gameplay code.</summary>
        public void SetIdForTooling(string value) => id = value;

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = DefaultIdPrefix + StableId.Slug(name);
        }
#endif
    }
}
