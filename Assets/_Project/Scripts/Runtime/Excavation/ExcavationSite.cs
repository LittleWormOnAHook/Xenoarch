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
        /// <summary>Random valuable cache (small gold-bordered dig), not a major dig site: no fence, circle or dig map.</summary>
        public bool IsCache { get; set; }
        /// <summary>Phase 1D: a dig bed inside a buried chamber (no terrain, no Digger: trench mesh; no border).</summary>
        public bool Underground { get; private set; }
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
            foreach (var s in Active) if (s != null && s.Grid != null && s.Contains(world) && s.WithinDepth(world)) return s;
            return null;
        }

        /// <summary>Vertical window of a site (sites stacked above each other, e.g. a chamber under a dig, stay apart).</summary>
        public bool WithinDepth(Vector3 world)
        {
            var l = transform.InverseTransformPoint(world);
            return l.y >= -(Grid.ny * Grid.cell) - 2f && l.y <= 40f;
        }

        /// <summary>Ground height for readings: an underground dig bed's surface where one contains the point, else the terrain.</summary>
        public static float GroundAt(Vector3 p, float fallback)
        {
            foreach (var s in Active)
                if (s != null && s.Underground && s.Grid != null && s.Contains(p) && s.WithinDepth(p)) return s.transform.position.y;
            return TerrainHeight(p) ?? fallback;
        }

        public static ExcavationSite Create(ArchaeologicalSiteDefinition def, float surfaceY, UnityEngine.SceneManagement.Scene scene, bool underground = false)
        {
            var go = new GameObject($"DigSite {def.Id}");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            var site = go.AddComponent<ExcavationSite>();
            site.Underground = underground;
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
            if (!Underground)
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
            if (!Underground && TerrainHeight(def.position).HasValue)
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
            if (restored) EnsurePitLight();

            foreach (var a in Grid.artifacts)
                if (!a.recovered) _pieces.Add(BuriedArtifact.Spawn(this, a, arts.Definition(def.artifacts[a.index].artifactId)));

            var xs = XenoarchRuntime.Session?.Exploration;
            if (!Underground && xs != null && xs.Site(def.Id).revealLevel >= (int)SiteRevealLevel.Pinpointed) ShowBorder(true);
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
            // Aimed at dig-surface material standing above the grid floor (lumps and peaks left between Digger carves):
            // the grid already counts that ground as dug, so its column stroke skipped it ("too far above the floor")
            // and nothing happened with any tool. Trim the leftover where it is aimed instead.
            if (_surface != null && LeftoverAt(l, out int lx, out int lz))
            {
                var r0 = LeftoverStroke(l, lx, lz, tool);
                if (r0.accepted || r0.refused) { EnsurePitLight(); return r0; }
            }
            // The Digger carve is at least CarveRadius wide, so the grid stroke covers the same footprint (otherwise the hole
            // looks dug where the grid still says "surface", and the next aim there reads depth 0).
            if (_surface != null) tool.radius = Mathf.Max(tool.radius, CarveRadius * 0.8f);
            var r = Grid.StrokeAt(l.x, l.z, l.y, tool);
            if (!r.accepted) return r;
            if (_mesh != null) Rebuild();
            foreach (var p in _pieces) if (p != null) p.Refresh();
            _dirty = true; _saveAt = Time.unscaledTime + 1f;
            if (_surface != null && r.cut != null && r.cut.Count > 0) CarveStroke(r.cut, tool.radius);
            EnsurePitLight();
            return r;
        }

        /// <summary>Leftover material is any aim this far (m) above its column's grid floor.</summary>
        float LeftoverThreshold => Mathf.Max(Grid.cell * 0.6f, 0.12f);

        bool LeftoverAt(Vector3 local, out int x, out int z)
        {
            x = Mathf.FloorToInt(local.x / Grid.cell); z = Mathf.FloorToInt(local.z / Grid.cell);
            if (!Grid.InBounds(x, z)) return false;
            if (Grid.TopDepth(x, z) < Grid.cell * 0.5f) return false;          // untouched column: normal digging
            if (local.y > Grid.Surface(x, z) + 0.25f) return false;             // above the original ground (a rock, a find)
            return local.y - Grid.FloorY(x, z) > LeftoverThreshold;
        }

        /// <summary>Layer at a depth below the column's original surface.</summary>
        SoilLayer LayerAtDepth(float depth) => Grid.LayerAt(Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, depth) / Grid.cell), 0, Grid.ny - 1));

        StrokeResult LeftoverStroke(Vector3 l, int x, int z, DigToolSpec tool)
        {
            var r = new StrokeResult { leftover = true };
            var layer = LayerAtDepth(Grid.Surface(x, z) - l.y);
            r.layer = layer; r.material = layer.kind;
            if (!DigTools.CanCut(layer, tool)) { r.refused = true; r.needsBetterTool = true; return r; }
            // Harder bands: smaller bites (never below one Digger voxel); the stroke rate already paces the tool.
            float res = Mathf.Max(1f, DigTools.Resistance(layer.hardness, tool.maxHardness));
            if (layer.hardness > tool.maxHardness) r.slowed = true;
            float rad = Mathf.Max(CarveRadius, Mathf.Max(tool.radius, CarveRadius) / Mathf.Sqrt(res));
            float floor = Grid.FloorY(x, z);
            // Centre a little inside the surface at the aim, but never so low that the sphere cuts below the grid floor.
            var c = new Vector3(l.x, Mathf.Max(l.y - rad * 0.35f, floor + rad * 0.95f), l.z);
            QueueCarve(transform.TransformPoint(c), rad, layer.diggerTexture);
            r.accepted = true;
            return r;
        }

        /// <summary>What the tool is on: the band at the aimed depth (not the column's top cell), and whether it is a
        /// leftover lump above the dug floor.</summary>
        public SoilLayer LayerAtPoint(Vector3 world, out float depth, out bool leftover)
        {
            var l = transform.InverseTransformPoint(world);
            int x = Mathf.Clamp(Mathf.FloorToInt(l.x / Grid.cell), 0, Grid.nx - 1), z = Mathf.Clamp(Mathf.FloorToInt(l.z / Grid.cell), 0, Grid.nz - 1);
            leftover = _surface != null && LeftoverAt(l, out _, out _);
            // Below the floor (Digger hole a bit deeper than the grid) reads as the column's current top band.
            depth = Mathf.Max(0f, Grid.Surface(x, z) - l.y);
            if (!leftover)
            {
                int top = Grid.TopLayer(x, z);
                if (top >= Grid.ny) { depth = Grid.ny * Grid.cell; return SoilLayer.Bedrock; }
                depth = Mathf.Max(depth, Grid.TopDepth(x, z));
                return Grid.LayerAt(top);
            }
            return LayerAtDepth(depth);
        }

        Light _pitLight;
        /// <summary>Shadowless fill light in a dug pit (t117u): the sun's shadow plus flat ambient left pit walls nearly black.</summary>
        void EnsurePitLight()
        {
            var cfg = XenoarchRuntime.Session?.Config;
            float k = cfg != null ? cfg.pitFillIntensity : 0.8f;
            if (k <= 0f || Grid == null) return;
            float maxDepth = 0f;
            for (int x = 0; x < Grid.nx; x++) for (int z = 0; z < Grid.nz; z++) maxDepth = Mathf.Max(maxDepth, Grid.TopDepth(x, z));
            if (maxDepth < 0.3f) return;
            if (_pitLight == null)
            {
                var go = new GameObject("Pit Fill Light");
                go.transform.SetParent(transform, false);
                _pitLight = go.AddComponent<Light>();
                _pitLight.type = LightType.Point; _pitLight.shadows = LightShadows.None;
                var a = RenderSettings.ambientLight; float m = Mathf.Max(0.05f, Mathf.Max(a.r, Mathf.Max(a.g, a.b)));
                _pitLight.color = Color.Lerp(Color.white, new Color(a.r / m, a.g / m, a.b / m), 0.5f);
                _pitLight.renderMode = LightRenderMode.ForcePixel;
            }
            float surf = Grid.surface != null ? Grid.Surface(Grid.nx / 2, Grid.nz / 2) : 0f;
            _pitLight.transform.localPosition = new Vector3(Grid.SizeX * 0.5f, surf - Mathf.Min(maxDepth * 0.4f, 1.2f), Grid.SizeZ * 0.5f);
            _pitLight.range = Mathf.Max(Grid.SizeX, Grid.SizeZ) * 0.6f + maxDepth;
            _pitLight.intensity = k;
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
            _replaying = false; EnsurePitLight();
            Debug.Log($"[Xenoarch] Dig site {Definition.Id}: replayed {ops} carve op(s) into the terrain");
        }

        /// <summary>Uncovered share of a find: the grid's count or, on a Digger surface, what the player can actually see,
        /// whichever is higher (t177u). Digger's smallest carve (~0.9 m across) strips the soil round a small find long
        /// before the grid cells over it are dug, which left finds sitting bare with no pickup prompt and no map mark.</summary>
        public float Exposure(GridArtifact a)
        {
            float g = Grid.Exposure(a);
            if (_surface == null || a == null || a.recovered || g >= BuriedArtifact.SafeExposure) return g;
            return Mathf.Max(g, VisualExposure(a));
        }

        readonly Dictionary<GridArtifact, (float at, float value)> _visual = new Dictionary<GridArtifact, (float, float)>();
        static readonly RaycastHit[] _exposeHits = new RaycastHit[8];

        /// <summary>Visible share of a find: five rays down over it (centre and four points round it) measure how far
        /// below the find's top the ground there now is. Cached for 0.2 s.</summary>
        float VisualExposure(GridArtifact a)
        {
            if (_visual.TryGetValue(a, out var c) && Time.time - c.at < 0.2f) return c.value;
            float v = 0f;
            BuriedArtifact piece = null;
            foreach (var p in _pieces) if (p != null && p.Grid == a) { piece = p; break; }
            var r = piece != null ? piece.GetComponentInChildren<Renderer>() : null;
            if (r != null)
            {
                var b = r.bounds;
                float top = b.max.y, h = Mathf.Max(0.05f, b.size.y), reach = 3f + h * 2f + 1f;
                var rig = DMI.Xenoarch.Player.PlayerRigLocator.Find();
                var me = rig != null ? rig.Root : null;
                float ex = b.extents.x * 0.7f, ez = b.extents.z * 0.7f, sum = 0f;
                for (int i = 0; i < 5; i++)
                {
                    var o = b.center + (i == 1 ? new Vector3(ex, 0f, 0f) : i == 2 ? new Vector3(-ex, 0f, 0f) : i == 3 ? new Vector3(0f, 0f, ez) : i == 4 ? new Vector3(0f, 0f, -ez) : Vector3.zero);
                    o.y = top + 3f;
                    int n = Physics.RaycastNonAlloc(o, Vector3.down, _exposeHits, reach, ~0, QueryTriggerInteraction.Ignore);
                    float ground = float.NegativeInfinity;
                    for (int k = 0; k < n; k++)
                    {
                        var hit = _exposeHits[k];
                        if (hit.collider == null || (me != null && hit.collider.transform.IsChildOf(me))) continue;
                        if (hit.collider.GetComponentInParent<BuriedArtifact>() != null) continue;
                        if (hit.point.y > ground) ground = hit.point.y;
                    }
                    // Highest surface over this point; nothing within reach means an open pit under it.
                    sum += float.IsNegativeInfinity(ground) ? 1f : Mathf.Clamp01((top - ground) / h);
                }
                v = sum / 5f;
            }
            _visual[a] = (Time.time, v);
            return v;
        }

        // ------------------------------------------------------------------ border
        /// <summary>Square outline around the site footprint, following the ground.</summary>
        public void ShowBorder(bool on)
        {
            if (!on) { if (_border != null) _border.enabled = false; if (_fence != null) _fence.SetActive(false); return; }
            if (Underground) return;
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
            if (IsCache) return;
            if (_fence == null) BuildFence();
            _fence.SetActive(true);
            DigSiteFlags.Instance?.SiteFenced(this);
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
            DigSiteFlags.Prewarm();
            DMI.Xenoarch.Exploration.SiteMarkerDirector.MarkerMaterial();
        }

        internal void OnRecovered(BuriedArtifact piece, string instanceId)
        {
            piece.Grid.recovered = true;
            _pieces.Remove(piece);
            XenoarchRuntime.Session?.Excavation.Store(Definition.Id, Grid, instanceId, piece.Grid.index);
            _dirty = false;
            if (UpdateCleared()) DMI.Xenoarch.UI.Toast.Show($"{Definition.displayName}: every find recovered. Site cleared; the scanner will not ping it any more.", 5f);
        }

        /// <summary>ZEN-26: marks a dig site (not a cache) cleared once it had finds and all are recovered. True when it
        /// was cleared just now. Also run by the scanner, so sites cleared in older saves catch up.</summary>
        public bool UpdateCleared()
        {
            if (IsCache || Grid == null || Definition == null || Grid.artifacts.Count == 0) return false;
            foreach (var a in Grid.artifacts) if (!a.recovered) return false;
            var x = XenoarchRuntime.Session?.Exploration;
            if (x == null || x.IsCleared(Definition.Id)) return false;
            x.MarkCleared(Definition.Id);
            return true;
        }

        /// <summary>ZEN-26: site cleared (saved flag, or a loaded site whose finds are all recovered).</summary>
        public static bool IsCleared(ArchaeologicalSiteDefinition def)
        {
            var x = XenoarchRuntime.Session?.Exploration;
            if (def == null || x == null) return false;
            if (x.IsCleared(def.Id)) return true;
            var s = Find(def.Id);
            return s != null && s.UpdateCleared();
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
                DMI.Xenoarch.Scenes.ShaderPrewarm.Warm(m[i]);
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
