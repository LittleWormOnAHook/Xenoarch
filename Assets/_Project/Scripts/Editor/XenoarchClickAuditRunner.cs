#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Tooling: Temp/xenoarch_clickaudit.queue holds comma-separated resolutions (e.g. 1280x720,1920x1080,2560x1440).
    /// For each, when the Editor is idle: set the Game view to that fixed size, write Temp/xenoarch_clickaudit.run and enter
    /// Play. The runtime ClickAudit runs, writes Logs/xenoarch_clickaudit.txt and stops Play via the existing stop trigger.
    /// Only acts while the queue file exists; delete it to abort.
    /// </summary>
    [InitializeOnLoad]
    static class XenoarchClickAuditRunner
    {
        const string Queue = "Temp/xenoarch_clickaudit.queue";
        const string RunFlag = "Temp/xenoarch_clickaudit.run";
        static double _next;

        static XenoarchClickAuditRunner() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 2.0;
            if (!File.Exists(Queue)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(RunFlag))
            {
                // A run is in progress (the harness deletes the flag when done); only clear a stale flag.
                if ((DateTime.Now - File.GetLastWriteTime(RunFlag)).TotalSeconds < 240) return;
                File.Delete(RunFlag);
            }
            var items = File.ReadAllText(Queue).Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (items.Count == 0) { File.Delete(Queue); return; }
            string res = items[0]; items.RemoveAt(0);
            if (items.Count == 0) File.Delete(Queue); else File.WriteAllText(Queue, string.Join(",", items));
            var wh = res.Split('x');
            string note = "";
            try { SetGameViewSize(int.Parse(wh[0]), int.Parse(wh[1])); }
            catch (Exception e) { note = " (resize failed: " + e.Message + ")"; }
            File.WriteAllText(RunFlag, res + note);
            File.AppendAllText("Logs/xenoarch_compile.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} click audit: entering Play at {res}{note}\n");
            EditorApplication.EnterPlaymode();
        }

        static void SetGameViewSize(int w, int h)
        {
            var asm = typeof(UnityEditor.Editor).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var single = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var inst = single.GetProperty("instance").GetValue(null, null);
            var groupType = (Enum)sizesType.GetProperty("currentGroupType").GetValue(inst, null);
            var group = sizesType.GetMethod("GetGroup").Invoke(inst, new object[] { groupType });
            var gt = group.GetType();
            int total = (int)gt.GetMethod("GetTotalCount").Invoke(group, null);
            var getSize = gt.GetMethod("GetGameViewSize");
            int found = -1;
            for (int i = 0; i < total; i++)
            {
                var s = getSize.Invoke(group, new object[] { i });
                var st = s.GetType();
                int sw = (int)st.GetProperty("width").GetValue(s, null), sh = (int)st.GetProperty("height").GetValue(s, null);
                var type = st.GetProperty("sizeType").GetValue(s, null).ToString();
                if (sw == w && sh == h && type == "FixedResolution") { found = i; break; }
            }
            if (found < 0)
            {
                var sizeType = asm.GetType("UnityEditor.GameViewSize");
                var enumType = asm.GetType("UnityEditor.GameViewSizeType");
                var ctor = sizeType.GetConstructor(new[] { enumType, typeof(int), typeof(int), typeof(string) });
                var ns = ctor.Invoke(new object[] { Enum.ToObject(enumType, 1), w, h, $"Audit {w}x{h}" });
                gt.GetMethod("AddCustomSize").Invoke(group, new[] { ns });
                found = (int)gt.GetMethod("GetTotalCount").Invoke(group, null) - 1;
            }
            var gv = EditorWindow.GetWindow(asm.GetType("UnityEditor.GameView"));
            gv.GetType().GetMethod("SizeSelectionCallback", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gv, new object[] { found, null });
        }
    }
}
#endif