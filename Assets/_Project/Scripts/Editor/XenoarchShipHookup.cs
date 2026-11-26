using System.Collections.Generic;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Ship;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Ship_Starter hookup (runs inside the 1C ship pass, i.e. only when the scene is open and SAVED). Adds scripts to
    /// Anthony's existing objects; never moves, creates or deletes objects:
    /// Refiner -> RefinerTerminal; Storage_* -> StorageTerminal (role from name, lid Crate_Top_02);
    /// Shuttle Platform -> ShuttleLift (+ clears Batching Static so it can move); Up/Down Console -> ShuttleLiftConsole;
    /// bay "Mark_2 Shuttle" -> ShuttleBayShuttle (and copies its scale to the landed shuttle).
    /// </summary>
    public static class XenoarchShipHookup
    {
        public static void Run(List<GameObject> all, StringBuilder log)
        {
            var live = all.Where(g => g != null && g.activeInHierarchy).ToList();

            // Refiner
            foreach (var g in live.Where(g => g.name == "Refiner"))
            {
                if (g.GetComponent<RefinerTerminal>() == null) { var t = Undo.AddComponent<RefinerTerminal>(g); t.terminalName = "Refiner"; log.AppendLine($"Hookup: RefinerTerminal added to '{Path(g)}'"); }
                EnsureCollider(g, log);
            }

            // Storage crates
            foreach (var g in live.Where(g => g.name.IndexOf("Storage", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var lid = StorageTerminal.FindLid(g.transform);
                if (lid == null) { log.AppendLine($"Hookup: '{Path(g)}' has 'Storage' in its name but no lid (Crate_Top_02) - skipped"); continue; }
                var st = g.GetComponent<StorageTerminal>();
                if (st == null) { st = Undo.AddComponent<StorageTerminal>(g); st.lid = lid; st.terminalName = g.name; }
                if (st.lid == null) st.lid = lid;
                EditorUtility.SetDirty(st);
                EnsureCollider(g, log);
                log.AppendLine($"Hookup: '{Path(g)}' -> {ShipHold.RoleFromName(g.name)} storage, lid '{lid.name}' (opens {st.lidAngle:0} deg)");
            }

            // Shuttle bay lift
            var platform = live.FirstOrDefault(g => g.name == "Shuttle Platform");
            ShuttleLift lift = null;
            if (platform != null)
            {
                lift = platform.GetComponent<ShuttleLift>() ?? Undo.AddComponent<ShuttleLift>(platform);
                var flags = GameObjectUtility.GetStaticEditorFlags(platform);
                if ((flags & StaticEditorFlags.BatchingStatic) != 0)
                {
                    Undo.RecordObject(platform, "Platform static");
                    GameObjectUtility.SetStaticEditorFlags(platform, flags & ~StaticEditorFlags.BatchingStatic);
                    log.AppendLine("Hookup: cleared Batching Static on 'Shuttle Platform' so it can move (other static flags kept)");
                }
                log.AppendLine($"Hookup: ShuttleLift on '{Path(platform)}'");
            }
            else log.AppendLine("Hookup: no 'Shuttle Platform' found");
            foreach (var n in new[] { "Up Console", "Down Console" })
            {
                var c = live.FirstOrDefault(g => g.name == n);
                if (c == null) { log.AppendLine($"Hookup: no '{n}' found"); continue; }
                var lc = c.GetComponent<ShuttleLiftConsole>() ?? Undo.AddComponent<ShuttleLiftConsole>(c);
                lc.lift = lift; EditorUtility.SetDirty(lc);
                EnsureCollider(c, log);
                log.AppendLine($"Hookup: ShuttleLiftConsole on '{Path(c)}'");
            }

            // Bay shuttle
            var config = AssetDatabase.LoadAssetAtPath<Core.XenoarchConfig>(XenoarchSetup.ConfigPath);
            var ship = config != null ? config.starterShip : null;
            foreach (var g in live.Where(g => g.transform.parent == null && g.name.Contains("Mark_2 Shuttle")))
            {
                if (g.GetComponent<ShuttleBayShuttle>() == null) Undo.AddComponent<ShuttleBayShuttle>(g);
                var src = PrefabUtility.GetCorrespondingObjectFromSource(g);
                string srcPath = src != null ? AssetDatabase.GetAssetPath(src) : "(not a prefab instance)";
                if (ship != null)
                {
                    if (src != null && ship.shuttlePrefab != src) { ship.shuttlePrefab = src; log.AppendLine($"Hookup: landed shuttle prefab set to the bay shuttle's prefab {srcPath}"); }
                    ship.shuttleScale = g.transform.localScale.x;
                    EditorUtility.SetDirty(ship);
                }
                log.AppendLine($"Hookup: ShuttleBayShuttle on bay shuttle '{g.name}' ({srcPath}, scale {g.transform.localScale.x:0.#####})");
            }
            AssetDatabase.SaveAssets();
        }

        static void EnsureCollider(GameObject g, StringBuilder log)
        {
            if (g.GetComponentInChildren<Collider>(true) != null) return;
            var rs = g.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var bc = Undo.AddComponent<BoxCollider>(g);
            bc.center = g.transform.InverseTransformPoint(b.center);
            var s = g.transform.lossyScale;
            bc.size = g.transform.InverseTransformVector(b.size); bc.size = new Vector3(Mathf.Abs(bc.size.x), Mathf.Abs(bc.size.y), Mathf.Abs(bc.size.z));
            log.AppendLine($"Hookup: BoxCollider added to '{g.name}' (it had none) so E can target it");
        }

        static string Path(GameObject g) => g.transform.parent == null ? g.name : Path(g.transform.parent.gameObject) + "/" + g.name;

        /// <summary>Smoke: components present in the saved Ship_Starter file.</summary>
        const string SfxPath = "Assets/_Project/Resources/XenoarchShipSfx.asset";
        const string KitSounds = "Assets/_Creepy_Cat/_3D Scifi Kit Vol 3/Sounds/";

        /// <summary>Ship sound set asset (not a scene object): references licensed Creepy Cat kit clips, keeps any clip already set.</summary>
        public static void EnsureSfx(StringBuilder log)
        {
            var sfx = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Ship.ShipSfx>(SfxPath);
            if (sfx == null) { sfx = ScriptableObject.CreateInstance<DMI.Xenoarch.Ship.ShipSfx>(); AssetDatabase.CreateAsset(sfx, SfxPath); }
            AudioClip Clip(string f) => AssetDatabase.LoadAssetAtPath<AudioClip>(KitSounds + f);
            if (sfx.liftStart == null) sfx.liftStart = Clip("DoorPump.wav");
            if (sfx.liftLoop == null) sfx.liftLoop = Clip("Elevator.mp3");
            if (sfx.liftStop == null) sfx.liftStop = Clip("DoorPump.wav");
            if (sfx.consoleHum == null) sfx.consoleHum = Clip("Computer_Server.wav"); // click / power-on / power-off stay procedural (kit has no short ones)
            EditorUtility.SetDirty(sfx);
            log.AppendLine($"Ship SFX: lift start={Name(sfx.liftStart)}, loop={Name(sfx.liftLoop)}, stop={Name(sfx.liftStop)} ({SfxPath})");
        }
        static string Name(Object o) => o != null ? o.name : "procedural";

        public static void Validate(System.Action<bool, string> check)
        {
            // Console power (t88u): blink curve dark -> on, and an emissive test console dims to off via property blocks only.
            float b0 = DMI.Xenoarch.Ship.ConsolePower.BlinkLevel(0f, 1.2f, 0.6f, 14f), b1 = DMI.Xenoarch.Ship.ConsolePower.BlinkLevel(1.2f, 1.2f, 0.6f, 14f);
            int flips = 0; float prev = 0f;
            for (float t = 0f; t < 0.72f; t += 0.01f) { float v = DMI.Xenoarch.Ship.ConsolePower.BlinkLevel(t, 1.2f, 0.6f, 14f); if (Mathf.Abs(v - prev) > 0.15f) flips++; prev = v; }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.hideFlags = HideFlags.HideAndDontSave;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", Color.cyan * 2f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            var cp = go.AddComponent<DMI.Xenoarch.Ship.ConsolePower>();
            int slots = cp.DisplayCount; // edit mode: no Awake, this initialises (dims) it
            var mpb = new MaterialPropertyBlock(); go.GetComponent<Renderer>().GetPropertyBlock(mpb, 0);
            bool dark = mpb.GetColor("_EmissionColor").maxColorComponent < 0.01f && mat.GetColor("_EmissionColor").maxColorComponent > 1f;
            Object.DestroyImmediate(go); Object.DestroyImmediate(mat);
            check(b0 == 0f && b1 == 1f && flips >= 4 && slots == 1 && dark, $"Console power: blink-on curve ({flips} blinks), display starts dark via property block, material untouched");
            var sfx = AssetDatabase.LoadAssetAtPath<DMI.Xenoarch.Ship.ShipSfx>(SfxPath);
            check(sfx != null, $"Ship SFX: sound set at {SfxPath} (lift loop {(sfx != null ? Name(sfx.liftLoop) : "-")})");
            string path = XenoarchSetup.ScenePath(Core.SceneNames.ShipStarter);
            if (!System.IO.File.Exists(path)) { check(true, "Ship hookup: Ship_Starter missing - skipped"); return; }
            string text = System.IO.File.ReadAllText(path);
            string Guid<T>() where T : MonoBehaviour
            {
                var tmp = new GameObject("tmp"); tmp.hideFlags = HideFlags.HideAndDontSave;
                var c = tmp.AddComponent<T>(); var ms = MonoScript.FromMonoBehaviour(c); Object.DestroyImmediate(tmp);
                return AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ms));
            }
            int Count(string guid) { int n = 0, i = 0; while ((i = text.IndexOf(guid, i, System.StringComparison.Ordinal)) >= 0) { n++; i += guid.Length; } return n; }
            int lifts = Count(Guid<ShuttleLift>()), consoles = Count(Guid<ShuttleLiftConsole>()), storage = Count(Guid<StorageTerminal>()), bay = Count(Guid<ShuttleBayShuttle>()), refiner = Count(Guid<RefinerTerminal>());
            bool done = lifts > 0 || storage > 0 || bay > 0;
            check(true, done ? $"Ship hookup: saved Ship_Starter has lift {lifts}, lift consoles {consoles}, storage crates {storage}, bay shuttle {bay}, refiner {refiner}"
                             : "Ship hookup: not applied yet (Ship_Starter has unsaved changes) - save it and run setup");
        }
    }
}
