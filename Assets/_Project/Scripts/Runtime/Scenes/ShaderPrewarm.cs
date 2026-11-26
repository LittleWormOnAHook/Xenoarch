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
        static void ResetStatics() { _queue.Clear(); _submitted.Clear(); _sceneMats.Clear(); _allQueued = false; _svc = null; _svcLoaded = false; _stuck.Clear(); _reported.Clear(); _wasCompiling = false; }

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
            var seen = new HashSet<string>();
            void AddPath(string path)
            {
                if (!path.EndsWith(".mat") || !seen.Add(path)) return;
                var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path); if (m != null) list.Add(m);
            }
            foreach (var folder in new[] { "Assets/DiggerData", "Assets/_Project/Resources" })
                if (UnityEditor.AssetDatabase.IsValidFolder(folder))
                    foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Material", new[] { folder }))
                        AddPath(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
            // Everything Resources and the project prefabs can spawn at runtime (shuttle, caches, pickups, artifacts, VFX,
            // via XenoarchConfig and the definition assets) - these appear mid-game, long after the loading screen.
            foreach (var folder in new[] { "Assets/_Project/Resources", "Assets/_Project/Prefabs", "Assets/_Project/Data" })
                if (UnityEditor.AssetDatabase.IsValidFolder(folder))
                    foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:Prefab t:ScriptableObject", new[] { folder }))
                        foreach (var dep in UnityEditor.AssetDatabase.GetDependencies(UnityEditor.AssetDatabase.GUIDToAssetPath(g), true))
                            AddPath(dep);
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

        // ------------------------------------------------------------------ warm pass
        // ShaderUtil.CompilePass / IsPassCompiled only cover the variant for the material's own keywords. The variant
        // actually drawn also carries URP's global keywords for the scene (main-light shadows, additional lights, fog,
        // SSAO, decals...), so a "compiled" material still showed the cyan placeholder the first time it appeared
        // (dig-site reveal, shuttle, loot). The warm pass draws every tracked material once on a tiny quad in front of
        // the real camera, behind the loading overlay, so the exact runtime variants get requested before the reveal.

        static readonly List<Material> _samples = new List<Material>();
        /// <summary>Representative code-made variants (plain Lit as used by soil / artifacts / nodes, Lit + emission,
        /// Unlit, Particles/Unlit) so every runtime `new Material` of these shaders is already warm.</summary>
        static void EnsureSamples()
        {
            if (_samples.Count > 0 && _samples[0] != null) return;
            _samples.Clear();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                _samples.Add(new Material(lit) { name = "Warm Lit (runtime)" });
                var e = new Material(lit) { name = "Warm Lit Emission (runtime)" };
                e.EnableKeyword("_EMISSION"); e.SetColor("_EmissionColor", Color.white);
                _samples.Add(e);
            }
            foreach (var n in new[] { "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Simple Lit" })
            { var sh = Shader.Find(n); if (sh != null) _samples.Add(new Material(sh) { name = $"Warm {n} (runtime)" }); }
            // transparent additive particles (FTL streaks / core) and the sun disc
            var pu = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (pu != null)
            {
                var a = new Material(pu) { name = "Warm Particles Additive (runtime)" };
                a.SetFloat("_Surface", 1f); a.SetFloat("_Blend", 2f); a.SetOverrideTag("RenderType", "Transparent");
                a.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); a.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); a.SetInt("_ZWrite", 0);
                a.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); a.EnableKeyword("_BLENDMODE_ADD"); a.renderQueue = 3000;
                _samples.Add(a);
            }
            var sun = Shader.Find("Xenoarch/SunDisc"); if (sun != null) _samples.Add(new Material(sun) { name = "Warm Sun Disc (runtime)" });
        }

        /// <summary>Shaders that can't (or needn't) be drawn on a plain quad: terrain (the scene's terrain draws itself),
        /// skybox, UI and hidden/internal shaders.</summary>
        static bool QuadDrawable(Shader s)
        {
            if (s == null || !s.isSupported) return false;
            string n = s.name;
            return !(n.StartsWith("Hidden/") || n.StartsWith("UI/") || n.Contains("Skybox") || n.Contains("Terrain") || n.StartsWith("TextMeshPro") || n.StartsWith("Sprites/Mask"));
        }

        /// <summary>A hidden one-shot draw of materials in front of the camera (under the loading screen).</summary>
        public sealed class WarmPass
        {
            GameObject _root;
            readonly HashSet<Material> _drawn = new HashSet<Material>();
            int _lastSync = -1;
            static Mesh _quad;
            public int Frames { get; private set; }
            public int Count => _drawn.Count;

            public static WarmPass Begin(Tracker t, Camera cam)
            {
                if (cam == null) return null;
                EnsureSamples();
                var w = new WarmPass { _root = new GameObject("[Xenoarch Shader Warm Pass]") };
                w._root.hideFlags = HideFlags.DontSave;
                w._root.transform.SetParent(cam.transform, false);
                w._root.transform.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 1f);
                w._root.layer = 0; // Default: every gameplay camera renders it (the rig's own layer may be culled)
                foreach (var m in _samples) w.Add(m);
                foreach (var m in _runtime) w.Add(m);
                w.Sync(t);
                return w;
            }

            /// <summary>Adds quads for materials the tracker gained since the last call (player rig, landed shuttle).</summary>
            public void Sync(Tracker t)
            {
                if (_root == null || t == null || t.mats.Count == _lastSync) return;
                _lastSync = t.mats.Count;
                foreach (var m in t.mats) Add(m);
                foreach (var m in _runtime) Add(m);
            }

            public void Tick() { if (_root != null) Frames++; }

            void Add(Material m)
            {
                if (m == null || !QuadDrawable(m.shader) || !_drawn.Add(m)) return;
                int i = _drawn.Count - 1;
                const int cols = 40; const float step = 0.012f;
                var go = new GameObject(m.name) { hideFlags = HideFlags.DontSave, layer = _root.layer };
                go.transform.SetParent(_root.transform, false);
                go.transform.localPosition = new Vector3((i % cols - cols * 0.5f) * step, ((i / cols) % 40 - 20) * step, 0f);
                go.transform.localScale = Vector3.one * step;
                if (m.shader.name.Contains("Decal"))
                {
                    var d = go.AddComponent<UnityEngine.Rendering.Universal.DecalProjector>();
                    d.material = m; d.size = new Vector3(step, step, step * 4f);
                    return;
                }
                go.AddComponent<MeshFilter>().sharedMesh = Quad();
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; // also requests the ShadowCaster variant
                r.receiveShadows = true;
            }

            public void End()
            {
                if (_root != null) Object.Destroy(_root);
                _root = null;
            }

            static Mesh Quad()
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "WarmQuad", hideFlags = HideFlags.DontSave };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
                _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                _quad.uv2 = _quad.uv;
                _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                _quad.RecalculateNormals(); _quad.RecalculateTangents(); _quad.RecalculateBounds();
                return _quad;
            }
        }

        // ------------------------------------------------------------------ dev log
        static float _auditNext; static bool _wasCompiling;
        static readonly HashSet<Material> _reported = new HashSet<Material>();

        /// <summary>Dev toggle (XenoarchConfig.logShaderWarm): outside the loading screen, logs a [ShaderWarm] line when the
        /// Editor starts compiling shaders and for each visible material whose pass is not compiled yet (i.e. what drew
        /// cyan), so missing entries can be added to the warm-up.</summary>
        public static void DevAudit(bool enabled, bool loading)
        {
#if UNITY_EDITOR
            if (!enabled || loading) { _wasCompiling = false; return; }
            bool compiling = UnityEditor.ShaderUtil.anythingCompiling;
            if (compiling && !_wasCompiling) Debug.Log($"[ShaderWarm] shader compiler started outside the loading screen (t={Time.realtimeSinceStartup:0.0}s)");
            _wasCompiling = compiling;
            if (!compiling || Time.realtimeSinceStartup < _auditNext) return;
            _auditNext = Time.realtimeSinceStartup + 0.5f;
            foreach (var r in Object.FindObjectsByType<Renderer>())
            {
                if (r == null || !r.isVisible) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null || _reported.Contains(m)) continue;
                    for (int i = 0; i < m.passCount; i++)
                        if (!UnityEditor.ShaderUtil.IsPassCompiled(m, i))
                        {
                            _reported.Add(m);
                            Debug.Log($"[ShaderWarm] not ready while visible: '{m.name}' ({m.shader.name}, pass {i} {m.GetPassName(i)}) on '{r.name}' in {r.gameObject.scene.name}");
                            break;
                        }
                }
            }
#endif
        }
    }
}
