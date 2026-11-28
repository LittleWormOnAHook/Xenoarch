using System;
using System.Collections.Generic;
using DMI.Xenoarch.Audio;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.Editor
{
    /// <summary>
    /// Creates the UI / footstep sound catalogs. Kenney CC0 clips (in Xenoarch_Free) stay in the committed
    /// fallback slots. When Pro Sound Collection wavs are present under Assets/Pro Sound Collection v4.26 Audio,
    /// they are assigned to vendor slots (preferred at runtime). Does not copy or move vendor files.
    /// </summary>
    [InitializeOnLoad]
    public static class ProSoundCatalogBuilder
    {
        const string DataDir = "Assets/_Project/Data/Audio";
        const string UiPath = DataDir + "/UISoundCatalog.asset";
        const string FootPath = DataDir + "/ProSoundFootstepCatalog.asset";
        const string StubPath = "Assets/_Project/Resources/Audio/UISoundCatalogStub.asset";
        const string KenneyUi = "Assets/Xenoarch_Free/Audio/Kenney_InterfaceSounds/";
        const string KenneySciFi = "Assets/Xenoarch_Free/Audio/Kenney_SciFiSounds/";
        const string ExtractedStereo = "Assets/Pro Sound Collection v4.26 Audio/ProSoundCollection";
        const string ExtractedMono = "Assets/Pro Sound Collection v4.26 Audio/ProSoundCollectionMONO";
        const int FootstepCap = 12;

        static readonly string[] LegacyExtractedRoots =
        {
            "Assets/Pro Sound Collection v4.26 Audio",
            "Assets/Pro Sound Collection",
            "Assets/ProSoundCollection",
            "Assets/RUINS/Pro Sound Collection v4.26/Extracted"
        };

        [InitializeOnLoadMethod]
        static void Boot()
        {
            EditorApplication.delayCall += RunAfterReload;
        }

        static void RunAfterReload()
        {
            var project = System.IO.Directory.GetParent(Application.dataPath)?.FullName;
            var trigger = project != null
                ? System.IO.Path.Combine(project, "Temp", "xenoarch_rebuild_uisound.trigger")
                : null;
            bool force = !string.IsNullOrEmpty(trigger) && System.IO.File.Exists(trigger);
            // Only run a full rebuild when explicitly requested. Indexing the extracted
            // pack (~10k clips) on every domain reload stalls the Editor for minutes.
            if (!force) return;
            System.IO.File.Delete(trigger);
            Debug.Log("[Xenoarch] Rebuilding UI sound catalogs (trigger).");
            EnsureCatalogs(true);
        }

        [MenuItem("Xenoarch/Audio/Rebuild UI Sound Catalog")]
        public static void RebuildMenu()
        {
            EnsureCatalogs(force: true);
            int n = CountVendor();
            Debug.Log(n > 0
                ? $"[Xenoarch] UI sound catalogs rebuilt with {n} Pro Sound vendor UI clips (Kenney kept as fallback)."
                : "[Xenoarch] UI sound catalogs rebuilt. Pro Sound Collection wavs not found; Kenney fallbacks are in use.");
        }

        static void EnsureCatalogs() => EnsureCatalogs(false);

        static void EnsureCatalogs(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data"))
                AssetDatabase.CreateFolder("Assets/_Project", "Data");
            if (!AssetDatabase.IsValidFolder(DataDir))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Audio");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources/Audio"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/_Project/Resources"))
                    AssetDatabase.CreateFolder("Assets/_Project", "Resources");
                AssetDatabase.CreateFolder("Assets/_Project/Resources", "Audio");
            }

            var ui = LoadOrCreate<UISoundCatalog>(UiPath);
            var feet = LoadOrCreate<ProSoundFootstepCatalog>(FootPath);
            var stub = LoadOrCreate<UISoundCatalogStub>(StubPath);
            bool dirty = false;
            if (stub.ui != ui) { stub.ui = ui; dirty = true; }
            if (stub.footsteps != feet) { stub.footsteps = feet; dirty = true; }
            if (dirty) EditorUtility.SetDirty(stub);

            FillUiDefaults(ui, force);
            TryAssignConvertedProSound(ui, feet);
            AssetDatabase.SaveAssets();
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void FillUiDefaults(UISoundCatalog ui, bool force)
        {
            if (ui.entries == null || ui.entries.Length == 0)
                ui.entries = DefaultEntries();
            else
                ui.entries = MergeEntries(ui.entries, DefaultEntries());

            foreach (var e in ui.entries)
            {
                if (e == null) continue;
                var path = KenneyPath(e.evt);
                if (string.IsNullOrEmpty(path)) continue;
                var kenney = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (kenney == null) continue;
                // If an older build stored a Pro Sound clip in the fallback slot, move it to the vendor slot.
                if (e.clip != null && e.clip != kenney && LooksLikeProSound(e.clip.name) && e.vendorClip == null)
                    e.vendorClip = e.clip;
                if (e.clip == null || force || LooksLikeProSound(e.clip.name))
                    e.clip = kenney;
            }
            ui.muteWhileLoading = true;
            if (ui.volume <= 0f) ui.volume = 1f;
            EditorUtility.SetDirty(ui);
        }

        static bool LooksLikeProSound(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.StartsWith("ui_menu_", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("sci-fi_", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("footstep_", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("item_pickup_", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("collect_item", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("chime_bell_", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("small_box_", StringComparison.OrdinalIgnoreCase);
        }

        static UISoundCatalog.Entry[] DefaultEntries()
        {
            return new[]
            {
                E(UISoundEvent.Click, 0.7f, 0.97f, 1.03f, 0.04f),
                E(UISoundEvent.Hover, 0.28f, 0.98f, 1.02f, 0.1f),
                E(UISoundEvent.Tab, 0.55f, 0.97f, 1.03f, 0.05f),
                E(UISoundEvent.WindowOpen, 0.7f, 0.98f, 1.02f, 0f),
                E(UISoundEvent.WindowClose, 0.65f, 0.98f, 1.02f, 0f),
                E(UISoundEvent.Confirm, 0.75f, 0.98f, 1.02f, 0f),
                E(UISoundEvent.Cancel, 0.65f, 0.97f, 1.03f, 0f),
                E(UISoundEvent.Error, 0.8f, 0.97f, 1.03f, 0f),
                E(UISoundEvent.Notification, 0.6f, 0.98f, 1.02f, 0.15f),
                E(UISoundEvent.Pickup, 0.7f, 0.97f, 1.05f, 0.05f),
                E(UISoundEvent.Drop, 0.7f, 0.96f, 1.04f, 0.05f),
                E(UISoundEvent.SlotDrop, 0.55f, 0.97f, 1.03f, 0.05f),
                E(UISoundEvent.Slider, 0.35f, 0.98f, 1.02f, 0.05f),
                E(UISoundEvent.Typing, 0.3f, 0.96f, 1.06f, 0.045f),
                E(UISoundEvent.MapPing, 0.65f, 0.97f, 1.03f, 0.1f),
                E(UISoundEvent.TerminalPowerOn, 0.7f, 0.98f, 1.02f, 0f),
            };
        }

        static UISoundCatalog.Entry E(UISoundEvent evt, float vol, float p0, float p1, float cd) =>
            new UISoundCatalog.Entry
            {
                evt = evt,
                volume = vol,
                pitch = new Vector2(p0, p1),
                cooldown = cd
            };

        static UISoundCatalog.Entry[] MergeEntries(UISoundCatalog.Entry[] have, UISoundCatalog.Entry[] defaults)
        {
            var list = new List<UISoundCatalog.Entry>();
            var seen = new HashSet<UISoundEvent>();
            if (have != null)
                foreach (var e in have)
                    if (e != null && seen.Add(e.evt)) list.Add(e);
            foreach (var e in defaults)
                if (e != null && seen.Add(e.evt)) list.Add(e);
            return list.ToArray();
        }

        static string KenneyPath(UISoundEvent evt)
        {
            switch (evt)
            {
                case UISoundEvent.Click: return KenneyUi + "click_002.ogg";
                case UISoundEvent.Hover: return KenneyUi + "tick_002.ogg";
                case UISoundEvent.Tab: return KenneyUi + "switch_001.ogg";
                case UISoundEvent.WindowOpen: return KenneyUi + "open_001.ogg";
                case UISoundEvent.WindowClose: return KenneyUi + "close_001.ogg";
                case UISoundEvent.Confirm: return KenneyUi + "confirmation_001.ogg";
                case UISoundEvent.Cancel: return KenneyUi + "back_001.ogg";
                case UISoundEvent.Error: return KenneyUi + "error_001.ogg";
                case UISoundEvent.Notification: return KenneyUi + "bong_001.ogg";
                case UISoundEvent.Pickup: return KenneyUi + "pluck_001.ogg";
                case UISoundEvent.Drop: return KenneyUi + "drop_001.ogg";
                case UISoundEvent.SlotDrop: return KenneyUi + "select_002.ogg";
                case UISoundEvent.Slider: return KenneyUi + "tick_001.ogg";
                case UISoundEvent.Typing: return KenneyUi + "tick_004.ogg";
                case UISoundEvent.MapPing: return KenneyUi + "select_007.ogg";
                case UISoundEvent.TerminalPowerOn: return KenneySciFi + "forceField_000.ogg";
                default: return null;
            }
        }

        static void TryAssignConvertedProSound(UISoundCatalog ui, ProSoundFootstepCatalog feet)
        {
            // Stereo first for UI (do not overwrite with mono). Footsteps are indexed mono-first below.
            var uiClips = IndexAudio(new[] { ExtractedStereo, ExtractedMono }.ConcatLegacy());
            if (uiClips.Count == 0) return;

            var want = new Dictionary<UISoundEvent, string[]>
            {
                { UISoundEvent.Click, new[] { "ui_menu_button_click_16" } },
                { UISoundEvent.Hover, new[] { "ui_menu_button_beep_04" } },
                { UISoundEvent.Tab, new[] { "ui_menu_button_scroll_page_01" } },
                { UISoundEvent.WindowOpen, new[] { "ui_menu_popup_message_01" } },
                { UISoundEvent.WindowClose, new[] { "ui_menu_button_scroll_whoosh_01" } },
                { UISoundEvent.Confirm, new[] { "ui_menu_button_confirm_01" } },
                { UISoundEvent.Cancel, new[] { "ui_menu_button_cancel_01" } },
                { UISoundEvent.Error, new[] { "ui_menu_button_error_01" } },
                { UISoundEvent.Notification, new[] { "chime_bell_positive_ring_01" } },
                { UISoundEvent.Pickup, new[] { "item_pickup_swipe_01", "collect_item_01" } },
                { UISoundEvent.Drop, new[] { "small_box_close_01" } },
                { UISoundEvent.SlotDrop, new[] { "ui_menu_button_click_08" } },
                { UISoundEvent.Slider, new[] { "ui_menu_button_scroll_20" } },
                { UISoundEvent.Typing, new[] { "ui_menu_button_keystroke_01" } },
                { UISoundEvent.MapPing, new[] { "sci-fi_radar_ui_scan_01" } },
                { UISoundEvent.TerminalPowerOn, new[] { "sci-fi_power_on_rise_beep_01" } },
            };
            bool any = false;
            foreach (var e in ui.entries)
            {
                if (e == null || !want.TryGetValue(e.evt, out var names)) continue;
                var found = FindClip(uiClips, names);
                if (found != null) { e.vendorClip = found; any = true; }
            }
            if (any) EditorUtility.SetDirty(ui);

            // Mono walks first (cleaner for world footsteps), then stereo if a prefix is missing.
            var footClips = IndexAudio(new[] { ExtractedMono, ExtractedStereo }.ConcatLegacy());
            AssignFoot(feet, footClips, "MoonRegolith", "footstep_dirt_loose_", "footstep_gravel_walk_");
            AssignFoot(feet, footClips, "Dust", "footstep_dirt_walk_");
            AssignFoot(feet, footClips, "Sand", "footstep_dirt_loose_");
            AssignFoot(feet, footClips, "Snow", "footstep_snow_walk_");
            AssignFoot(feet, footClips, "Mud", "footstep_mud_walk_");
            AssignFoot(feet, footClips, "Dirt", "footstep_dirt_walk_");
            AssignFoot(feet, footClips, "Rock", "footstep_concrete_walk_");
            AssignFoot(feet, footClips, "Ice", "footstep_ice_crunchy_walk_");
            AssignFoot(feet, footClips, "Basalt", "footstep_concrete_walk_");
            AssignFoot(feet, footClips, "Metal", "footstep_metal_low_walk_");
            AssignFoot(feet, footClips, "Gravel", "footstep_gravel_walk_");
            AssignFoot(feet, footClips, "Grating", "footstep_metal_high_walk_");
            AssignFoot(feet, footClips, "Wood", "footstep_wood_walk_");
            AssignFoot(feet, footClips, "Water", "footstep_water_walk_");
        }

        static void AssignFoot(ProSoundFootstepCatalog feet, Dictionary<string, AudioClip> clips, string id, params string[] prefixes)
        {
            var found = new List<AudioClip>();
            foreach (var prefix in prefixes)
            {
                var batch = new List<KeyValuePair<string, AudioClip>>();
                foreach (var kv in clips)
                    if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        batch.Add(kv);
                batch.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
                foreach (var kv in batch)
                    if (!found.Contains(kv.Value)) found.Add(kv.Value);
            }
            if (found.Count == 0) return;
            if (found.Count > FootstepCap) found = found.GetRange(0, FootstepCap);

            if (feet.surfaces == null) feet.surfaces = Array.Empty<ProSoundFootstepCatalog.SurfaceClips>();
            for (int i = 0; i < feet.surfaces.Length; i++)
            {
                if (feet.surfaces[i] != null && string.Equals(feet.surfaces[i].surfaceId, id, StringComparison.OrdinalIgnoreCase))
                {
                    feet.surfaces[i].clips = found.ToArray();
                    EditorUtility.SetDirty(feet);
                    return;
                }
            }
            var list = new List<ProSoundFootstepCatalog.SurfaceClips>(feet.surfaces)
            {
                new ProSoundFootstepCatalog.SurfaceClips { surfaceId = id, clips = found.ToArray() }
            };
            feet.surfaces = list.ToArray();
            EditorUtility.SetDirty(feet);
        }

        static Dictionary<string, AudioClip> IndexAudio(string[] roots)
        {
            var map = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root) || !AssetDatabase.IsValidFolder(root)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { root }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip == null) continue;
                    var name = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (name.EndsWith("_Cue", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!map.ContainsKey(name)) map[name] = clip;
                }
            }
            return map;
        }

        static AudioClip FindClip(Dictionary<string, AudioClip> clips, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
                if (clips.TryGetValue(names[i], out var c) && c != null) return c;
            return null;
        }

        static int CountVendor()
        {
            var ui = AssetDatabase.LoadAssetAtPath<UISoundCatalog>(UiPath);
            if (ui == null || ui.entries == null) return 0;
            int n = 0;
            foreach (var e in ui.entries)
                if (e != null && e.vendorClip != null) n++;
            return n;
        }

        static string[] ConcatLegacy(this string[] preferred)
        {
            var list = new List<string>(preferred);
            foreach (var r in LegacyExtractedRoots)
                if (!list.Contains(r)) list.Add(r);
            return list.ToArray();
        }
    }
}
