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
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
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
            if (_surface != null && r.cut != null)
                foreach (var c in r.cut) Carve(c.x, c.y, c.z, Grid.Fill(c.x, c.y, c.z) == 0 ? 1f : 0.6f);
            return r;
        }

        /// <summary>Radius of one Digger carve (never below Digger's voxel size).</summary>
        public float CarveRadius => _surface != null ? Mathf.Max(Grid.cell * 0.8f, _surface.MinRadius * 1.25f) : Grid.cell * 0.72f;

        bool Carve(int x, int y, int z, float strength)
        {
            float rad = CarveRadius;
            // Sphere sits so its bottom is the bottom of the removed cell: the visual depth then matches the grid depth
            // instead of a sphere centred mid-cell digging half a radius too deep.
            var pos = CellCentre(x, y, z, 1f) + transform.up * (rad * 0.92f);
            return _surface.Apply(new ExcavationEdit
            {
                position = pos, radius = rad, strength = strength,
                stroke = ExcavationStroke.Remove, materialIndex = Grid.LayerAt(y).diggerTexture,
            });
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
        static void RegisterGate() { if (!Scenes.SceneLoader.ReadyGates.Contains(BusyStatus)) Scenes.SceneLoader.ReadyGates.Add(BusyStatus); }

        System.Collections.IEnumerator Replay()
        {
            _replaying = true;
            yield return null;
            int ops = 0;
            for (int x = 0; x < Grid.nx; x++)
                for (int z = 0; z < Grid.nz; z++)
                    for (int y = 0; y < Grid.ny; y++)
                    {
                        byte f = Grid.Fill(x, y, z);
                        if (f == 255) break;
                        while (!Carve(x, y, z, f == 0 ? 1f : 0.6f)) yield return null;
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
            if (!on) { if (_border != null) _border.enabled = false; return; }
            if (_border == null)
            {
                var go = new GameObject("Site Border");
                go.transform.SetParent(transform, false);
                _border = go.AddComponent<LineRenderer>();
                _border.useWorldSpace = true; _border.loop = true;
                _border.widthMultiplier = 0.06f; _border.numCornerVertices = 2;
                _border.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _border.receiveShadows = false;
                var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                var m = new Material(sh) { name = "Site Border (runtime)" };
                m.SetColor("_BaseColor", _borderColor); m.color = _borderColor;
                _border.sharedMaterial = m; _border.startColor = _border.endColor = _borderColor;
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
        }

        public bool BorderVisible => _border != null && _border.enabled;

        internal void OnRecovered(BuriedArtifact piece, string instanceId)
        {
            piece.Grid.recovered = true;
            _pieces.Remove(piece);
            XenoarchRuntime.Session?.Excavation.Store(Definition.Id, Grid, instanceId, piece.Grid.index);
            _dirty = false;
        }

        void Update() { if (_dirty && Time.unscaledTime >= _saveAt) Flush(); }

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
