using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using UnityEditor;
using UnityEngine;

namespace DMI.Xenoarch.EditorTools
{
    /// <summary>
    /// Phase 2 M1 regression tooling for procedural generation (Edit Mode only: no Play Mode, no scenes, no assets written).
    /// <list type="bullet">
    /// <item><b>Galaxy Snapshot</b> rebuilds the procedural galaxy from the galaxy seed with the artifact pool the runtime
    /// uses (<see cref="GameSession.RuntimeArtifactPool"/>) and writes every generated system, body, recipe, site and
    /// buried artifact to <c>Logs/xenoarch_galaxy_snapshot.txt</c>. It also generates twice and checks both are identical.</item>
    /// <item><b>Compare</b> diffs a fresh snapshot against the committed baseline <c>Docs/galaxy_snapshot_baseline.txt</c>
    /// (lines starting with '#' are ignored) and writes <c>Logs/xenoarch_galaxy_compare.txt</c> (PASS / FAIL).</item>
    /// <item><b>Record Baseline</b> writes the baseline (only when deliberately re-baselining).</item>
    /// </list>
    /// Trigger file (Editor not playing): <c>Temp/xenoarch_galaxy.trigger</c> containing "snapshot", "compare" or "baseline".
    /// </summary>
    [InitializeOnLoad]
    public static class XenoarchGalaxySnapshot
    {
        public const string Trigger = "Temp/xenoarch_galaxy.trigger";
        public const string SnapshotPath = "Logs/xenoarch_galaxy_snapshot.txt";
        public const string ComparePath = "Logs/xenoarch_galaxy_compare.txt";
        public const string StatusPath = "Logs/xenoarch_galaxy_status.txt";
        public const string BaselinePath = "Docs/galaxy_snapshot_baseline.txt";
        static double _next;

        static XenoarchGalaxySnapshot() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || !File.Exists(Trigger)) return;
            string cmd = "";
            try { cmd = File.ReadAllText(Trigger).Trim().ToLowerInvariant(); File.Delete(Trigger); } catch { return; }
            Directory.CreateDirectory("Logs");
            File.WriteAllText(StatusPath, $"{System.DateTime.Now:o} running '{cmd}'\n");
            try
            {
                if (cmd.StartsWith("baseline")) RecordBaseline();
                else if (cmd.StartsWith("compare")) Compare();
                else Snapshot();
            }
            catch (System.Exception e)
            {
                Directory.CreateDirectory("Logs");
                File.WriteAllText(ComparePath, $"# Galaxy snapshot tool FAILED ({cmd}) {System.DateTime.Now:o}\nFAIL: {e}\n");
                Debug.LogException(e);
            }
            File.AppendAllText(StatusPath, $"{System.DateTime.Now:o} done\n");
        }

        static string F(float v) => v.ToString("0.0###", CultureInfo.InvariantCulture);
        static string V(Vector3 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)})";
        static string C(Color c) => $"({F(c.r)}, {F(c.g)}, {F(c.b)})";

        /// <summary>Builds the snapshot text (deterministic part has no '#' lines).</summary>
        public static string Build(out bool repeatable)
        {
            var config = AssetDatabase.LoadAssetAtPath<XenoarchConfig>(XenoarchSetup.ConfigPath);
            if (config == null || config.galaxy == null) { repeatable = false; return "# ERROR: XenoarchConfig or galaxy missing\n"; }
            string a = Generate(config), b = Generate(config);
            repeatable = a == b;
            ProceduralGalaxy.ResetForTooling(config.galaxy); // leave nothing generated behind in the Editor
            var sb = new StringBuilder();
            sb.AppendLine($"# Xenoarch galaxy snapshot {System.DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}, Unity {Application.unityVersion}");
            sb.AppendLine($"# generated twice in one session: {(repeatable ? "identical" : "DIFFERENT (non-deterministic!)")}");
            sb.Append(a);
            return sb.ToString();
        }

        static string Generate(XenoarchConfig config)
        {
            var g = config.galaxy;
            var pool = GameSession.RuntimeArtifactPool(config.itemRegistry);
            var legacy = ProceduralArtifactPools.LegacyRegistryFilter(config.itemRegistry);
            ProceduralGalaxy.ResetForTooling(g);
            bool logOn = Debug.unityLogger.logEnabled; Debug.unityLogger.logEnabled = false;
            try { ProceduralGalaxy.Ensure(g, pool); } finally { Debug.unityLogger.logEnabled = logOn; }

            var sb = new StringBuilder();
            sb.AppendLine($"galaxy {g.Id} seed={g.seed}");
            sb.AppendLine($"artifactPool runtime=[{string.Join(", ", pool)}]");
            sb.AppendLine($"artifactPool legacyRegistryFilter=[{string.Join(", ", legacy)}]");
            sb.AppendLine($"artifactPool v1=[{string.Join(", ", ProceduralArtifactPools.V1)}]");
            // Independent-stream probe: the helper must give the same values for the same (id, stream, seed) forever.
            foreach (var probe in new[] { ("body.probe", "materials"), ("body.probe", "pool2"), ("site.probe", "poi"), ("sys.probe", "story") })
            {
                var r = ProceduralGalaxy.Stream(probe.Item1, probe.Item2, g.seed);
                sb.AppendLine($"stream {probe.Item1}:{probe.Item2} -> {r.Next()} {r.Next()} {r.Next()}");
            }
            int systems = 0, bodies = 0, sites = 0, finds = 0;
            foreach (var s in g.AllSystems)
            {
                if (s == null || s.generatedBodies == null || s.generatedBodies.Count == 0) continue;
                systems++;
                sb.AppendLine($"system {s.Id} \"{s.displayName}\" class={s.stellarClass} seed={s.seed} pos={V(s.galacticPosition)} ftl={F(s.minFtlRangeLy)} potential={F(s.archaeologicalPotential)} known={s.knownByDefault} reveal={s.revealFact}");
                foreach (var b in s.generatedBodies)
                {
                    if (b == null) continue;
                    bodies++;
                    sb.AppendLine($"  body {b.Id} \"{b.displayName}\" kind={b.kind} au={F(b.orbitAU)} period={F(b.orbitPeriodDays)} radiusKm={F(b.radiusKm)} g={F(b.gravityG)} potential={F(b.archaeologicalPotential)} seed={b.seed} scene={b.sceneName} base={V(b.baseLanding)} env={(b.environment != null ? b.environment.Id : "-")} gravity={(b.environment != null ? F(b.environment.gravity) : "-")}");
                    var rec = ProceduralGalaxy.Recipe(b.Id);
                    if (rec != null)
                    {
                        sb.AppendLine($"    recipe type={rec.type} seed={rec.seed} relief={F(rec.relief)} freq={rec.frequency.ToString("0.000000", CultureInfo.InvariantCulture)} offset=({F(rec.noiseOffset.x)}, {F(rec.noiseOffset.y)}) low={C(rec.low)} high={C(rec.high)} pads={rec.pads.Count} holes={rec.holes.Count}");
                        foreach (var p in rec.pads) sb.AppendLine($"      pad {V(p)}");
                    }
                    foreach (var site in ProceduralGalaxy.Sites)
                    {
                        if (site == null || site.bodyId != b.Id) continue;
                        sites++;
                        sb.AppendLine($"    site {site.Id} \"{site.displayName}\" pos={V(site.position)} r={F(site.radius)} depth={F(site.depth)} diff={site.difficulty} passes={site.orbitalPassesNeeded} signal={F(site.signalStrength)} seed={site.seed} hint={V(site.landingHint)}");
                        foreach (var f in site.artifacts) { finds++; sb.AppendLine($"      find {f.artifactId} cell=({f.cell.x}, {f.cell.y}, {f.cell.z}) footprint={f.footprint}"); }
                    }
                }
            }
            sb.AppendLine($"totals systems={systems} bodies={bodies} sites={sites} finds={finds}");
            return sb.ToString();
        }

        [MenuItem("Xenoarch/Tools/Galaxy Snapshot", priority = 300)]
        public static void Snapshot()
        {
            string text = Build(out bool rep);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(SnapshotPath, text);
            Debug.Log($"[Xenoarch] Galaxy snapshot written to {SnapshotPath} (repeatable={rep})");
        }

        [MenuItem("Xenoarch/Tools/Galaxy Snapshot Compare", priority = 301)]
        public static bool Compare()
        {
            string text = Build(out bool rep);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(SnapshotPath, text);
            var report = new StringBuilder();
            report.AppendLine($"# Galaxy snapshot compare {System.DateTime.Now:yyyy-MM-dd HH:mm:ss zzz} vs {BaselinePath}");
            bool ok;
            if (!File.Exists(BaselinePath)) { report.AppendLine("FAIL: no baseline (run Xenoarch/Tools/Galaxy Snapshot Record Baseline once)"); ok = false; }
            else
            {
                var a = Body(File.ReadAllText(BaselinePath)); var b = Body(text);
                int diffs = 0, n = Mathf.Max(a.Count, b.Count);
                for (int i = 0; i < n; i++)
                {
                    string x = i < a.Count ? a[i] : "<missing>", y = i < b.Count ? b[i] : "<missing>";
                    if (x == y) continue;
                    if (++diffs <= 40) report.AppendLine($"line {i + 1}:\n  baseline: {x}\n  current : {y}");
                }
                ok = diffs == 0 && rep;
                report.AppendLine(ok ? $"PASS: {a.Count} lines identical; generation repeatable" : $"FAIL: {diffs} differing line(s); repeatable={rep}");
            }
            File.WriteAllText(ComparePath, report.ToString());
            Debug.Log($"[Xenoarch] Galaxy snapshot compare: {(ok ? "PASS" : "FAIL")} ({ComparePath})");
            return ok;
        }

        [MenuItem("Xenoarch/Tools/Galaxy Snapshot Record Baseline", priority = 302)]
        public static void RecordBaseline()
        {
            string text = Build(out bool rep);
            Directory.CreateDirectory(Path.GetDirectoryName(BaselinePath));
            File.WriteAllText(BaselinePath, text);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(SnapshotPath, text);
            Debug.Log($"[Xenoarch] Galaxy snapshot baseline written to {BaselinePath} (repeatable={rep})");
        }

        static List<string> Body(string text)
        {
            var l = new List<string>();
            foreach (var raw in text.Replace("\r", "").Split('\n')) if (raw.Length > 0 && !raw.StartsWith("#")) l.Add(raw);
            return l;
        }
    }
}
