using System.IO;
using System.Text;
using DMI.Xenoarch.Galaxy;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Creates one unique height profile asset per generated landable planet (wide Inspector ranges, 2049 heightmaps).
    /// Existing assets are left alone so hand tweaks survive. Reset from the menu if you want the defaults back.
    /// </summary>
    public static class XenoarchHeightProfiles
    {
        public const string CatalogPath = "Assets/_Project/Resources/Galaxy/PlanetHeightCatalog.asset";
        public const string Folder = "Assets/_Project/Data/Terrain/HeightProfiles";

        public static void Ensure(StringBuilder log, bool overwrite = false)
        {
            XenoarchSetup.EnsureFolder("Assets/_Project/Resources/Galaxy");
            XenoarchSetup.EnsureFolder(Folder);
            var cat = AssetDatabase.LoadAssetAtPath<PlanetHeightCatalog>(CatalogPath);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<PlanetHeightCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
                log?.AppendLine("created " + CatalogPath);
            }
            if (cat.profiles == null) cat.profiles = new System.Collections.Generic.List<PlanetHeightProfile>();

            foreach (var def in Defaults)
            {
                string path = $"{Folder}/Height_{Slug(def.bodyId)}.asset";
                var profile = AssetDatabase.LoadAssetAtPath<PlanetHeightProfile>(path);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<PlanetHeightProfile>();
                    Apply(profile, def);
                    AssetDatabase.CreateAsset(profile, path);
                    log?.AppendLine("created " + path);
                }
                else if (overwrite)
                {
                    Apply(profile, def);
                    EditorUtility.SetDirty(profile);
                    log?.AppendLine("reset " + path);
                }
                if (!cat.profiles.Contains(profile)) cat.profiles.Add(profile);
            }
            cat.profiles.RemoveAll(p => p == null);
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            PlanetHeightCatalog.Invalidate();
        }

        [MenuItem("Xenoarch/Planets/Reset Height Profiles to Defaults", priority = 84)]
        public static void ResetDefaults()
        {
            if (!EditorUtility.DisplayDialog("Reset height profiles",
                "This overwrites every generated-planet height profile with the default unique shapes. Your Inspector tweaks will be lost. Moon and Mars are not touched.",
                "Reset", "Cancel")) return;
            var log = new StringBuilder();
            Ensure(log, overwrite: true);
            Debug.Log("[Xenoarch] Height profiles reset.\n" + log);
        }

        static string Slug(string bodyId)
        {
            string s = bodyId ?? "body";
            if (s.StartsWith("body.")) s = s.Substring(5);
            return s.Replace('.', '_').Replace(' ', '_');
        }

        struct Def
        {
            public string bodyId, description;
            public HeightShapeKind shape;
            public float relief, frequency, persistence, lacunarity, ridge, warp, cliff, mesa, canyon, craters, peaks, rim, flatten;
            public int octaves, terraces, seedOffset;
            public bool veg, snow, glacier, bergs, drifts;
        }

        static void Apply(PlanetHeightProfile p, Def d)
        {
            p.bodyId = d.bodyId;
            p.description = d.description;
            p.shape = d.shape;
            p.heightmapResolution = ProceduralGalaxy.BakeHeightRes;
            p.relief = d.relief;
            p.frequency = d.frequency;
            p.octaves = d.octaves;
            p.persistence = d.persistence;
            p.lacunarity = d.lacunarity;
            p.ridgeWeight = d.ridge;
            p.warp = d.warp;
            p.terraceSteps = d.terraces;
            p.cliffSharpness = d.cliff;
            p.mesaCoverage = d.mesa;
            p.canyonDepth = d.canyon;
            p.craterDensity = d.craters;
            p.peakDensity = d.peaks;
            p.rimHeight = d.rim;
            p.rimStart = 0.84f;
            p.flattenRadius = d.flatten;
            p.flattenFalloff = 2.3f;
            p.seedOffset = d.seedOffset;
            p.noiseOffset = new Vector2((d.seedOffset % 97) * 3.7f, (d.seedOffset / 7) * 1.9f);
            p.futureRockPrefabs = true;
            p.futureVegetation = d.veg;
            p.futureSnowLayer = d.snow;
            p.futureGlacierSheets = d.glacier;
            p.futureIcebergs = d.bergs;
            p.futureSnowDrifts = d.drifts;
        }

        // One unique shape per landable generated planet. Relief and flatten are generous on steep worlds
        // so dig sites and landing pads stay reasonably flat. Future ice/veg flags are stored, not applied.
        static readonly Def[] Defaults =
        {
            D("body.alpha_centauri.p0", HeightShapeKind.CrateredBasin, "Barren cratered basin, low rims, open floors.",
                55f, 0.0022f, 5, 0.5f, 2.05f, 0.1f, 0.15f, 0, 0.1f, 0.2f, 0.15f, 0.55f, 0.1f, 10f, 22f, 1101, false, false, false, false, false),
            D("body.alpha_centauri.p1", HeightShapeKind.IsolatedPeaks, "Rocky plains broken by a few sharp peaks.",
                140f, 0.0020f, 5, 0.48f, 2.1f, 0.55f, 0.2f, 0, 0.25f, 0.1f, 0.05f, 0.08f, 0.7f, 16f, 26f, 1102, false, false, false, false, false),
            D("body.barnards_star.p0", HeightShapeKind.FlatPlains, "Almost level barren flats, only a hint of roll.",
                14f, 0.0014f, 3, 0.4f, 2.0f, 0f, 0.05f, 0, 0f, 0f, 0f, 0.05f, 0.05f, 6f, 18f, 1201, false, false, false, false, false),
            D("body.barnards_star.p1", HeightShapeKind.VolcanicCaldera, "Dark volcanic cones with collapsed calderas.",
                210f, 0.0024f, 5, 0.5f, 2.15f, 0.4f, 0.18f, 0, 0.2f, 0.05f, 0.1f, 0.15f, 0.85f, 18f, 28f, 1202, false, false, false, false, false),
            D("body.barnards_star.p2", HeightShapeKind.FissureRidges, "Lava-flow ridges split by long fissures.",
                95f, 0.0031f, 5, 0.52f, 2.05f, 0.35f, 0.4f, 0, 0.3f, 0.05f, 0.25f, 0.1f, 0.15f, 12f, 22f, 1203, false, false, false, false, false),
            D("body.barnards_star.p3", HeightShapeKind.RuggedFoothills, "Barren foothills, mid relief, no big peaks.",
                85f, 0.0034f, 6, 0.55f, 2.1f, 0.4f, 0.22f, 0, 0.2f, 0.05f, 0.05f, 0.12f, 0.2f, 12f, 22f, 1204, false, false, false, false, false),
            D("body.sirius.p0", HeightShapeKind.Highlands, "Raised barren highlands, broad and wind-smoothed.",
                70f, 0.0018f, 5, 0.45f, 2.0f, 0.15f, 0.12f, 0, 0.1f, 0.05f, 0.05f, 0.15f, 0.15f, 14f, 20f, 1301, false, false, false, false, false),
            D("body.epsilon_eridani.p1", HeightShapeKind.IceSheetNunataks, "Broad ice sheet with rocky nunataks punching through.",
                120f, 0.0016f, 4, 0.42f, 2.05f, 0.2f, 0.1f, 0, 0.1f, 0f, 0.05f, 0.08f, 0.65f, 10f, 24f, 1401, false, true, true, false, true),
            D("body.tau_ceti.p0", HeightShapeKind.JaggedCliffs, "Volcanic escarpments and jagged cliff edges.",
                180f, 0.0028f, 6, 0.5f, 2.2f, 0.55f, 0.35f, 0, 0.85f, 0.1f, 0.2f, 0.05f, 0.15f, 16f, 30f, 1501, false, false, false, false, false),
            D("body.tau_ceti.p1", HeightShapeKind.DuneSeas, "Desert dune seas with long wind-aligned crests.",
                48f, 0.0048f, 4, 0.4f, 2.0f, 0.05f, 0.55f, 0, 0.05f, 0.1f, 0.05f, 0.02f, 0.05f, 8f, 20f, 1502, false, false, false, false, false),
            D("body.tau_ceti.p2", HeightShapeKind.MesaTablelands, "Desert mesas and tablelands with steep flanks.",
                130f, 0.0023f, 5, 0.48f, 2.1f, 0.15f, 0.3f, 0, 0.35f, 0.62f, 0.15f, 0.05f, 0.1f, 14f, 26f, 1503, false, false, false, false, false),
            D("body.tau_ceti.p3", HeightShapeKind.BrokenPlateau, "Barren plateau cracked by faults and down-drops.",
                100f, 0.0021f, 5, 0.5f, 2.08f, 0.2f, 0.4f, 5, 0.25f, 0.3f, 0.2f, 0.1f, 0.1f, 12f, 24f, 1504, false, false, false, false, false),
            D("body.proxima_centauri.p0", HeightShapeKind.MountainRange, "High volcanic mountain range, dramatic relief.",
                260f, 0.0026f, 6, 0.52f, 2.2f, 0.75f, 0.4f, 0, 0.4f, 0.05f, 0.15f, 0.05f, 0.2f, 20f, 32f, 1601, false, false, false, false, false),
            D("body.proxima_centauri.p1", HeightShapeKind.HeavyCraters, "Old barren highland densely punched by craters.",
                75f, 0.0020f, 4, 0.45f, 2.0f, 0.05f, 0.1f, 0, 0.05f, 0.05f, 0.05f, 0.9f, 0.05f, 12f, 22f, 1602, false, false, false, false, false),
            D("body.proxima_centauri.p2", HeightShapeKind.TerracedSteps, "Barren terraced benches and risers.",
                90f, 0.0024f, 5, 0.5f, 2.05f, 0.2f, 0.15f, 8, 0.3f, 0.2f, 0.05f, 0.08f, 0.1f, 12f, 24f, 1603, false, false, false, false, false),
            D("body.g93ce5fd0.p0", HeightShapeKind.CanyonLands, "Rocky canyon country, deep winding cuts.",
                160f, 0.0027f, 5, 0.5f, 2.12f, 0.25f, 0.55f, 0, 0.3f, 0.15f, 0.8f, 0.05f, 0.1f, 14f, 28f, 1701, false, false, false, false, false),
            D("body.g93ce5fd0.p1", HeightShapeKind.FrozenFjords, "Icy highlands cut by deep frozen fjords.",
                170f, 0.0020f, 5, 0.48f, 2.1f, 0.3f, 0.45f, 0, 0.25f, 0.05f, 0.7f, 0.05f, 0.15f, 14f, 28f, 1702, false, true, true, true, true),
            D("body.g93ce5fd0.p2", HeightShapeKind.RollingIce, "Soft rolling ice hills with light wind texture.",
                40f, 0.0030f, 5, 0.5f, 2.0f, 0.15f, 0.2f, 0, 0.05f, 0f, 0.05f, 0.04f, 0.08f, 8f, 20f, 1703, false, true, false, false, true),
            D("body.g94ce6163.p0", HeightShapeKind.BasinAndRange, "Desert basins between long parallel ranges.",
                125f, 0.0022f, 5, 0.5f, 2.08f, 0.5f, 0.25f, 0, 0.2f, 0.1f, 0.2f, 0.05f, 0.15f, 14f, 24f, 1801, false, false, false, false, false),
            D("body.g94ce6163.p1", HeightShapeKind.CrevassedGlacier, "Glacial ice scored by crevasse fields.",
                65f, 0.0036f, 5, 0.48f, 2.05f, 0.2f, 0.3f, 0, 0.15f, 0f, 0.15f, 0.04f, 0.08f, 8f, 22f, 1802, false, true, true, false, true),
            D("body.g95ce62f6.p0", HeightShapeKind.RollingHills, "Open desert hills, rounded and easy to cross.",
                60f, 0.0033f, 5, 0.52f, 2.05f, 0.1f, 0.18f, 0, 0.05f, 0.05f, 0.05f, 0.04f, 0.1f, 10f, 20f, 1901, true, false, false, false, false),
            D("body.g95ce62f6.p1", HeightShapeKind.GentleUndulation, "Rocky low swells, the mildest of the mountain worlds.",
                32f, 0.0025f, 4, 0.45f, 2.0f, 0.05f, 0.1f, 0, 0.05f, 0.05f, 0.05f, 0.06f, 0.08f, 8f, 18f, 1902, false, false, false, false, false),
        };

        static Def D(string id, HeightShapeKind shape, string desc,
            float relief, float freq, int oct, float pers, float lac, float ridge, float warp, int terraces,
            float cliff, float mesa, float canyon, float craters, float peaks, float rim, float flatten, int seed,
            bool veg, bool snow, bool glacier, bool bergs, bool drifts) =>
            new Def
            {
                bodyId = id, shape = shape, description = desc,
                relief = relief, frequency = freq, octaves = oct, persistence = pers, lacunarity = lac,
                ridge = ridge, warp = warp, terraces = terraces, cliff = cliff, mesa = mesa, canyon = canyon,
                craters = craters, peaks = peaks, rim = rim, flatten = flatten, seedOffset = seed,
                veg = veg, snow = snow, glacier = glacier, bergs = bergs, drifts = drifts
            };
    }
}
