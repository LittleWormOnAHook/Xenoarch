using System;
using System.Collections.Generic;
using DMI.Xenoarch.Footsteps;
using UnityEngine;

namespace DMI.Xenoarch.Audio
{
    /// <summary>
    /// Extra footstep variations to append onto <see cref="FootstepSurfaceMap"/> at runtime. Existing ASH / project
    /// clips stay as the fallback. Assigned Pro Sound walks are preferred when present; missing vendor refs are
    /// skipped so a clone without the pack still uses the ASH clips. Also registers Gravel and Grating surfaces.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Audio/Pro Sound Footstep Catalog", fileName = "ProSoundFootstepCatalog")]
    public sealed class ProSoundFootstepCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class SurfaceClips
        {
            public string surfaceId;
            public AudioClip[] clips;
        }

        public SurfaceClips[] surfaces = Array.Empty<SurfaceClips>();

        public static ProSoundFootstepCatalog Load()
        {
            var stub = Resources.Load<UISoundCatalogStub>(UISoundCatalogStub.ResourcesPath);
            if (stub != null && stub.footsteps != null) return stub.footsteps;
            return Resources.Load<ProSoundFootstepCatalog>("Audio/ProSoundFootstepCatalog");
        }

        /// <summary>
        /// In-memory only: add Gravel / Grating, fix keyword rules, and append catalog clips. Does not dirty the
        /// Resources surface-map asset.
        /// </summary>
        public static void ApplyTo(FootstepSurfaceMap map)
        {
            if (map == null) return;
            EnsureExtraSurfaces(map);
            var cat = Load();
            if (cat != null && cat.surfaces != null)
            {
                for (int i = 0; i < cat.surfaces.Length; i++)
                {
                    var e = cat.surfaces[i];
                    if (e == null || string.IsNullOrEmpty(e.surfaceId) || e.clips == null) continue;
                    Append(map, e.surfaceId, e.clips);
                }
            }
            // Temporary stand-ins so new surfaces are not silent before Pro Sound is converted.
            FillIfEmpty(map, "Gravel", "Rock", "Dirt");
            FillIfEmpty(map, "Grating", "Metal", "Rock");
        }

        static void EnsureExtraSurfaces(FootstepSurfaceMap map)
        {
            if (map.surfaces == null) map.surfaces = new List<FootstepSurfaceMap.Surface>();
            if (Find(map, "Gravel") == null)
            {
                map.surfaces.Add(new FootstepSurfaceMap.Surface
                {
                    id = "Gravel",
                    volume = 0.62f,
                    pitch = new Vector2(0.92f, 1.12f),
                    leavePrints = true,
                    printSize = 0.32f,
                    printOpacity = 0.45f,
                    printNormalStrength = 0.85f,
                    printLifetime = 20f
                });
            }
            if (Find(map, "Grating") == null)
            {
                map.surfaces.Add(new FootstepSurfaceMap.Surface
                {
                    id = "Grating",
                    volume = 0.6f,
                    pitch = new Vector2(0.98f, 1.2f),
                    leavePrints = false
                });
            }

            EnsureRuleFirst(map.terrainLayerRules, "Gravel", "gravel", "pebble", "scree", "shingle");
            RemoveKeyword(map.terrainLayerRules, "Dirt", "gravel");
            EnsureRuleFirst(map.physicMaterialRules, "Gravel", "gravel");
            EnsureRuleFirst(map.physicMaterialRules, "Grating", "grating", "grate");
            EnsureRuleFirst(map.objectNameRules, "Grating", "grating", "grate", "catwalk");
            RemoveKeyword(map.objectNameRules, "Metal", "grate", "catwalk");
        }

        static FootstepSurfaceMap.Surface Find(FootstepSurfaceMap map, string id)
        {
            if (map.surfaces == null) return null;
            for (int i = 0; i < map.surfaces.Count; i++)
                if (map.surfaces[i] != null && string.Equals(map.surfaces[i].id, id, StringComparison.OrdinalIgnoreCase))
                    return map.surfaces[i];
            return null;
        }

        static void Append(FootstepSurfaceMap map, string id, AudioClip[] extra)
        {
            var s = Find(map, id);
            if (s == null || extra == null || extra.Length == 0) return;
            var list = new List<AudioClip>();
            if (s.clips != null)
                for (int i = 0; i < s.clips.Length; i++)
                    if (s.clips[i] != null) list.Add(s.clips[i]);
            for (int i = 0; i < extra.Length; i++)
            {
                var c = extra[i];
                if (c == null || list.Contains(c)) continue;
                bool dup = false;
                for (int j = 0; j < list.Count; j++)
                    if (list[j] != null && list[j].name == c.name) { dup = true; break; }
                if (!dup) list.Add(c);
            }
            s.clips = list.ToArray();
        }

        static void FillIfEmpty(FootstepSurfaceMap map, string id, params string[] donors)
        {
            var s = Find(map, id);
            if (s == null) return;
            if (s.clips != null)
                for (int i = 0; i < s.clips.Length; i++)
                    if (s.clips[i] != null) return;
            for (int d = 0; d < donors.Length; d++)
            {
                var src = Find(map, donors[d]);
                if (src == null || src.clips == null || src.clips.Length == 0) continue;
                s.clips = src.clips;
                return;
            }
        }

        static void EnsureRuleFirst(List<FootstepSurfaceMap.Rule> rules, string surface, params string[] keywords)
        {
            if (rules == null) return;
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] == null || !string.Equals(rules[i].surface, surface, StringComparison.OrdinalIgnoreCase))
                    continue;
                rules[i].keywords = MergeKeywords(rules[i].keywords, keywords);
                if (i > 0)
                {
                    var r = rules[i];
                    rules.RemoveAt(i);
                    rules.Insert(0, r);
                }
                return;
            }
            rules.Insert(0, new FootstepSurfaceMap.Rule { surface = surface, keywords = keywords });
        }

        static void RemoveKeyword(List<FootstepSurfaceMap.Rule> rules, string surface, params string[] remove)
        {
            if (rules == null) return;
            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (r == null || r.keywords == null) continue;
                if (!string.Equals(r.surface, surface, StringComparison.OrdinalIgnoreCase)) continue;
                var keep = new List<string>();
                for (int k = 0; k < r.keywords.Length; k++)
                {
                    var key = r.keywords[k];
                    bool drop = false;
                    for (int x = 0; x < remove.Length; x++)
                        if (string.Equals(key, remove[x], StringComparison.OrdinalIgnoreCase)) { drop = true; break; }
                    if (!drop && !string.IsNullOrEmpty(key)) keep.Add(key);
                }
                r.keywords = keep.ToArray();
            }
        }

        static string[] MergeKeywords(string[] have, string[] add)
        {
            var list = new List<string>();
            if (have != null)
                for (int i = 0; i < have.Length; i++)
                    if (!string.IsNullOrEmpty(have[i])) list.Add(have[i]);
            if (add != null)
                for (int i = 0; i < add.Length; i++)
                {
                    bool dup = false;
                    for (int j = 0; j < list.Count; j++)
                        if (string.Equals(list[j], add[i], StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                    if (!dup && !string.IsNullOrEmpty(add[i])) list.Add(add[i]);
                }
            return list.ToArray();
        }
    }
}
