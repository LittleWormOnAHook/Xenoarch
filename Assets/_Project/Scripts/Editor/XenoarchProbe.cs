using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>Read-only probe of the open scene (Temp/xenoarch_probe.trigger -> Logs/xenoarch_probe.txt). Never edits or saves.</summary>
    public static class XenoarchProbe
    {
        public const string Trigger = "Temp/xenoarch_probe.trigger";
        static readonly string[] Names = { "Shuttle Platform", "Up Console", "Down Console", "Mark_2 Shuttle", "Gear_Left", "Gear_Right", "Gear_Behind" };

        public static void Run()
        {
            var sb = new StringBuilder();
            var scene = EditorSceneManager.GetActiveScene();
            sb.AppendLine($"scene {scene.name} dirty={scene.isDirty}");
            Physics.SyncTransforms();
            var all = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene == scene).ToList();
            var names = Names.ToList();
            try { var extra = File.ReadAllText(Trigger + ".names"); names = extra.Split('|').Where(x => x.Length > 0).ToList(); } catch { }
            foreach (var n in names)
                foreach (var t in all.Where(t => n.StartsWith("~") ? t.name.IndexOf(n.Substring(1), System.StringComparison.OrdinalIgnoreCase) >= 0 : t.name == n))
                {
                    var go = t.gameObject;
                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero);
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    sb.AppendLine($"[{t.name}] path={Path(t)} active={go.activeInHierarchy}/{go.activeSelf} static={GameObjectUtility.GetStaticEditorFlags(go)} pos={t.position:F3} rot={t.eulerAngles:F1} lossy={t.lossyScale:F3} children={t.childCount} renderers={rs.Length} bounds c={b.center:F3} s={b.size:F3}");
                    foreach (var c in go.GetComponents<Component>()) sb.AppendLine($"    comp {c?.GetType().Name}");
                    foreach (var c in go.GetComponentsInChildren<Collider>(true).Take(12)) sb.AppendLine($"    col {Path(c.transform)} {c.GetType().Name} enabled={c.enabled} trigger={c.isTrigger} b={c.bounds.center:F2}/{c.bounds.size:F2}");
                    for (int i = 0; i < Mathf.Min(t.childCount, 10); i++) sb.AppendLine($"    child {t.GetChild(i).name} active={t.GetChild(i).gameObject.activeSelf}");
                    if (n == "Shuttle Platform")
                    {
                        foreach (var h in Physics.RaycastAll(b.center + Vector3.up * 0.2f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(8))
                            sb.AppendLine($"    down hit {Path(h.collider.transform)} d={h.distance:F2} y={h.point.y:F2}");
                        for (int i = 0; i < Mathf.Min(t.childCount, 40); i++) sb.AppendLine($"    child {t.GetChild(i).name} active={t.GetChild(i).gameObject.activeSelf} pos={t.GetChild(i).position:F2}");
                    }
                }
            foreach (var t in all.Where(t => t.GetComponent("PlayerSpawnPoint") != null || t.GetComponent("ShipAirlockInteractable") != null))
                sb.AppendLine($"spawn/airlock {Path(t)} pos={t.position:F2}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/xenoarch_probe.txt", sb.ToString());
            Debug.Log("[Xenoarch] probe written");
        }

        static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
