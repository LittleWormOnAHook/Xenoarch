using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Scenes
{
    /// <summary>
    /// Shader prewarming without the Editor's modal "Compiling shader" dialog.
    /// Editor: queues ASYNC compiles (ShaderUtil.CompilePass forceSync=false) for every material the build scenes use,
    /// a few per frame in the background from the moment the game starts (so planet shaders compile while you are on the
    /// ship), and the destination scene's materials all at once when a load starts. Progress = passes compiled / tracked.
    /// Build: ShaderVariantCollection.WarmUpProgressively a few variants per frame (more while the loading screen is up).
    /// Driven by <see cref="SceneLoader"/>.Update.
    /// </summary>
    public static class ShaderPrewarm
    {
        public const string CollectionResource = "Shaders/XenoarchVariants";
        static ShaderVariantCollection _svc; static bool _svcLoaded;
        public static ShaderVariantCollection Collection
        {
            get { if (!_svcLoaded) { _svcLoaded = true; _svc = Resources.Load<ShaderVariantCollection>(CollectionResource); } return _svc; }
        }

        /// <summary>Progress of a set of shader work the loading screen waits on. Passes already compiled by earlier loads /
        /// the background prewarm count as done from the start (<see cref="StartDone"/>), so <see cref="Relative"/> runs
        /// 0..1 over the work that is actually left. Passes that never compile (no compiler output for this pipeline,
        /// e.g. built-in-only passes) are remembered once a load stalls on them and skipped from then on.</summary>
        public sealed class Tracker
        {
            internal readonly List<Material> mats = new List<Material>();
            internal readonly HashSet<Material> set = new HashSet<Material>();
            public int Total { get; private set; }
            public int Done { get; private set; }
            /// <summary>Passes already compiled when tracking started (-1 = not sampled yet).</summary>
            public int StartDone { get; private set; } = -1;
            public int Remaining => Mathf.Max(0, Total - Done);
            /// <summary>0..1 over all tracked passes; recomputed each call (cheap: a flag read per pass).</summary>
            public float Progress
            {
                get
                {
#if UNITY_EDITOR
                    int total = 0, done = 0;
                    foreach (var m in mats)
                    {
                        if (m == null || m.shader == null) continue;
                        for (int i = 0; i < m.passCount; i++)
                        {
                            if (_stuck.Contains(Key(m, i))) continue;
                            total++; if (UnityEditor.ShaderUtil.IsPassCompiled(m, i)) done++;
                        }
                    }
                    Total = total; Done = done;
                    if (StartDone < 0) StartDone = done;
                    return total == 0 ? 1f : (float)done / total;
#else
                    var c = Collection;
                    if (c == null || c.variantCount == 0) { Total = Done = 0; if (StartDone < 0) StartDone = 0; return 1f; }
                    Total = c.variantCount; Done = Mathf.Min(c.warmedUpVariantCount, Total);
                    if (StartDone < 0) StartDone = Done;
                    return c.isWarmedUp ? 1f : (float)Done / Total;
#endif
                }
            }
            /// <summary>0..1 over the work left when tracking started (call after <see cref="Progress"/>).</summary>
            public float Relative
            {
                get
                {
                    float p = Progress;
                    if (p >= 1f) return 1f;
                    int left = Total - Mathf.Max(0, StartDone);
                    return left <= 0 ? 1f : Mathf.Clamp01((Done - StartDone) / (float)left);
                }
            }
            /// <summary>The load stalled with nothing compiling: remember the still-uncompiled passes as never-compiling so
            /// this and later loads stop waiting on them. Returns how many were skipped.</summary>
            public int SkipStalled()
            {
                int n = 0;
#if UNITY_EDITOR
                foreach (var m in mats)
                {
                    if (m == null || m.shader == null) continue;
                    for (int i = 0; i < m.passCount; i++)
                        if (!UnityEditor.ShaderUtil.IsPassCompiled(m, i) && _stuck.Add(Key(m, i))) n++;
                }
#endif
                return n;
            }
            internal void Add(Material m) { if (m != null && set.Add(m)) mats.Add(m); }
        }

        // (material, pass) pairs that never compiled in an earlier load (kept for the session).
        static readonly HashSet<(Material, int)> _stuck = new HashSet<(Material, int)>();
        static (Material, int) Key(Material m, int pass) => (m, pass);

        /// <summary>True while the Editor is compiling any shader asynchronously (always false in builds).</summary>
        public static bool AnythingCompiling
        {
#if UNITY_EDITOR
            get => UnityEditor.ShaderUtil.anythingCompiling;
#else
            get => false;
#endif
        }

#if UNITY_EDITOR
        static readonly Queue<Material> _queue = new Queue<Material>();
        static readonly HashSet<Material> _submitted = new HashSet<Material>();
        static readonly Dictionary<string, List<Material>> _sceneMats = new Dictionary<string, List<Material>>();
        static bool _allQueued;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _queue.Clear(); _submitted.Clear(); _sceneMats.Clear(); _allQueued = false; _svc = null; _svcLoaded = false; _stuck.Clear(); }

        /// <summary>Request async compilation of every pass of <paramref name="m"/> (never blocks, never shows the dialog).</summary>
        static void Submit(Material m)
        {
            if (m == null || m.shader == null || !_submitted.Add(m)) return;
            for (int i = 0; i < m.passCount; i++)
                if (!UnityEditor.ShaderUtil.IsPassCompiled(m, i)) UnityEditor.ShaderUtil.CompilePass(m, i, false);
        }

        static string ScenePath(string sceneName)
        {
            foreach (var s in UnityEditor.EditorBuildSettings.scenes)
                if (System.IO.Path.GetFileNameWithoutExtension(s.path) == sceneName) return s.path;
            return null;
        }

        static List<Material> SceneMaterials(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return new List<Material>();
            if (_sceneMats.TryGetValue(scenePath, out var list)) return list;
            list = new List<Material>();
            foreach (var dep in UnityEditor.AssetDatabase.GetDependencies(scenePath, true))
                if (dep.EndsWith(".mat")) { var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(dep); if (m != null) list.Add(m); }
            _sceneMats[scenePath] = list;
            return list;
        }

        /// <summary>Runtime-created / folder materials every planet needs (Digger terrain, project Resources).</summary>
        static List<Material> SharedMaterials()
        {
            const string key = "<shared>";
            if (_sceneMats.TryGetValue(key, out var list)) return list;
            list = new List<Material>();
            foreach (var folder in new[] { "Assets/DiggerData", "Assets/_Project/Resources" })
                if (UnityEditor.AssetDatabase.IsValidFolder(folder))
                    foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Material", new[] { folder }))
                    { var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)); if (m != null) list.Add(m); }
            _sceneMats[key] = list;
            return list;
        }
#endif

        /// <summary>Background step, every frame. <paramref name="budget"/> = materials (Editor) or variants (build) per frame.</summary>
        public static void Tick(int budget)
        {
#if UNITY_EDITOR
            if (!_allQueued)
            {
                _allQueued = true;
                var t0 = Time.realtimeSinceStartup;
                foreach (var m in SharedMaterials()) _queue.Enqueue(m);
                foreach (var s in UnityEditor.EditorBuildSettings.scenes)
                    if (s.enabled) foreach (var m in SceneMaterials(s.path)) _queue.Enqueue(m);
                Debug.Log($"[Xenoarch] Shader prewarm (Editor, async): {_queue.Count} materials queued in {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
            }
            for (int i = 0; i < budget && _queue.Count > 0; i++) Submit(_queue.Dequeue());
#else
            var c = Collection;
            if (c != null && !c.isWarmedUp) c.WarmUpProgressively(Mathf.Max(1, budget));
#endif
        }

        /// <summary>Start tracking (and, in the Editor, immediately async-compile) everything the given scene renders.</summary>
        public static Tracker TrackScene(string sceneName)
        {
            var t = new Tracker();
#if UNITY_EDITOR
            foreach (var m in SceneMaterials(ScenePath(sceneName))) { t.Add(m); Submit(m); }
            foreach (var m in SharedMaterials()) { t.Add(m); Submit(m); }
#endif
            return t;
        }

        static readonly List<Material> _runtime = new List<Material>();

        /// <summary>Materials built in code (site marker, dig border, dig ring): compile them in the background now and
        /// include them in every landing's loading-screen wait, so they never draw as the Editor's cyan placeholder
        /// the first time a scan shows them.</summary>
        public static void Warm(Material m)
        {
            if (m == null || _runtime.Contains(m)) return;
            _runtime.Add(m);
#if UNITY_EDITOR
            Submit(m);
#endif
        }

        /// <summary>Adds the runtime materials registered with <see cref="Warm"/> to a tracker.</summary>
        public static void TrackRuntime(Tracker t)
        {
            if (t == null) return;
            foreach (var m in _runtime) if (m != null) t.Add(m);
        }

        /// <summary>Also track (and async-compile) the materials on these renderers (player rig, landed shuttle...).</summary>
        public static void TrackRenderers(Tracker t, Transform root)
        {
#if UNITY_EDITOR
            if (t == null || root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials) { if (m == null) continue; t.Add(m); Submit(m); }
#endif
        }
    }
}
