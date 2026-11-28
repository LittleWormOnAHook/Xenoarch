using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    /// <summary>
    /// One-click preset: extract the audio from "Assets/Pro Sound Collection v4.26" (.uasset sounds) with UModel into
    /// "Assets/Pro Sound Collection v4.26 Audio", keeping the package folder structure. The source pack is only read.
    /// Stops with a clear message when umodel.exe is missing. Menu, or create Temp/xenoarch_prosound.trigger.
    /// Both folders are gitignored (vendor content).
    /// </summary>
    [InitializeOnLoad]
    public static class UE4ConvProSound
    {
        public const string Source = "Assets/Pro Sound Collection v4.26";
        public const string Output = "Assets/Pro Sound Collection v4.26 Audio";
        const string Trigger = "Temp/xenoarch_prosound.trigger";
        public const string LogPath = "Logs/xenoarch_prosound.txt";
        const string RuinsTrigger = "Temp/xenoarch_ruins.trigger";
        public const string RuinsSource = @"A:\Unity Assets\Ruins Treasure Prop Pack v4.26\Ruins Treasure Prop Pack\RuinsTre7e7190869711V1\data\Content\RuinsTreasureProps";
        public const string RuinsOutput = "Assets/RUINS/Ruins Treasure Prop Pack";
        public const string RuinsLog = "Logs/xenoarch_ruins.txt";
        static double _next;

        static UE4ConvProSound() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (File.Exists(Trigger)) { File.Delete(Trigger); Run(false); }
            else if (File.Exists(RuinsTrigger)) { File.Delete(RuinsTrigger); RunRuins(false); }
        }

        [MenuItem("Xenoarch/Tools/UE4 \u2192 Unity: Extract Pro Sound Collection Audio")]
        static void Menu() => Run(true);

        [MenuItem("Xenoarch/Tools/UE4 \u2192 Unity: Convert Ruins Treasure Prop Pack")]
        static void RuinsMenu() => RunRuins(true);

        public static void RunRuins(bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Finish("Refused: the Editor is in Play mode.", false, RuinsLog); return; }
            if (!Directory.Exists(RuinsSource)) { Finish("Source folder not found: " + RuinsSource, interactive, RuinsLog); return; }
            var o = ConverterOptions.Load();
            o.sourceFolder = RuinsSource; o.destOverride = RuinsOutput; o.audioSubfolder = "Audio"; o.packName = "RuinsTreasureProps";
            o.soundsOnly = false; o.useUModel = true; o.meshFormat = UModelMesh.Psk; o.ueGame = "(auto-detect)"; o.exportPng = true; o.exportLods = false;
            o.dryRun = false; o.overwrite = false; o.reuseStaging = true; o.keepStaging = false; o.makePrefabs = true; o.copyAudio = true;
            o.batchFolders = new System.Collections.Generic.List<string>();
            string exe = UModel.Find(o);
            if (exe == null) { Finish("Stopped: umodel.exe not found. " + UModel.MissingMessage, interactive, RuinsLog); return; }
            var rep = UE4Converter.Run(o);
            Finish(rep.Summary() + "\nUModel: " + rep.umodelSummary + "\nReport: " + rep.mdPath, interactive, RuinsLog);
        }

        static void Finish(string msg, bool dialog, string log = null)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(log ?? LogPath, DateTime.Now.ToString("s") + "\n" + msg + "\n");
            Debug.Log("[UE4 Converter] Pro Sound Collection: " + msg);
            if (dialog) EditorUtility.DisplayDialog("Pro Sound Collection audio", msg, "OK");
        }

        public static void Run(bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Finish("Refused: the Editor is in Play mode.", false); return; }
            var o = ConverterOptions.Load();
            string root = UModel.ProjectRoot;
            if (!Directory.Exists(Path.Combine(root, Source))) { Finish("Source folder not found: " + Source, interactive); return; }

            o.sourceFolder = Path.Combine(root, Source);
            o.destOverride = Output; o.audioSubfolder = ""; o.packName = "ProSoundCollection_v4.26";
            o.soundsOnly = true; o.useUModel = true; o.meshFormat = UModelMesh.None; o.ueGame = "(auto-detect)";   // verified: -game=ue4.26 makes UModel report every clip as empty; auto-detect exports them o.exportPng = false; o.exportLods = false;
            o.dryRun = false; o.overwrite = false; o.reuseStaging = true; o.keepStaging = false; o.makePrefabs = false; o.copyAudio = true;
            o.batchFolders = new System.Collections.Generic.List<string>();

            string exe = UModel.Find(o);
            if (exe == null)
            {
                Finish("Stopped: umodel.exe was not found, so nothing was extracted or written.\n" +
                       @"Put it at  " + UModel.RecommendedDir + "\\umodel.exe  (umodel_64.exe also works; or set the path in Xenoarch > Tools > UE4 \u2192 Unity Converter > UModel).\n" +
                       "Download: " + UModel.DownloadPage + " (Win32 zip, extract it there). Then run this menu item again.", interactive);
                return;
            }
            if (interactive && !EditorUtility.DisplayDialog("Pro Sound Collection audio",
                    $"Extract the sounds from\n{Source}\ninto\n{Output}\nusing\n{exe}?\n\nThe source folder is only read.", "Extract", "Cancel")) return;

            var rep = UE4Converter.Run(o);
            string msg = rep.Summary() + "\nUModel: " + rep.umodelSummary + "\nReport: " + rep.mdPath;
            Finish(msg, interactive);
        }
    }
}