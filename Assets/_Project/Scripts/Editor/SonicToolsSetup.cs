using System.IO;
using System.Text;
using DMI.Xenoarch.Excavation;
using DMI.Xenoarch.Items;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Sonic dig tools data: renames the shovel and brush items/tiers, retires the precision tier from the hotbar defaults
    /// (its item id stays valid and maps to the excavator's Fine focus), and builds the sonic audio catalog
    /// (Resources/Audio/SonicToolAudio.asset: Kenney fallbacks committed, Pro Sound clips as gitignored vendorClips).
    /// Rerunnable. Trigger: Temp/xenoarch_sonic.trigger or Tools > Xenoarch > Setup > Sonic Tools.
    /// </summary>
    [InitializeOnLoad]
    public static class SonicToolsSetup
    {
        public const string Trigger = "Temp/xenoarch_sonic.trigger";
        const string ItemsDir = "Assets/_Project/Data/Items";
        const string ToolDir = "Assets/_Project/Resources/Excavation/DigTools";
        const string AudioDir = "Assets/_Project/Resources/Audio";
        const string Kenney = "Assets/Xenoarch_Free/Audio/Kenney_SciFiSounds/";
        const string VendorRoot = "Assets/Pro Sound Collection v4.26 Audio";
        static double _next;

        static SonicToolsSetup() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (!File.Exists(Trigger)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try { File.Delete(Trigger); } catch { return; }
            Run();
        }

        [MenuItem("Tools/Xenoarch/Setup/Sonic Tools (names, audio)")]
        public static void Run()
        {
            var sb = new StringBuilder();
            Item("Item_tool_excavation_shovel.asset", "Sonic Excavator",
                "A handheld sonic emitter. Scroll sets the chisel head size, Shift+scroll the reach; Q cycles the head shape, R the power and T the focus (Fine is the old precision mode). Too strong near fragile finds.", sb);
            Item("Item_tool_brush.asset", "Ultrasonic Brush",
                "A fan of ultrasonic waves that lifts dust and loose soil off a find without touching it. Scroll sets the spread.", sb);
            Item("Item_tool_precision_tool.asset", "Precision Tool (merged)",
                "Merged into the Sonic Excavator's Fine focus. Kept so older saves keep working: it opens the excavator in Fine focus.", sb);
            Tier("DigTool_shovel.asset", "Sonic Excavator", "excavator", null, sb);
            Tier("DigTool_brush.asset", "Ultrasonic Brush", "brush", null, sb);
            Tier("DigTool_precision.asset", null, null, false, sb);
            DigTools.Reload();
            Audio(sb);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/xenoarch_sonic.txt", sb.ToString());
            Debug.Log("[Xenoarch] Sonic tools setup:\n" + sb);
        }

        static void Item(string file, string name, string desc, StringBuilder sb)
        {
            var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemsDir + "/" + file);
            if (it == null) { sb.AppendLine("missing item " + file); return; }
            it.displayName = name; it.description = desc;
            EditorUtility.SetDirty(it);
            sb.AppendLine("item " + file + " -> " + name);
        }

        static void Tier(string file, string name, string shortName, bool? enabled, StringBuilder sb)
        {
            var d = AssetDatabase.LoadAssetAtPath<DigToolDefinition>(ToolDir + "/" + file);
            if (d == null) { sb.AppendLine("missing tier " + file); return; }
            var so = new SerializedObject(d);
            if (name != null) so.FindProperty("spec.name").stringValue = name;
            if (shortName != null) so.FindProperty("spec.shortName").stringValue = shortName;
            if (enabled.HasValue) so.FindProperty("enabledInGame").boolValue = enabled.Value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(d);
            sb.AppendLine("tier " + file + (name != null ? " -> " + name : "") + (enabled.HasValue ? " enabled=" + enabled.Value : ""));
        }

        static AudioClip Vendor(string name)
        {
            if (string.IsNullOrEmpty(name) || !AssetDatabase.IsValidFolder(VendorRoot)) return null;
            foreach (var guid in AssetDatabase.FindAssets(name + " t:AudioClip", new[] { VendorRoot }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<AudioClip>(p);
            }
            return null;
        }

        static AudioClip Free(string file) => file == null ? null : AssetDatabase.LoadAssetAtPath<AudioClip>(Kenney + file);

        static SonicToolAudioCatalog.Entry E(SonicSfx id, string vendor, string[] extras, string kenney, float vol, StringBuilder sb)
        {
            var e = new SonicToolAudioCatalog.Entry { id = id, clip = Free(kenney), vendorClip = Vendor(vendor), volume = vol };
            var list = new System.Collections.Generic.List<AudioClip>();
            if (extras != null) foreach (var x in extras) { var c = Vendor(x); if (c != null) list.Add(c); }
            e.vendorExtras = list.ToArray();
            sb.AppendLine($"sfx {id}: vendor={(e.vendorClip != null ? vendor : "none")} (+{list.Count}) fallback={(e.clip != null ? kenney : "generated/none")}");
            return e;
        }

        static void Audio(StringBuilder sb)
        {
            if (!AssetDatabase.IsValidFolder(AudioDir)) { Directory.CreateDirectory(AudioDir); AssetDatabase.Refresh(); }
            string path = AudioDir + "/SonicToolAudio.asset";
            var cat = AssetDatabase.LoadAssetAtPath<SonicToolAudioCatalog>(path);
            if (cat == null) { cat = ScriptableObject.CreateInstance<SonicToolAudioCatalog>(); AssetDatabase.CreateAsset(cat, path); }
            cat.entries = new[]
            {
                E(SonicSfx.Equip, "sci-fi_device_item_power_up_flash_01", new[] { "sci-fi_device_item_power_up_flash_02", "sci-fi_device_item_power_up_flash_03" }, "forceField_000.ogg", 0.6f, sb),
                E(SonicSfx.BeamLoop, "sci-fi_forcefield_hum_loop_01", new[] { "sci-fi_forcefield_hum_loop_02" }, null, 0.45f, sb),
                E(SonicSfx.FanLoop, "sci-fi_hologram_electric_buzz_01", null, null, 0.4f, sb),
                E(SonicSfx.Change, "sci-fi_forcefield_holo_power_down_01", null, "forceField_001.ogg", 0.5f, sb),
                E(SonicSfx.Overheat, "sci-fi_flash_item_light_power_zap_down_large_01", null, "forceField_004.ogg", 0.7f, sb),
                E(SonicSfx.Empty, "sci-fi_electric_pulse_power_down_01", new[] { "sci-fi_electric_pulse_power_down_02" }, "forceField_003.ogg", 0.6f, sb),
                E(SonicSfx.Jar, null, null, "impactMetal_000.ogg", 0.6f, sb),
            };
            EditorUtility.SetDirty(cat);
        }
    }
}
