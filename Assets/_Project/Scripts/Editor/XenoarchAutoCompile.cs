using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Tooling hook so script edits made outside Unity compile without a manual click. Polls (1 s) for
    /// Temp/xenoarch_compile.trigger: when it appears (and the Editor is not in Play mode), refreshes the AssetDatabase and
    /// requests a script compile. Every compile's result is written to Logs/xenoarch_compile.txt (one line per assembly,
    /// errors listed), so tools can check for errors without reading Editor.log. Also runs any queued Temp/xenoarch_*
    /// setup/smoke triggers when no compile is pending. Never enters/exits Play mode and never saves scenes.
    /// </summary>
    [InitializeOnLoad]
    static class XenoarchAutoCompile
    {
        public const string CompileTrigger = "Temp/xenoarch_compile.trigger";
        public const string CompileLog = "Logs/xenoarch_compile.txt";
        /// <summary>Heartbeat + play/compile state for tooling (rewritten every ~5 s and on change).</summary>
        public const string StateFile = "Logs/xenoarch_editor_state.txt";
        /// <summary>Only used when Anthony has OK'd it: leaves Play mode (no scene saving).</summary>
        public const string StopPlayTrigger = "Temp/xenoarch_stopplay.trigger";
        static double _next, _nextState;
        static string _lastState;

        static XenoarchAutoCompile()
        {
            EditorApplication.update += Poll;
            CompilationPipeline.compilationStarted += _ => Write($"{Stamp()} compile started");
            CompilationPipeline.assemblyCompilationFinished += OnAssembly;
            CompilationPipeline.compilationFinished += _ => Write($"{Stamp()} compile finished");
        }

        static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        static void Write(string line)
        {
            try { Directory.CreateDirectory("Logs"); File.AppendAllText(CompileLog, line + "\n"); } catch { /* tooling only */ }
        }

        static void WriteState()
        {
            string state = $"playing={EditorApplication.isPlaying} compiling={EditorApplication.isCompiling} updating={EditorApplication.isUpdating}";
            if (state == _lastState && EditorApplication.timeSinceStartup < _nextState) return;
            _lastState = state; _nextState = EditorApplication.timeSinceStartup + 5.0;
            try { Directory.CreateDirectory("Logs"); File.WriteAllText(StateFile, $"{DateTime.Now:o} {state}\n"); } catch { }
        }

        static void OnAssembly(string asm, CompilerMessage[] messages)
        {
            var errors = messages.Where(m => m.type == CompilerMessageType.Error).ToList();
            Write($"{Stamp()} {Path.GetFileName(asm)}: {(errors.Count == 0 ? "OK" : errors.Count + " error(s)")}");
            foreach (var e in errors) Write($"  ERROR {e.message}");
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            WriteState();
            if (EditorApplication.isPlaying && File.Exists(StopPlayTrigger))
            {
                File.Delete(StopPlayTrigger);
                Write($"{Stamp()} stop Play requested by tooling");
                EditorApplication.isPlaying = false;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(CompileTrigger))
            {
                File.Delete(CompileTrigger);
                Write($"{Stamp()} compile requested (refresh)");
                AssetDatabase.Refresh();
                CompilationPipeline.RequestScriptCompilation();
                return; // queued setup triggers run after the reload
            }
            if (Directory.Exists("Temp") && Directory.GetFiles("Temp", "xenoarch_*.trigger").Length > 0) XenoarchFirstRun.TryRun();
        }
    }
}
