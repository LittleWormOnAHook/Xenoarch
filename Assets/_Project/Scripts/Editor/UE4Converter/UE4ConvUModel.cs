using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    internal sealed class UModelResult
    {
        public bool started, cancelled, ok;
        public int exitCode = -1, files;
        public string logPath, summary;
        public Dictionary<string, int> exported = new Dictionary<string, int>();
        public List<string> errors = new List<string>();
    }

    /// <summary>
    /// UModel (UE Viewer by Gildor, free standalone) integration: locate umodel.exe, build the export command line, run it with a
    /// cancellable progress bar and a captured log, and summarise the staged files. Nothing here downloads anything.
    /// </summary>
    internal static class UModel
    {
        public const string DownloadPage = "https://www.gildor.org/en/projects/umodel";
        public const string RecommendedDir = @"A:\Tools\UModel";
        public const string PrefLogFolder = "Logs/UE4Converter";

        public static readonly string[] GamePresets = BuildGames();
        static string[] BuildGames()
        {
            var l = new List<string> { "(auto-detect)" };
            for (int i = 0; i <= 27; i++) l.Add("ue4." + i);
            for (int i = 0; i <= 3; i++) l.Add("ue5." + i);
            return l.ToArray();
        }

        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        public static string MissingMessage =>
            "UModel (UE Viewer) was not found. It is a free standalone tool by Gildor. Download it from " + DownloadPage +
            " (Win32 zip), extract it, and put it at " + RecommendedDir + @"\umodel.exe (umodel_64.exe there also works; or set the path in the UModel section). " +
            "Nothing is downloaded automatically.";

        // ---- locating the executable --------------------------------------------------------------------------

        public static string Find(ConverterOptions o, IEnumerable<string> extraDirs = null)
        {
            if (!string.IsNullOrWhiteSpace(o?.umodelExe) && File.Exists(o.umodelExe)) return o.umodelExe;
            var dirs = new List<string>();
            if (extraDirs != null) dirs.AddRange(extraDirs);
            string root = ProjectRoot;
            dirs.Add(RecommendedDir); dirs.Add(@"A:\Tools"); dirs.Add(@"A:\Unity Assets\umodel_win32");
            dirs.Add(Path.Combine(root, "Tools", "UModel")); dirs.Add(Path.Combine(root, "Tools"));
            dirs.Add(Path.Combine(Directory.GetParent(root)?.FullName ?? root, "Tools", "UModel"));
            dirs.Add(@"C:\Tools\UModel"); dirs.Add(@"C:\UModel");
            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrEmpty(pf)) dirs.Add(Path.Combine(pf, "UModel"));
            string profile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(profile)) { dirs.Add(Path.Combine(profile, "Downloads")); dirs.Add(Path.Combine(profile, "Desktop")); }
            foreach (var d in dirs)
            {
                var hit = InDir(d);
                if (hit != null) return hit;
            }
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                try
                {
                    foreach (var n in new[] { "umodel_64.exe", "umodel.exe" })
                    { var f = Path.Combine(d.Trim('"'), n); if (File.Exists(f)) return f; }
                }
                catch { }
            }
            return null;
        }

        static string InDir(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                var hit = Pick(Directory.GetFiles(dir, "umodel*.exe", SearchOption.TopDirectoryOnly));
                if (hit != null) return hit;
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    string n = Path.GetFileName(sub).ToLowerInvariant();
                    if (!n.Contains("umodel") && !n.Contains("ue viewer") && !n.Contains("ue_viewer") && !n.Contains("ueviewer")) continue;
                    hit = Pick(Directory.GetFiles(sub, "umodel*.exe", SearchOption.TopDirectoryOnly));
                    if (hit != null) return hit;
                }
            }
            catch { }
            return null;
        }

        static string Pick(string[] files)
        {
            if (files == null || files.Length == 0) return null;
            return files.FirstOrDefault(f => Path.GetFileName(f).Equals("umodel_64.exe", StringComparison.OrdinalIgnoreCase))
                   ?? files.FirstOrDefault(f => Path.GetFileName(f).Equals("umodel.exe", StringComparison.OrdinalIgnoreCase))
                   ?? files[0];
        }

        // ---- command line -------------------------------------------------------------------------------------

        static string Q(string s) => "\"" + s + "\"";
        static string Fwd(string p) => Path.GetFullPath(p).Replace('\\', '/').TrimEnd('/');

        public static string StagingDir(string pack) => Path.Combine(ProjectRoot, "Library", "UE4Converter", "staging", ConverterOptions.Sanitize(pack));

        /// <summary>umodel -path="src" -game=ue4.NN -export -out="staging" -sounds [-png] [-gltf] [-lods] "*.uasset"</summary>
        public static string BuildArgs(ConverterOptions o, string srcDir, string outDir)
        {
            var a = new List<string> { "-path=" + Q(Fwd(srcDir)) };
            if (!string.IsNullOrWhiteSpace(o.ueGame) && !o.ueGame.StartsWith("(")) a.Add("-game=" + o.ueGame.Trim());
            a.Add("-export");
            a.Add("-out=" + Q(Fwd(outDir)));
            a.Add("-sounds");
            if (o.exportPng) a.Add("-png");
            switch (o.meshFormat)
            {
                case UModelMesh.Gltf: a.Add("-gltf"); break;
                case UModelMesh.None: a.Add("-nomesh"); a.Add("-nostat"); a.Add("-noanim"); break;
            }
            if (o.soundsOnly && o.meshFormat != UModelMesh.None) { a.Add("-nomesh"); a.Add("-nostat"); a.Add("-noanim"); }
            if (o.soundsOnly) a.Add("-notex");
            if (o.exportLods && o.meshFormat != UModelMesh.None && !o.soundsOnly) a.Add("-lods");
            a.Add(Q("*.uasset"));
            return string.Join(" ", a);
        }

        // ---- log parsing --------------------------------------------------------------------------------------

        static readonly Regex ExportRx = new Regex(@"^\s*Exporting\s+(?<cls>[A-Za-z0-9_]+)\s+(?<name>\S.*?)\s*$", RegexOptions.Multiline);
        static readonly Regex ErrRx = new Regex(@"^.*(\bERROR\b|\bFATAL\b|\bFailed\b|Unsupported|not supported|Unable to|Cannot|Unversioned UE4).*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        public static void ParseLog(string log, UModelResult r)
        {
            foreach (Match m in ExportRx.Matches(log ?? ""))
            {
                string c = m.Groups["cls"].Value;
                r.exported.TryGetValue(c, out int n); r.exported[c] = n + 1;
            }
            foreach (Match m in ErrRx.Matches(log ?? ""))
            {
                if (r.errors.Count >= 200) break;
                string line = m.Value.Trim();
                if (line.Length > 0 && !line.StartsWith("Exporting", StringComparison.OrdinalIgnoreCase)) r.errors.Add(line);
            }
        }

        // ---- running ------------------------------------------------------------------------------------------

        /// <param name="progress">called ~4x/s with (last log line, exports so far); return true to cancel.</param>
        public static UModelResult Run(string exe, string args, string logPath, Func<string, int, bool> progress)
        {
            var r = new UModelResult { logPath = logPath };
            var sb = new StringBuilder();
            sb.AppendLine("> " + exe + " " + args);
            string last = ""; int exports = 0; var lk = new object();
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "", StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var p = new Process { StartInfo = psi })
            {
                DataReceivedEventHandler h = (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (lk) { sb.AppendLine(e.Data); last = e.Data; if (e.Data.TrimStart().StartsWith("Exporting", StringComparison.Ordinal)) exports++; }
                };
                p.OutputDataReceived += h; p.ErrorDataReceived += h;
                try { p.Start(); r.started = true; }
                catch (Exception e) { r.summary = "could not start UModel: " + e.Message; Save(logPath, sb); return r; }
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                while (!p.WaitForExit(250))
                {
                    string l; int n; lock (lk) { l = last; n = exports; }
                    if (progress != null && progress(l, n))
                    {
                        r.cancelled = true;
                        try { p.Kill(); } catch { }
                        break;
                    }
                }
                try { p.WaitForExit(); r.exitCode = p.ExitCode; } catch { }
            }
            string log; lock (lk) log = sb.ToString();
            Save(logPath, sb);
            ParseLog(log, r);
            r.ok = !r.cancelled && r.exitCode == 0;
            return r;
        }

        static void Save(string path, StringBuilder sb)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, sb.ToString()); } catch { }
        }

        public static int CountFiles(string dir) => Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count() : 0;

        public static string Describe(string dir)
        {
            if (!Directory.Exists(dir)) return "no output";
            return string.Join(", ", Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .GroupBy(f => Path.GetExtension(f).ToLowerInvariant()).OrderByDescending(g => g.Count()).Take(10).Select(g => $"{g.Count()} x {(g.Key.Length == 0 ? "(none)" : g.Key)}"));
        }
    }

    /// <summary>Audio length probing (WAV header, Ogg Vorbis last granule) used to choose load type / compression.</summary>
    internal static class AudioProbe
    {
        public static float Duration(string path)
        {
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                long len = new FileInfo(path).Length;
                if (ext == ".wav") return WavSeconds(path, len);
                if (ext == ".ogg") { float s = OggSeconds(path, len); if (s > 0) return s; }
                return len / 16000f;   // mp3/flac/aiff estimate (~128 kbps)
            }
            catch { return 0f; }
        }

        static float WavSeconds(string path, long len)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                if (len < 44 || new string(br.ReadChars(4)) != "RIFF") return len / 32000f;
                br.ReadInt32(); br.ReadChars(4);
                int byteRate = 0;
                while (fs.Position + 8 <= len)
                {
                    string id = new string(br.ReadChars(4)); long size = br.ReadUInt32();
                    if (id == "fmt ") { var b = br.ReadBytes((int)Math.Min(size, 16)); if (b.Length >= 12) byteRate = BitConverter.ToInt32(b, 8); fs.Position += size - b.Length; }
                    else if (id == "data") { if (size == 0 || size > len) size = len - fs.Position; return byteRate > 0 ? size / (float)byteRate : 0f; }
                    else fs.Position += size + (size & 1);
                }
            }
            return 0f;
        }

        static float OggSeconds(string path, long len)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var head = new byte[Math.Min(len, 4096)]; fs.Read(head, 0, head.Length);
                int vi = IndexOf(head, new byte[] { 1, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' }, 0, true);
                if (vi < 0 || vi + 16 > head.Length) return 0f;
                int rate = BitConverter.ToInt32(head, vi + 7 + 4 + 1);
                if (rate <= 0) return 0f;
                int tailLen = (int)Math.Min(len, 65536);
                var tail = new byte[tailLen]; fs.Position = len - tailLen; fs.Read(tail, 0, tailLen);
                int oi = IndexOf(tail, new byte[] { (byte)'O', (byte)'g', (byte)'g', (byte)'S' }, tailLen - 4, false);
                if (oi < 0 || oi + 14 > tailLen) return 0f;
                long granule = BitConverter.ToInt64(tail, oi + 6);
                return granule > 0 ? granule / (float)rate : 0f;
            }
        }

        static int IndexOf(byte[] buf, byte[] pat, int from, bool forward)
        {
            if (forward) { for (int i = Math.Max(0, from); i <= buf.Length - pat.Length; i++) if (Eq(buf, i, pat)) return i; }
            else for (int i = Math.Min(from, buf.Length - pat.Length); i >= 0; i--) if (Eq(buf, i, pat)) return i;
            return -1;
        }
        static bool Eq(byte[] b, int i, byte[] p) { for (int k = 0; k < p.Length; k++) if (b[i + k] != p[k]) return false; return true; }

        static readonly Regex UiRx = new Regex(@"(^|[/_\-\s])(ui|menu|interface|button|click|hover|notification|beep|confirm|cancel|select)([/_\-\s\.]|$)", RegexOptions.IgnoreCase);

        /// <summary>Short/UI clips decompress on load; medium clips stay compressed in memory; long ambience/music streams.</summary>
        public static AudioJob Classify(string destAsset, float seconds)
        {
            var j = new AudioJob { seconds = seconds };
            if (seconds >= 20f) j.load = UnityEngine.AudioClipLoadType.Streaming;
            else if (seconds <= 4f || UiRx.IsMatch(destAsset) && seconds <= 8f) j.load = UnityEngine.AudioClipLoadType.DecompressOnLoad;
            else j.load = UnityEngine.AudioClipLoadType.CompressedInMemory;
            return j;
        }
    }
}