using System;
using System.Collections.Generic;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Galaxy;
using UnityEngine;

namespace DMI.Xenoarch.World
{
    /// <summary>
    /// Per-planet-type landing surface: TerrainLayers (Moon/Mars PBR reused where they fit), scatter, and dig styles.
    /// Loaded from Resources/World/PlanetSurfaceCatalog. Missing layers fall back to generated matte colour noise.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/World/Planet Surface Catalog", fileName = "PlanetSurfaceCatalog")]
    public sealed class PlanetSurfaceCatalog : ScriptableObject
    {
        public const string ResourcePath = "World/PlanetSurfaceCatalog";

        [Serializable]
        public sealed class Entry
        {
            public ProceduralPlanetType type;
            [Tooltip("Terrain layers, top to bottom of the list = indices 0..n (regolith, soil, rock, optional crust/ice).")]
            public TerrainLayer[] layers;
            [Tooltip("When true, a generated matte ice sheet is appended if the layer list has no 4th layer.")]
            public bool generateIceLayer;
            public Color iceColor = new Color(0.78f, 0.88f, 0.95f);
            public Color scatterTint = Color.white;
            [Min(0)] public int scatterCount = 400;
            [Min(0)] public int slopeClusters = 40;
            public DigSiteStyle majorStyle;
            public DigSiteStyle cacheStyle;
        }

        public List<Entry> entries = new List<Entry>();

        static PlanetSurfaceCatalog _loaded;
        public static PlanetSurfaceCatalog Load()
        {
            if (_loaded == null) _loaded = Resources.Load<PlanetSurfaceCatalog>(ResourcePath);
            return _loaded;
        }

        public static Entry For(ProceduralPlanetType type)
        {
            var cat = Load();
            if (cat != null && cat.entries != null)
                foreach (var e in cat.entries) if (e != null && e.type == type) return e;
            return Fallback(type);
        }

        public static Entry Fallback(ProceduralPlanetType type) => type switch
        {
            ProceduralPlanetType.Ice => new Entry { type = type, generateIceLayer = true, iceColor = new Color(0.78f, 0.88f, 0.95f), scatterTint = new Color(0.86f, 0.9f, 0.95f), scatterCount = 360, slopeClusters = 35 },
            ProceduralPlanetType.Desert => new Entry { type = type, scatterTint = new Color(1f, 0.62f, 0.43f), scatterCount = 440, slopeClusters = 40 },
            ProceduralPlanetType.Volcanic => new Entry { type = type, scatterTint = new Color(0.45f, 0.28f, 0.24f), scatterCount = 400, slopeClusters = 50 },
            ProceduralPlanetType.Barren => new Entry { type = type, scatterTint = new Color(0.86f, 0.86f, 0.88f), scatterCount = 480, slopeClusters = 45 },
            _ => new Entry { type = type, scatterTint = new Color(0.7f, 0.64f, 0.56f), scatterCount = 400, slopeClusters = 40 },
        };
    }
}