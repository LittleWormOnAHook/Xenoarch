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
    /// Runtime dig site built from an <see cref="ArchaeologicalSiteDefinition"/>: owns the <see cref="ExcavationGrid"/>,
    /// renders it as a stepped trench mesh (with collider) inside the terrain hole the editor cut for the site, places
    /// the site's buried artifacts, applies tool strokes, saves to <see cref="ExcavationService"/>, and mirrors accepted
    /// strokes to an <see cref="IExcavationSurface"/> (Digger PRO) when the scene has one.
    /// </summary>
    public sealed class ExcavationSite : MonoBehaviour
    {
        public ArchaeologicalSiteDefinition Definition { get; private set; }
        public ExcavationGrid Grid { get; private set; }
        readonly List<BuriedArtifact> _pieces = new List<BuriedArtifact>();
        Mesh _mesh;
        MeshCollider _collider;
        IExcavationSurface _surface;
        bool _dirty;
        float _saveAt;

        public static readonly List<ExcavationSite> Active = new List<ExcavationSite>();
        void OnEnable() => Active.Add(this);
        void OnDisable() { Active.Remove(this); Flush(); }

        public static ExcavationSite Create(ArchaeologicalSiteDefinition def, float surfaceY, UnityEngine.SceneManagement.Scene scene)
        {
            var go = new GameObject($"DigSite {def.Id}");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var site = go.AddComponent<ExcavationSite>();
            site.Init(def, surfaceY);
            return site;
        }

        void Init(ArchaeologicalSiteDefinition def, float surfaceY)
        {
            Definition = def;
            Grid = ExcavationGrid.ForSite(def.radius, def.depth, def.cellSize > 0.05f ? def.cellSize : 0.25f);
            transform.position = new Vector3(def.position.x - Grid.SizeX * 0.5f, surfaceY, def.position.z - Grid.SizeZ * 0.5f);

            var arts = XenoarchRuntime.Session?.Artifacts;
            for (int i = 0; i < def.artifacts.Count; i++)
            {
                var sp = def.artifacts[i];
                var adef = arts?.Definition(sp.artifactId);
                if (sp == null || adef == null) continue;
                int y = Mathf.Clamp(sp.cell.y, 1, Grid.ny - 2);
                Grid.artifacts.Add(new GridArtifact
                {
                    index = i, x = Mathf.Clamp(sp.cell.x, 0, Grid.nx - 1), y = y, z = Mathf.Clamp(sp.cell.z, 0, Grid.nz - 1),
                    footprint = Mathf.Max(1, sp.footprint), fragility = adef.fragility, condition = adef.buriedCondition,
                });
            }
            var ex = XenoarchRuntime.Session?.Excavation;
            bool restored = ex != null && ex.Load(def.Id, Grid);

            var mf = gameObject.AddComponent<MeshFilter>();
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterials = Materials(def.bodyId);
            _mesh = new Mesh { name = $"Trench {def.Id}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            mf.sharedMesh = _mesh;
            _collider = gameObject.AddComponent<MeshCollider>();
            Rebuild();

            foreach (var a in Grid.artifacts)
                if (!a.recovered) _pieces.Add(BuriedArtifact.Spawn(this, a, arts.Definition(def.artifacts[a.index].artifactId)));

            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb is IExcavationSurface s) { _surface = s; break; }
            Debug.Log($"[Xenoarch] Dig site {def.Id}: {Grid.nx}x{Grid.nz}x{Grid.ny} cells of {Grid.cell} m, {_pieces.Count} buried artifact(s){(restored ? ", restored from save" : "")}{(_surface != null ? ", mirroring to " + _surface.GetType().Name : "")}");
        }

        public Vector3 CellCentre(int x, int y, int z, float depthFrac = 0.5f) =>
            transform.TransformPoint(new Vector3((x + 0.5f) * Grid.cell, -(y + depthFrac) * Grid.cell, (z + 0.5f) * Grid.cell));

        public bool Contains(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            return l.x >= 0f && l.z >= 0f && l.x <= Grid.SizeX && l.z <= Grid.SizeZ;
        }

        /// <summary>Apply one tool stroke at a world point on this site's surface.</summary>
        public StrokeResult Stroke(Vector3 worldHit, DigToolSpec tool)
        {
            var l = transform.InverseTransformPoint(worldHit);
            var r = Grid.Stroke(l.x, l.z, -l.y, tool);
            if (!r.accepted) return r;
            Rebuild();
            foreach (var p in _pieces) if (p != null) p.Refresh();
            _dirty = true; _saveAt = Time.unscaledTime + 1f;
            if (_surface != null && !_surface.IsBusy)
                _surface.Apply(new ExcavationEdit { position = worldHit, radius = tool.radius, strength = Mathf.Clamp01(r.removed), stroke = tool.damage <= 0f ? ExcavationStroke.Smooth : ExcavationStroke.Remove, materialIndex = (int)r.material });
            return r;
        }

        public float Exposure(GridArtifact a) => Grid.Exposure(a);

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

        static Material[] Materials(string bodyId)
        {
            string key = bodyId ?? "";
            if (MatCache.TryGetValue(key, out var m) && m != null && m[0] != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material[4];
            for (int i = 0; i < 4; i++)
            {
                m[i] = new Material(sh) { name = $"Soil {(SoilMaterial)i} ({key})" };
                var c = SoilPalette.For(bodyId, (SoilMaterial)i);
                m[i].SetColor("_BaseColor", c); m[i].color = c;
                m[i].SetFloat("_Smoothness", 0.08f);
            }
            MatCache[key] = m;
            return m;
        }

        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<int>[] _t = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

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
                    float x0 = x * c, x1 = x0 + c, z0 = z * c, z1 = z0 + c, y = -d;
                    Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up, mat);
                    // Walls toward shallower neighbours (outside the grid counts as the untouched surface).
                    Wall(x, z, d, x + 1, z, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), Vector3.left);
                    Wall(x, z, d, x - 1, z, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), Vector3.right);
                    Wall(x, z, d, x, z + 1, new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), Vector3.back);
                    Wall(x, z, d, x, z - 1, new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), Vector3.forward);
                }
            _mesh.Clear();
            _mesh.subMeshCount = 4;
            _mesh.SetVertices(_v); _mesh.SetNormals(_n); _mesh.SetUVs(0, _uv);
            for (int i = 0; i < 4; i++) _mesh.SetTriangles(_t[i], i, false);
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
