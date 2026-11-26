using System.Collections.Generic;
using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>Soil colours per body (shared by the runtime trench mesh and the editor-made terrain layer).</summary>
    public static class SoilPalette
    {
        public static Color Surface(string bodyId) => bodyId != null && bodyId.Contains("mars") ? new Color(0.56f, 0.31f, 0.19f) : new Color(0.47f, 0.47f, 0.48f);

        public static Color For(string bodyId, SoilMaterial m)
        {
            var s = Surface(bodyId);
            return m switch
            {
                SoilMaterial.Dust => s,
                SoilMaterial.Loose => s * 0.82f,
                SoilMaterial.Compacted => s * 0.64f,
                _ => new Color(0.22f, 0.21f, 0.21f),
            };
        }
    }

    /// <summary>
    /// Runtime dig site built from an <see cref="ArchaeologicalSiteDefinition"/>: owns the <see cref="ExcavationGrid"/>
    /// (the source of truth: cells, hardness, artifact exposure/condition, saves), follows the terrain surface per column,
    /// places the buried finds and applies tool strokes. When the scene has an <see cref="IExcavationSurface"/> (Digger
    /// PRO) every cut cell is carved there and the dug state is replayed into it on load; otherwise a stepped trench mesh
    /// (with collider) renders the grid. A square border marks the site once it is pinpointed.
    /// </summary>
    public sealed class ExcavationSite : MonoBehaviour
    {
        public ArchaeologicalSiteDefinition Definition { get; private set; }
        public ExcavationGrid Grid { get; private set; }
        public bool MirrorsToSurface => _surface != null;
        readonly List<BuriedArtifact> _pieces = new List<BuriedArtifact>();
        Mesh _mesh;
        MeshCollider _collider;
        IExcavationSurface _surface;
        LineRenderer _border;
        GameObject _fence;   // posts + rope + ground circle (built once, toggled with the border)
        Color _borderColor = new Color(1f, 0.62f, 0.15f, 0.9f);
        bool _dirty;
        float _saveAt;

        public static readonly List<ExcavationSite> Active = new List<ExcavationSite>();
        void OnEnable() => Active.Add(this);
        void OnDisable() { Active.Remove(this); Flush(); }

        public static ExcavationSite Find(string siteId)
        {
            foreach (var s in Active) if (s != null && s.Definition != null && s.Definition.Id == siteId) return s;
            return null;
        }

        /// <summary>The site whose footprint contains the world point (x/z), if any.</summary>
        public static ExcavationSite At(Vector3 world)
        {
            foreach (var s in Active) if (s != null && s.Grid != null && s.Contains(world)) return s;
            return null;
        }

        public static ExcavationSite Create(ArchaeologicalSiteDefinition def, float surfaceY, UnityEngine.SceneManagement.Scene scene)
        {
            var go = new GameObject($"DigSite {def.Id}");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var site = go.AddComponent<ExcavationSite>();
            site.Init(def, surfaceY);
            return site;
        }

        /// <summary>Terrain height at a world x/z (any active terrain), or null.</summary>
        public static float? TerrainHeight(Vector3 p)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                if (t == null || t.terrainData == null) continue;
                var tp = t.transform.position; var size = t.terrainData.size;
                if (p.x >= tp.x && p.z >= tp.z && p.x <= tp.x + size.x && p.z <= tp.z + size.z) return tp.y + t.SampleHeight(p);
            }
            return null;
        }

        /// <summary>Cells per side / layers an artifact occupies, from its size (parts of big finds show first).</summary>
        public static Vector2Int FootprintFor(ArtifactDefinition def, float cell, int minFootprint)
        {
            var e = BuriedArtifact.Extents(def);
            int f = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(e.x, e.z) * 0.9f / cell), Mathf.Max(1, minFootprint), 3);
            int h = Mathf.Clamp(Mathf.CeilToInt(e.y * 0.9f / cell), 1, 2);
            return new Vector2Int(f, h);
        }

        void Init(ArchaeologicalSiteDefinition def, float surfaceY)
        {
            Definition = def;
            foreach (var mb in FindObjectsByType<MonoBehaviour>())
                if (mb is IExcavationSurface s && mb.gameObject.scene == gameObject.scene) { _surface = s; break; }

            var style = def.style;
            Grid = ExcavationGrid.ForSite(def.radius, def.depth, def.cellSize, style != null ? style.Profile() : null);
            if (style == null) Grid.layers = SoilLayer.DefaultProfile(SoilPalette.Surface(def.bodyId));
            else _borderColor = style.borderColor;
            if (_surface != null) Grid.tolerance = 3f;
            var corner = new Vector3(def.position.x - Grid.SizeX * 0.5f, surfaceY, def.position.z - Grid.SizeZ * 0.5f);
            transform.position = corner;

            // Per-column surface heights from the terrain (sites on uneven ground).
            if (TerrainHeight(def.position).HasValue)
            {
                Grid.surface = new float[Grid.nx * Grid.nz];
                for (int x = 0; x < Grid.nx; x++)
                    for (int z = 0; z < Grid.nz; z++)
                    {
                        var w = corner + new Vector3((x + 0.5f) * Grid.cell, 0f, (z + 0.5f) * Grid.cell);
                        Grid.surface[x + Grid.nx * z] = (TerrainHeight(w) ?? surfaceY) - surfaceY;
                    }
            }

            var arts = XenoarchRuntime.Session?.Artifacts;
            for (int i = 0; i < def.artifacts.Count; i++)
            {
                var sp = def.artifacts[i];
                var adef = sp != null ? arts?.Definition(sp.artifactId) : null;
                if (adef == null) continue;
                var fh = FootprintFor(adef, Grid.cell, sp.footprint);
                int y = Mathf.Clamp(sp.cell.y, 1, Grid.ny - 1 - fh.y);
                Grid.artifacts.Add(new GridArtifact
                {
                    index = i, x = Mathf.Clamp(sp.cell.x, 0, Grid.nx - fh.x), y = y, z = Mathf.Clamp(sp.cell.z, 0, Grid.nz - fh.x),
                    footprint = fh.x, height = fh.y, fragility = adef.fragility, condition = adef.buriedCondition,
                });
            }
            var ex = XenoarchRuntime.Session?.Excavation;
            bool restored = ex != null && ex.Load(def.Id, Grid);

            if (_surface == null)
            {
                var mf = gameObject.AddComponent<MeshFilter>();
                var mr = gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterials = Materials(def.Id, Grid);
                _mesh = new Mesh { name = $"Trench {def.Id}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                _mesh.MarkDynamic();
                mf.sharedMesh = _mesh;
                _collider = gameObject.AddComponent<MeshCollider>();
                Rebuild();
            }
            else if (restored) StartCoroutine(Replay());

            foreach (var a in Grid.artifacts)
                if (!a.recovered) _pieces.Add(BuriedArtifact.Spawn(this, a, arts.Definition(def.artifacts[a.index].artifactId)));

            var xs = XenoarchRuntime.Session?.Exploration;
            if (xs != null && xs.Site(def.Id).revealLevel >= (int)SiteRevealLevel.Pinpointed) ShowBorder(true);
            Debug.Log($"[Xenoarch] Dig site {def.Id} ({(style != null ? style.displayName : "default soil")}): {Grid.nx}x{Grid.nz}x{Grid.ny} cells of {Grid.cell} m, {_pieces.Count} buried artifact(s){(restored ? ", restored from save" : "")}{(_surface != null ? ", carving " + _surface.GetType().Name : ", trench mesh")}");
        }

        public Vector3 CellCentre(int x, int y, int z, float depthFrac = 0.5f) =>
            transform.TransformPoint(new Vector3((x + 0.5f) * Grid.cell, Grid.Surface(x, z) - (y + depthFrac) * Grid.cell, (z + 0.5f) * Grid.cell));

        public bool Contains(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            return l.x >= 0f && l.z >= 0f && l.x <= Grid.SizeX && l.z <= Grid.SizeZ;
        }

        /// <summary>Apply one tool stroke at a world point on this site's surface.</summary>
        public StrokeResult Stroke(Vector3 worldHit, DigToolSpec tool)
        {
            var l = transform.InverseTransformPoint(worldHit);
            // The Digger carve is at least CarveRadius wide, so the grid stroke covers the same footprint (otherwise the hole
            // looks dug where the grid still says "surface", and the next aim there reads depth 0).
            if (_surface != null) tool.radius = Mathf.Max(tool.radius, CarveRadius * 0.8f);
            var r = Grid.StrokeAt(l.x, l.z, l.y, tool);
            if (!r.accepted) return r;
            if (_mesh != null) Rebuild();
            foreach (var p in _pieces) if (p != null) p.Refresh();
            _dirty = true; _saveAt = Time.unscaledTime + 1f;
            if (_surface != null && r.cut != null && r.cut.Count > 0) CarveStroke(r.cut, tool.radius);
            return r;
        }

        /// <summary>Radius of one Digger carve (never below Digger's voxel size).</summary>
        public float CarveRadius => _surface != null ? Mathf.Max(Grid.cell * 0.8f, _surface.MinRadius * 1.25f) : Grid.cell * 0.72f;

        /// <summary>
        /// One Digger edit per stroke (ZEN-8): a full-strength sphere centred over the cut columns whose bottom sits just
        /// below the deepest new floor. The old path queued one partial-strength sphere per cut cell (dozens per stroke,
        /// often shallower than a Digger voxel), which flooded Digger's async buffer (lag, the hole showing up late) and
        /// mostly just repainted the texture instead of visibly carving.
        /// </summary>
        void CarveStroke(List<Vector3Int> cut, float strokeRadius)
        {
            float minFloor = float.MaxValue, sx = 0f, sz = 0f; int deepest = 0;
            foreach (var c in cut)
            {
                minFloor = Mathf.Min(minFloor, Grid.FloorY(c.x, c.z));
                sx += (c.x + 0.5f) * Grid.cell; sz += (c.z + 0.5f) * Grid.cell;
                deepest = Mathf.Max(deepest, c.y);
            }
            float rad = Mathf.Max(CarveRadius, strokeRadius);
            var centre = transform.TransformPoint(new Vector3(sx / cut.Count, minFloor, sz / cut.Count)) + transform.up * (rad * 0.9f);
            QueueCarve(centre, rad, Grid.LayerAt(deepest).diggerTexture);
        }

        /// <summary>One carve for a whole column down to its current floor (load replay).</summary>
        bool CarveColumn(int x, int z)
        {
            float rad = CarveRadius;
            var pos = transform.TransformPoint(new Vector3((x + 0.5f) * Grid.cell, Grid.FloorY(x, z), (z + 0.5f) * Grid.cell)) + transform.up * (rad * 0.92f);
            int top = Mathf.Min(Grid.TopLayer(x, z), Grid.ny - 1);
            return Carve(pos, rad, Grid.LayerAt(top).diggerTexture);
        }

        bool Carve(Vector3 pos, float rad, int texture) => _surface.Apply(new ExcavationEdit
        {
            position = pos, radius = rad, strength = 1f, stroke = ExcavationStroke.Remove, materialIndex = texture,
        });

        const int MaxPendingCarves = 24;
        void QueueCarve(Vector3 pos, float rad, int texture)
        {
            // Digger refuses edits while its buffer is full: keep them and retry (a deeper carve later in the queue
            // covers older ones, so the oldest are dropped first if the player out-digs Digger for a long time).
            if (_pendingCarves.Count == 0 && Carve(pos, rad, texture)) return;
            if (_pendingCarves.Count >= MaxPendingCarves) _pendingCarves.Dequeue();
            _pendingCarves.Enqueue((pos, rad, texture));
        }

        /// <summary>Re-carves the saved dug state into the Digger surface (Digger edits are not persisted separately).</summary>
        bool _replaying;

        /// <summary>Loading-screen gate: a dig site is still re-carving its saved hole, or Digger is still applying edits.</summary>
        public static string BusyStatus()
        {
            foreach (var s in Active)
                if (s != null && (s._replaying || (s._surface != null && s._surface.IsBusy))) return "Preparing excavation terrain";
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RegisterGate()
        {
            if (!Scenes.SceneLoader.ReadyGates.Contains(BusyStatus)) Scenes.SceneLoader.ReadyGates.Add(BusyStatus);
            Scenes.SceneLoader.GateProgress[BusyStatus] = BusyProgress;
        }

        int _replayDone, _replayTotal;
        /// <summary>Loading-bar progress of the terrain rebuild: replayed columns / all columns per site (Digger still
        /// applying edits after the replay counts as 95%).</summary>
        public static float BusyProgress()
        {
            float sum = 0f; int n = 0;
            foreach (var s in Active)
            {
                if (s == null) continue;
                n++;
                if (s._replaying) sum += s._replayTotal > 0 ? (float)s._replayDone / s._replayTotal : 0f;
                else sum += (s._surface != null && s._surface.IsBusy) ? 0.95f : 1f;
            }
            return n == 0 ? 1f : sum / n;
        }

        System.Collections.IEnumerator Replay()
        {
            _replaying = true; _replayDone = 0; _replayTotal = Grid.nx * Grid.nz;
            yield return null;
            int ops = 0;
            for (int x = 0; x < Grid.nx; x++)
                for (int z = 0; z < Grid.nz; z++, _replayDone++)
                {
                    if (Grid.TopDepth(x, z) < 0.01f) continue;   // untouched column
                    while (!CarveColumn(x, z)) yield return null;
                    ops++;
                }
            _replaying = false;
            Debug.Log($"[Xenoarch] Dig site {Definition.Id}: replayed {ops} carve op(s) into the terrain");
        }

        public float Exposure(GridArtifact a) => Grid.Exposure(a);

        // ------------------------------------------------------------------ border
        /// <summary>Square outline around the site footprint, following the ground.</summary>
        public void ShowBorder(bool on)
        {
            if (!on) { if (_border != null) _border.enabled = false; if (_fence != null) _fence.SetActive(false); return; }
            if (_border == null)
            {
                var go = new GameObject("Site Border");
                go.transform.SetParent(transform, false);
                _border = go.AddComponent<LineRenderer>();
                _border.useWorldSpace = true; _border.loop = true;
                _border.widthMultiplier = 0.06f; _border.numCornerVertices = 2;
                _border.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _border.receiveShadows = false;
                _border.sharedMaterial = BorderMaterial(_borderColor); _border.startColor = _border.endColor = _borderColor;
                var pts = new List<Vector3>();
                float sx = Grid.SizeX, sz = Grid.SizeZ, step = 0.5f;
                void Edge(Vector3 a, Vector3 b)
                {
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / step));
                    for (int i = 0; i < n; i++)
                    {
                        var w = transform.TransformPoint(Vector3.Lerp(a, b, i / (float)n));
                        w.y = (TerrainHeight(w) ?? transform.position.y) + 0.05f;
                        pts.Add(w);
                    }
                }
                Edge(Vector3.zero, new Vector3(sx, 0, 0)); Edge(new Vector3(sx, 0, 0), new Vector3(sx, 0, sz));
                Edge(new Vector3(sx, 0, sz), new Vector3(0, 0, sz)); Edge(new Vector3(0, 0, sz), Vector3.zero);
                _border.positionCount = pts.Count; _border.SetPositions(pts.ToArray());
            }
            _border.enabled = true;
            if (_fence == null) BuildFence();
            _fence.SetActive(true);
        }

        public static readonly Color FencePostColor = new Color(0.86f, 0.8f, 0.68f, 1f);
        public static readonly Color FenceRopeColor = new Color(1f, 0.45f, 0.1f, 1f);
        public static readonly Color SiteCircleColor = new Color(1f, 0.78f, 0.3f, 0.75f);
        static Mesh _postMesh;

        /// <summary>
        /// Survey fence around a marked site (ZEN-8): stakes every ~2.5 m along the grid border with an orange rope at
        /// waist height, plus a large ground circle around the whole site. Rebuilt from the reveal level on every scene load,
        /// so it persists with the save. No colliders (purely visual), shared prewarmed materials.
        /// </summary>
        void BuildFence()
        {
            _fence = new GameObject("Site Fence");
            _fence.transform.SetParent(transform, false);
            float sx = Grid.SizeX, sz = Grid.SizeZ;
            var corners = new[] { Vector3.zero, new Vector3(sx, 0, 0), new Vector3(sx, 0, sz), new Vector3(0, 0, sz) };
            var rope = new List<Vector3>();
            if (_postMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _postMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }
            var postMat = BorderMaterial(FencePostColor);
            const float spacing = 2.5f, postH = 1.1f, ropeH = 0.9f;
            for (int e = 0; e < 4; e++)
            {
                Vector3 a = corners[e], b = corners[(e + 1) % 4];
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / spacing));
                for (int i = 0; i < n; i++)
                {
                    var w = transform.TransformPoint(Vector3.Lerp(a, b, i / (float)n));
                    float gy = TerrainHeight(w) ?? transform.position.y;
                    var post = new GameObject("Stake");
                    post.transform.SetParent(_fence.transform, true);
                    post.transform.position = new Vector3(w.x, gy + postH * 0.5f - 0.1f, w.z);
                    post.transform.localScale = new Vector3(0.07f, postH * 0.5f, 0.07f);   // unit cylinder is 2 m tall
                    post.AddComponent<MeshFilter>().sharedMesh = _postMesh;
                    var mr = post.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = postMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rope.Add(new Vector3(w.x, gy + ropeH, w.z));
                }
            }
            var ropeLr = NewLine("Fence Rope", FenceRopeColor, 0.035f);
            ropeLr.loop = true; ropeLr.positionCount = rope.Count; ropeLr.SetPositions(rope.ToArray());

            // Large ground circle around the whole site.
            var centre = transform.TransformPoint(new Vector3(sx * 0.5f, 0f, sz * 0.5f));
            float r = Mathf.Sqrt(sx * sx + sz * sz) * 0.5f + 1.5f;
            int seg = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * r / 1f), 24, 256);
            var ring = new Vector3[seg];
            for (int i = 0; i < seg; i++)
            {
                float t = i / (float)seg * Mathf.PI * 2f;
                var w = centre + new Vector3(Mathf.Cos(t) * r, 0f, Mathf.Sin(t) * r);
                w.y = (TerrainHeight(w) ?? centre.y) + 0.06f;
                ring[i] = w;
            }
            var circle = NewLine("Site Circle", SiteCircleColor, 0.18f);
            circle.loop = true; circle.positionCount = seg; circle.SetPositions(ring);
        }

        LineRenderer NewLine(string name, Color c, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_fence.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.widthMultiplier = width; lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.sharedMaterial = BorderMaterial(c); lr.startColor = lr.endColor = c;
            return lr;
        }

        public bool BorderVisible => _border != null && _border.enabled;

        static readonly Dictionary<Color, Material> _borderMats = new Dictionary<Color, Material>();
        /// <summary>Shared unlit border material per colour (prewarmed, so the first scan reveal never draws it cyan).</summary>
        public static Material BorderMaterial(Color c)
        {
            if (_borderMats.TryGetValue(c, out var m) && m != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            m = new Material(sh) { name = "Site Border (runtime)" };
            m.SetColor("_BaseColor", c); m.color = c;
            _borderMats[c] = m;
            DMI.Xenoarch.Scenes.ShaderPrewarm.Warm(m);
            return m;
        }

        /// <summary>Builds the code-made materials a scan or dig can show (border, beacon) before the player needs them.</summary>
        public static void PrewarmRuntimeMaterials()
        {
            BorderMaterial(new Color(1f, 0.62f, 0.15f, 0.9f));
            BorderMaterial(FencePostColor); BorderMaterial(FenceRopeColor); BorderMaterial(SiteCircleColor);
            DMI.Xenoarch.Exploration.SiteMarkerDirector.MarkerMaterial();
        }

        internal void OnRecovered(BuriedArtifact piece, string instanceId)
        {
            piece.Grid.recovered = true;
            _pieces.Remove(piece);
            XenoarchRuntime.Session?.Excavation.Store(Definition.Id, Grid, instanceId, piece.Grid.index);
            _dirty = false;
        }

        readonly Queue<(Vector3 pos, float rad, int tex)> _pendingCarves = new Queue<(Vector3, float, int)>();
        /// <summary>Carves waiting for Digger's buffer (the hole is still catching up with the grid).</summary>
        public int PendingCarves => _pendingCarves.Count;

        void Update()
        {
            if (_dirty && Time.unscaledTime >= _saveAt) Flush();
            for (int n = 0; n < 4 && _pendingCarves.Count > 0 && !_replaying; n++)   // capped per frame
            {
                var (pos, rad, tex) = _pendingCarves.Peek();
                if (!Carve(pos, rad, tex)) break;
                _pendingCarves.Dequeue();
            }
        }

        public void Flush()
        {
            if (!_dirty || Grid == null) return;
            _dirty = false;
            XenoarchRuntime.Session?.Excavation.Store(Definition.Id, Grid);
        }

        // ------------------------------------------------------------------ mesh
        static readonly Dictionary<string, Material[]> MatCache = new Dictionary<string, Material[]>();
        const int Kinds = 6;

        static Material[] Materials(string key, ExcavationGrid g)
        {
            if (MatCache.TryGetValue(key, out var m) && m != null && m[0] != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material[Kinds];
            for (int i = 0; i < Kinds; i++)
            {
                var c = SoilLayer.Bedrock.color;
                foreach (var l in g.layers) if ((int)l.kind == i) { c = l.color; break; }
                m[i] = new Material(sh) { name = $"Soil {(SoilMaterial)i} ({key})" };
                m[i].SetColor("_BaseColor", c); m[i].color = c;
                m[i].SetFloat("_Smoothness", 0.08f);
            }
            MatCache[key] = m;
            return m;
        }

        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int>[] _t = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };

        void Rebuild()
        {
            var g = Grid; float c = g.cell;
            _v.Clear(); _n.Clear(); _uv.Clear(); foreach (var t in _t) t.Clear();
            for (int x = 0; x < g.nx; x++)
                for (int z = 0; z < g.nz; z++)
                {
                    float d = g.TopDepth(x, z);
                    int top = g.TopLayer(x, z);
                    var mat = top >= g.ny ? SoilMaterial.Bedrock : g.MaterialAt(top);
                    float x0 = x * c, x1 = x0 + c, z0 = z * c, z1 = z0 + c, y = g.Surface(x, z) - d;
                    Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up, mat);
                    // Walls toward shallower neighbours (outside the grid counts as the untouched surface).
                    Wall(x, z, d, x + 1, z, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), Vector3.left);
                    Wall(x, z, d, x - 1, z, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), Vector3.right);
                    Wall(x, z, d, x, z + 1, new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), Vector3.back);
                    Wall(x, z, d, x, z - 1, new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), Vector3.forward);
                }
            _mesh.Clear();
            _mesh.subMeshCount = Kinds;
            _mesh.SetVertices(_v); _mesh.SetNormals(_n); _mesh.SetUVs(0, _uv);
            for (int i = 0; i < Kinds; i++) _mesh.SetTriangles(_t[i], i, false);
            _mesh.RecalculateBounds();
            _collider.sharedMesh = null;
            _collider.sharedMesh = _mesh;
        }

        void Wall(int x, int z, float d, int nx, int nz, Vector3 a, Vector3 b, Vector3 normal)
        {
            var g = Grid;
            float dn = g.InBounds(nx, nz) ? g.TopDepth(nx, nz) : 0f;
            if (dn >= d - 1e-4f) return;
            int y0 = Mathf.FloorToInt(dn / g.cell), y1 = Mathf.Min(g.ny - 1, Mathf.FloorToInt((d - 1e-4f) / g.cell));
            for (int y = y0; y <= y1; y++)
            {
                float top = Mathf.Max(dn, y * g.cell), bot = Mathf.Min(d, (y + 1) * g.cell);
                if (bot - top < 1e-4f) continue;
                Quad(new Vector3(a.x, -top, a.z), new Vector3(b.x, -top, b.z), new Vector3(b.x, -bot, b.z), new Vector3(a.x, -bot, a.z), normal, g.MaterialAt(y));
            }
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, SoilMaterial m)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++) _n.Add(normal);
            var o = transform.position;
            bool flat = normal.y > 0.5f;
            foreach (var p in new[] { a, b, c, d })
                _uv.Add(flat ? new Vector2(p.x + o.x, p.z + o.z) * 0.5f : new Vector2((p.x + o.x + p.z + o.z) * 0.5f, p.y * 0.5f));
            var tris = _t[(int)m];
            bool keep = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 0f;
            if (keep) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
        }
    }
}
