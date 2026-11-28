using System;
using System.Collections.Generic;
using DMI.Xenoarch.Audio;
using UnityEngine;

namespace DMI.Xenoarch.Footsteps
{
    /// <summary>
    /// Per-surface footstep audio and footprint tunables, plus the keyword rules that pick a surface
    /// from a terrain layer, physic material, tag, or object name. Adapted from the ASH footstep surface
    /// map (ASH.Player.ASHFootstepSurfaceMap) for Xenoarch's planetary layers and ship/rover metal.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Footstep Surface Map", fileName = "XA_FootstepSurfaceMap")]
    public sealed class FootstepSurfaceMap : ScriptableObject
    {
        public const string ResourcesPath = "Footsteps/XA_FootstepSurfaceMap";

        [Serializable]
        public sealed class Surface
        {
            public string id = "Dirt";
            public AudioClip[] clips = Array.Empty<AudioClip>();
            [Range(0f, 2f)] public float volume = 0.6f;
            [Tooltip("Pitch range. Wide on purpose so surfaces can go from muted dust to bright ice.")]
            public Vector2 pitch = new Vector2(0.8f, 1.2f);
            [Tooltip("Leave a visible print on this surface. Off for rock, ice and metal.")]
            public bool leavePrints;
            [Tooltip("Print length in metres. Soft moon regolith is the largest.")]
            [Range(0f, 1.2f)] public float printSize = 0.35f;
            [Range(0f, 1f)] public float printOpacity = 0.6f;
            [Tooltip("Normal-map strength used to fake print depth. Mapped into the URP Decal Normal Blend.")]
            [Range(0f, 4f)] public float printNormalStrength = 1f;
            [Tooltip("Seconds a print stays before fading out.")]
            [Range(0.5f, 180f)] public float printLifetime = 20f;
            [Tooltip("Extra volume when walking slowly through soft soil (sand, dust, snow).")]
            [Range(0f, 2f)] public float softVolumeBoost = 0f;
            [Range(0f, 0.5f)] public float softPitchDrop = 0f;
        }

        [Serializable]
        public sealed class Rule
        {
            public string surface = "Dirt";
            [Tooltip("Case-insensitive 'name contains' keywords (tags can also be exact).")]
            public string[] keywords = Array.Empty<string>();
        }

        [Serializable]
        public sealed class LayerRule
        {
            public string surface = "Metal";
            public LayerMask layers;
        }

        public List<Surface> surfaces = new List<Surface>();

        [Header("Terrain")]
        [Tooltip("Used when a terrain layer name matches no rule.")]
        public string terrainDefault = "Dust";
        [Tooltip("Keyword on the dominant TerrainLayer name or its diffuse texture name.")]
        public List<Rule> terrainLayerRules = new List<Rule>();

        [Header("Meshes (ship, rover, Digger, props)")]
        public List<Rule> physicMaterialRules = new List<Rule>();
        public List<Rule> tagRules = new List<Rule>();
        public List<LayerRule> layerRules = new List<LayerRule>();
        [Tooltip("Keyword on the GameObject, parent or renderer material name.")]
        public List<Rule> objectNameRules = new List<Rule>();
        public string meshDefault = "Dirt";

        [Header("Footprints")]
        [Range(8, 128)] public int maxPrints = 64;
        public Texture2D printAlbedoLeft;
        public Texture2D printAlbedoRight;
        public Texture2D printNormalLeft;
        public Texture2D printNormalRight;

        static FootstepSurfaceMap _live;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetLive() => _live = null;

        public static FootstepSurfaceMap Live
        {
            get
            {
                if (_live == null)
                {
                    _live = Resources.Load<FootstepSurfaceMap>(ResourcesPath);
                    if (_live == null) _live = CreateInstance<FootstepSurfaceMap>();
                    _live.EnsureDefaults();
                    ProSoundFootstepCatalog.ApplyTo(_live);
                }
                return _live;
            }
        }

        public Surface Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return surfaces.Count > 0 ? surfaces[0] : null;
            for (int i = 0; i < surfaces.Count; i++)
                if (string.Equals(surfaces[i].id, id, StringComparison.OrdinalIgnoreCase))
                    return surfaces[i];
            return surfaces.Count > 0 ? surfaces[0] : null;
        }

        public static string Match(List<Rule> rules, string name, bool exact = false)
        {
            if (string.IsNullOrEmpty(name) || rules == null) return null;
            for (int i = 0; i < rules.Count; i++)
            {
                var keys = rules[i].keywords;
                if (keys == null) continue;
                for (int k = 0; k < keys.Length; k++)
                {
                    var key = keys[k];
                    if (string.IsNullOrEmpty(key)) continue;
                    bool hit = exact
                        ? string.Equals(name, key, StringComparison.OrdinalIgnoreCase)
                        : name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (hit) return rules[i].surface;
                }
            }
            return null;
        }

        public void EnsureDefaults()
        {
            if (surfaces == null) surfaces = new List<Surface>();
            EnsureSurface("MoonRegolith", s =>
            {
                s.volume = 0.48f;
                s.pitch = new Vector2(0.82f, 1.02f);
                s.leavePrints = true;
                s.printSize = 0.56f;
                s.printOpacity = 0.88f;
                s.printNormalStrength = 2.4f;
                s.printLifetime = 60f;
                s.softVolumeBoost = 0.45f;
                s.softPitchDrop = 0.12f;
            });
            EnsureSurface("Dust", s =>
            {
                s.volume = 0.52f;
                s.pitch = new Vector2(0.88f, 1.08f);
                s.leavePrints = true;
                s.printSize = 0.44f;
                s.printOpacity = 0.72f;
                s.printNormalStrength = 1.7f;
                s.printLifetime = 40f;
                s.softVolumeBoost = 0.25f;
                s.softPitchDrop = 0.06f;
            });
            EnsureSurface("Sand", s =>
            {
                s.volume = 0.55f;
                s.pitch = new Vector2(0.9f, 1.1f);
                s.leavePrints = true;
                s.printSize = 0.42f;
                s.printOpacity = 0.68f;
                s.printNormalStrength = 1.5f;
                s.printLifetime = 35f;
                s.softVolumeBoost = 0.2f;
                s.softPitchDrop = 0.05f;
            });
            EnsureSurface("Snow", s =>
            {
                s.volume = 0.5f;
                s.pitch = new Vector2(0.9f, 1.12f);
                s.leavePrints = true;
                s.printSize = 0.38f;
                s.printOpacity = 0.55f;
                s.printNormalStrength = 1.15f;
                s.printLifetime = 30f;
                s.softVolumeBoost = 0.3f;
                s.softPitchDrop = 0.08f;
            });
            EnsureSurface("Mud", s =>
            {
                s.volume = 0.62f;
                s.pitch = new Vector2(0.8f, 1.0f);
                s.leavePrints = true;
                s.printSize = 0.4f;
                s.printOpacity = 0.8f;
                s.printNormalStrength = 1.35f;
                s.printLifetime = 45f;
                s.softVolumeBoost = 0.2f;
                s.softPitchDrop = 0.1f;
            });
            EnsureSurface("Dirt", s =>
            {
                s.volume = 0.55f;
                s.pitch = new Vector2(0.9f, 1.1f);
                s.leavePrints = true;
                s.printSize = 0.3f;
                s.printOpacity = 0.4f;
                s.printNormalStrength = 0.7f;
                s.printLifetime = 18f;
            });
            EnsureSurface("Rock", s =>
            {
                s.volume = 0.68f;
                s.pitch = new Vector2(0.94f, 1.14f);
                s.leavePrints = false;
            });
            EnsureSurface("Ice", s =>
            {
                s.volume = 0.52f;
                s.pitch = new Vector2(1.04f, 1.22f);
                s.leavePrints = false;
            });
            EnsureSurface("Basalt", s =>
            {
                s.volume = 0.7f;
                s.pitch = new Vector2(0.88f, 1.06f);
                s.leavePrints = false;
            });
            EnsureSurface("Metal", s =>
            {
                s.volume = 0.58f;
                s.pitch = new Vector2(0.96f, 1.18f);
                s.leavePrints = false;
            });
            EnsureSurface("Wood", s =>
            {
                s.volume = 0.65f;
                s.pitch = new Vector2(0.92f, 1.1f);
                s.leavePrints = false;
            });
            EnsureSurface("Water", s =>
            {
                s.volume = 0.7f;
                s.pitch = new Vector2(0.9f, 1.1f);
                s.leavePrints = false;
            });

            if (string.IsNullOrEmpty(terrainDefault)) terrainDefault = "Dust";
            if (string.IsNullOrEmpty(meshDefault)) meshDefault = "Dirt";
            if (maxPrints < 8) maxPrints = 64;

            if (terrainLayerRules == null || terrainLayerRules.Count == 0)
            {
                terrainLayerRules = new List<Rule>
                {
                    R("MoonRegolith", "moon_regolith", "moon_soil", "tl_moon_regolith", "moon soil"),
                    R("Dust", "mars_regolith", "mars_soil", "tl_mars_regolith", "regolith", "dust", "ash", "fines"),
                    R("Sand", "sand", "dune", "desert"),
                    R("Snow", "snow", "powder"),
                    R("Ice", "ice", "tl_ice", "ice_sheet", "glacier", "frozen", "permafrost"),
                    R("Mud", "mud", "wet", "silt", "clay"),
                    R("Basalt", "lava", "basalt", "volcanic", "igneous", "crackedcrust", "cracked_crust", "obsidian"),
                    R("Rock", "rock", "cliff", "stone", "bedrock", "slab", "boulder", "crust"),
                    R("Dirt", "dirt", "soil", "loam", "gravel", "path", "trail", "strata"),
                    R("Water", "water", "puddle", "shore"),
                };
            }

            if (physicMaterialRules == null || physicMaterialRules.Count == 0)
            {
                physicMaterialRules = new List<Rule>
                {
                    R("Metal", "metal", "steel", "aluminium", "aluminum"),
                    R("Wood", "wood"),
                    R("Ice", "ice"),
                    R("Rock", "stone", "rock", "concrete"),
                    R("Sand", "sand"),
                    R("Mud", "mud"),
                    R("Dirt", "dirt", "soil"),
                    R("Snow", "snow"),
                    R("Water", "water"),
                };
            }

            if (objectNameRules == null || objectNameRules.Count == 0)
            {
                objectNameRules = new List<Rule>
                {
                    R("Metal", "ship", "shuttle", "cockpit", "hangar", "airlock", "deck", "hull", "bulkhead",
                        "rover", "vehicle", "chassis", "mark_1", "mark1", "grasshop", "metal", "grate", "catwalk",
                        "floor", "walkway", "ramp", "pad", "landing"),
                    R("Wood", "wood", "plank", "crate", "pallet"),
                    R("Rock", "rock", "stone", "cliff", "boulder", "concrete"),
                    R("Sand", "sand", "dune"),
                    R("Mud", "mud"),
                    R("Ice", "ice"),
                    R("Water", "water"),
                };
            }

            if (tagRules == null) tagRules = new List<Rule>();
            if (layerRules == null) layerRules = new List<LayerRule>();

            LoadClipsIfEmpty();
            LoadPrintsIfEmpty();
        }

        static Rule R(string surface, params string[] keywords) =>
            new Rule { surface = surface, keywords = keywords };

        void EnsureSurface(string id, Action<Surface> setup)
        {
            var existing = Get(id);
            if (existing != null && string.Equals(existing.id, id, StringComparison.OrdinalIgnoreCase))
                return;
            var s = new Surface { id = id };
            setup(s);
            surfaces.Add(s);
        }

        void LoadClipsIfEmpty()
        {
            AssignClips("MoonRegolith", "Audio/Footsteps/Sand");
            AssignClips("Dust", "Audio/Footsteps/Sand");
            AssignClips("Sand", "Audio/Footsteps/Sand");
            AssignClips("Snow", "Audio/Footsteps/Snow");
            AssignClips("Mud", "Audio/Footsteps/Dirt");
            AssignClips("Dirt", "Audio/Footsteps/Dirt");
            AssignClips("Rock", "Audio/Footsteps/Rock");
            AssignClips("Ice", "Audio/Footsteps/Snow");
            AssignClips("Basalt", "Audio/Footsteps/Rock");
            AssignClips("Metal", "Audio/Footsteps/Concrete");
            AssignClips("Wood", "Audio/Footsteps/Wood");
            AssignClips("Water", "Audio/Footsteps/Water");
        }

        void AssignClips(string id, string resourcesFolder)
        {
            var s = Get(id);
            if (s == null) return;
            if (s.clips != null && s.clips.Length > 0 && s.clips[0] != null) return;
            var loaded = Resources.LoadAll<AudioClip>(resourcesFolder);
            if (loaded != null && loaded.Length > 0) s.clips = loaded;
        }

        void LoadPrintsIfEmpty()
        {
            if (printAlbedoLeft == null) printAlbedoLeft = LoadPrintTex("Footprint_Boot_L");
            if (printAlbedoRight == null) printAlbedoRight = LoadPrintTex("Footprint_Boot_R");
            if (printNormalLeft == null) printNormalLeft = LoadPrintTex("Footprint_Boot_L_N");
            if (printNormalRight == null) printNormalRight = LoadPrintTex("Footprint_Boot_R_N");
        }

        static Texture2D LoadPrintTex(string name)
        {
            return Resources.Load<Texture2D>("Footsteps/" + name);
        }
    }
}
