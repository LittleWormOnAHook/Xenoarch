using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// Maps a generated body's id to a baked landing scene name (Surface_*). Written by the Editor bake command;
    /// read by <see cref="ProceduralGalaxy"/> so travel loads the saved scene instead of Surface_Procedural.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Baked Planet Registry", fileName = "BakedPlanetRegistry")]
    public sealed class BakedPlanetRegistry : ScriptableObject
    {
        public const string ResourcePath = "Galaxy/BakedPlanetRegistry";

        [Serializable]
        public sealed class Entry
        {
            public string bodyId;
            public string sceneName;
            public string scenePath;
        }

        public List<Entry> entries = new List<Entry>();

        static BakedPlanetRegistry _loaded;
        public static BakedPlanetRegistry Load()
        {
            if (_loaded == null) _loaded = Resources.Load<BakedPlanetRegistry>(ResourcePath);
            return _loaded;
        }

        /// <summary>Clears the cached asset so the next Load picks up Editor changes.</summary>
        public static void UnloadCache() => _loaded = null;

        public static bool TryGetScene(string bodyId, out string sceneName, out string scenePath)
        {
            sceneName = null; scenePath = null;
            var reg = Load();
            if (reg == null || string.IsNullOrEmpty(bodyId) || reg.entries == null) return false;
            foreach (var e in reg.entries)
                if (e != null && e.bodyId == bodyId && !string.IsNullOrEmpty(e.sceneName))
                {
                    sceneName = e.sceneName; scenePath = e.scenePath; return true;
                }
            return false;
        }

        public static bool TryGetScene(string bodyId, out string sceneName)
        {
            return TryGetScene(bodyId, out sceneName, out _);
        }
    }
}