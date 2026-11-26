using System.IO;
using System.Linq;
using System.Text;
using DMI.Xenoarch.Interaction;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Read-only tooling (t172u): when Temp/xeno_devicescan.trig appears (Editor not playing), lists every interactable /
    /// terminal / ship device component in the open scenes (as they are in the Editor, saved or not) to
    /// Logs/xenoarch_device_scan.txt: hierarchy path, active state, colliders, layer. Never changes or saves anything.
    /// Also available as Xenoarch/Tools/Scan Ship Devices.
    /// </summary>
    [InitializeOnLoad]
    static class XenoarchDeviceScan
    {
        const string Trigger = "Temp/xeno_devicescan.trig";
        const string Output = "Logs/xenoarch_device_scan.txt";
        static double _next;

        static XenoarchDeviceScan() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Trigger)) return;
            File.Delete(Trigger);
            Scan();
        }

        [MenuItem("Xenoarch/Tools/Scan Ship Devices")]
        public static void Scan()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{System.DateTime.Now:o} device scan");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                sb.AppendLine($"# scene {sc.name} path={sc.path} loaded={sc.isLoaded} dirty={sc.isDirty}");
                if (!sc.isLoaded) continue;
                foreach (var root in sc.GetRootGameObjects())
                    foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null) continue;
                        var ns = mb.GetType().Namespace ?? "";
                        bool device = mb is IInteractable || (ns.StartsWith("DMI.Xenoarch") && (ns.EndsWith(".Ship") || ns.EndsWith(".Economy")));
                        if (!device) continue;
                        var go = mb.gameObject;
                        var cols = go.GetComponentsInChildren<Collider>(true);
                        string colInfo = cols.Length == 0 ? "NO COLLIDER" : string.Join(",", cols.Take(4).Select(c => $"{c.GetType().Name}{(c.isTrigger ? "(trigger)" : "")}{(c.enabled && c.gameObject.activeInHierarchy ? "" : "(off)")}@{(c.gameObject == go ? "self" : c.name)}"));
                        var parentInteract = go.transform.parent != null ? go.transform.parent.GetComponentInParent<IInteractable>(true) : null;
                        string extra = "";
                        var t = mb.GetType();
                        foreach (var f in new[] { "terminalName", "stationName", "prompt", "targetScene", "targetSpawnId" })
                        {
                            var fi = t.GetField(f);
                            if (fi != null) extra += $" {f}='{fi.GetValue(mb)}'";
                        }
                        var prefab = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                        sb.AppendLine($"{t.Name} | {Path(go.transform)} | active={go.activeInHierarchy} enabled={mb.enabled} layer={LayerMask.LayerToName(go.layer)} | {colInfo}{extra}{(prefab != null ? $" | prefabRoot={prefab.name}" : "")}{(parentInteract != null ? $" | INSIDE {parentInteract.GetType().Name}" : "")} | pos={go.transform.position}");
                    }
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Output, sb.ToString());
            Debug.Log($"[Xenoarch] Device scan written to {Output}");
        }

        static string Path(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}