using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    public enum SoilMaterial : byte { Dust, Loose, Compacted, Bedrock, Ice, Rock }

    /// <summary>One soil band of a dig-site style, top to bottom. The last band continues down to the bedrock floor.</summary>
    [Serializable]
    public struct SoilLayer
    {
        public string label;
        public SoilMaterial kind;
        [Tooltip("Band thickness (m). Ignored for the last band.")] public float thickness;
        [Tooltip("How hard the band is (dust 0.5, loose 1, compacted 2, rock/ice 3). Every tool can cut it; tools rated below it are slower.")] public float hardness;
        public Color color;
        [Tooltip("Digger texture (terrain layer) index painted by strokes in this band.")] public int diggerTexture;
        [Tooltip("Tool rating (DigToolSpec.maxHardness) needed to cut this band at all. 0 = every tool can dig it (harder " +
                 "bands are just slower). Use on later worlds to require an upgraded excavator, a burster, etc.")] public float minToolRating;

        public static SoilLayer Make(string label, SoilMaterial kind, float thickness, float hardness, Color color, int tex = 0) =>
            new SoilLayer { label = label, kind = kind, thickness = thickness, hardness = hardness, color = color, diggerTexture = tex };

        public static readonly SoilLayer Bedrock = Make("bedrock", SoilMaterial.Bedrock, 0f, 99f, new Color(0.22f, 0.21f, 0.21f));

        public static SoilLayer[] DefaultProfile(Color surface) => new[]
        {
            Make("surface dust", SoilMaterial.Dust, 0.25f, 0.5f, surface),
            Make("loose regolith", SoilMaterial.Loose, 0.75f, 1f, surface * 0.82f),
            Make("compacted layer", SoilMaterial.Compacted, 0f, 2f, surface * 0.64f),
        };
    }

    /// <summary>Dig tool stats. Authored as <see cref="DigToolDefinition"/> assets (Resources/Excavation/DigTools); the
    /// values in <see cref="DigTools"/> are the built-in fallbacks. No tool has a depth cap: every tool digs down to the
    /// site floor (bedrock); tiers differ in chunk size, speed, hardness rating and artifact risk.</summary>
    [Serializable]
    public struct DigToolSpec
    {
        public string itemId, name;
        [Tooltip("Short HUD name (e.g. excavator).")] public string shortName;
        [Tooltip("Horizontal radius (m) of one stroke.")] public float radius;
        [Tooltip("Chunk size: cell fraction removed per stroke from soft soil (divided by hardness). Crude tools take big chunks, fine tools small bites.")] public float removal;
        [Tooltip("Hardness this tool cuts at full speed. Harder bands are still dug, just more slowly (squared falloff). Only bedrock stops every tool.")] public float maxHardness;
        [Tooltip("Artifact-damage risk: condition loss factor when a crude stroke cuts a visible find (x fragility x removed fraction).")] public float damage;
        [Tooltip("Crude tools (excavator, burster) can jar or damage finds; fine tools never do.")] public bool crude;
        [Tooltip("Strokes per second while the button is held.")] public float rate;
        [Tooltip("Optional cap on cells cut per column per stroke (0 = no cap: the chunk size decides).")] public int cells;
        [Tooltip("Smallest bite (cell fraction) a stroke always takes from the aimed column, however hard the band (0 = 0.03), " +
                 "so slow digging still shows progress.")] public float minBite;
        [Tooltip("Head shape of the stroke on a mirrored surface (sonic excavator).")] public ExcavationBrush brush;
    }

    public static class DigTools
    {
        // Built-in fallbacks (also used by the editor checks). Shovel: 3x the old effective dig speed (ZEN-8 / t104u).
        public static readonly DigToolSpec Shovel = new DigToolSpec { itemId = "item.tool.excavation_shovel", name = "Sonic Excavator", shortName = "excavator", radius = 0.45f, removal = 2f, maxHardness = 2f, damage = 0.6f, rate = 5.6f, crude = true };
        // ZEN-23 (t166u): precision = small shallow controlled bites, narrow; brush = a thin dust/loose-soil skim, wide and soft.
        public static readonly DigToolSpec Precision = new DigToolSpec { itemId = "item.tool.precision_tool", name = "Fine focus", shortName = "fine", radius = 0.1f, removal = 0.15f, maxHardness = 2f, damage = 0f, rate = 5f, cells = 1, minBite = 0.02f };
        public static readonly DigToolSpec Brush = new DigToolSpec { itemId = "item.tool.brush", name = "Ultrasonic Brush", shortName = "brush", radius = 0.6f, removal = 0.035f, maxHardness = 1f, damage = 0f, rate = 7f, cells = 1, minBite = 0.005f };
        // t209u: removal 1.8 -> 3.5 so the drill clearly out-digs the shovel in ice/rock (about 1.5-1.9x per second); in soft soil it is ~1.25x deeper per second but narrower.
        public static readonly DigToolSpec Drill = new DigToolSpec { itemId = "item.tool.powered_drill", name = "Sonic Burster", shortName = "burster", radius = 0.25f, removal = 3.5f, maxHardness = 4f, damage = 0.8f, rate = 4f, crude = true };
        public static readonly DigToolSpec[] Defaults = { Shovel, Precision, Brush, Drill };

        public const string ResourceFolder = "Excavation/DigTools";
        static DigToolSpec[] _all;
        /// <summary>Tool tiers in slot order (keys 2, 3, 4...): the DigToolDefinition assets, or the defaults if none.</summary>
        public static DigToolSpec[] All
        {
            get
            {
                if (_all != null) return _all;
                var defs = Resources.LoadAll<DigToolDefinition>(ResourceFolder);
                if (defs == null || defs.Length == 0) return _all = Defaults;
                Array.Sort(defs, (a, b) => a.slotOrder != b.slotOrder ? a.slotOrder.CompareTo(b.slotOrder) : string.CompareOrdinal(a.name, b.name));
                var list = new List<DigToolSpec>();
                foreach (var d in defs) if (d != null && d.enabledInGame && list.Count < 8) list.Add(d.spec);
                return _all = list.Count > 0 ? list.ToArray() : Defaults;
            }
        }
        /// <summary>Drop the cached list (editor tooling after assets change).</summary>
        public static void Reload() => _all = null;

        /// <summary>Divisor for removal in a band of this hardness: the band's hardness, and linearly slower again past the
        /// tool rating (t108u: was squared, which made rock bands with the shovel/precision tool feel like a wall).</summary>
        public static float Resistance(float hardness, float rated)
        {
            float d = Mathf.Max(0.25f, hardness);
            if (rated > 0f && hardness > rated) d *= hardness / rated;
            return d;
        }

        /// <summary>Can this tool cut the band at all (the band's optional minimum tool rating)?</summary>
        public static bool CanCut(in SoilLayer layer, in DigToolSpec tool) => layer.minToolRating <= 0f || tool.maxHardness >= layer.minToolRating;
    }

    /// <summary>An artifact embedded in the grid: top cell (x, y, z), footprint = cells per side, height = layers.
    /// Big finds show parts first (exposure = uncovered share of the top layer). Condition only drops once part of it is
    /// visible and a crude tool cuts one of its cells.</summary>
    public sealed class GridArtifact
    {
        public int index, x, y, z, footprint = 1, height = 1;
        public float fragility = 0.5f, condition = 1f;
        public bool recovered;
        /// <summary>Share of dust brushed off an exposed find (ultrasonic brush), 0..1. Saved with the site.</summary>
        public float clean;

    }

    public struct StrokeResult
    {
        public bool accepted, refused;
        public bool slowed;             // a cut band is harder than the tool rating (dug slowly)
        public float removed;          // total cell fraction removed
        public SoilMaterial material;   // material at the stroke centre
        public SoilLayer layer;
        public float damageDealt;
        public GridArtifact damaged;
        /// <summary>A crude stroke cut right above a still-hidden find (jarring it) or within two cells of it (warning).</summary>
        public GridArtifact hiddenBelow;
        public bool jarred;
        /// <summary>The band needs a better tool (SoilLayer.minToolRating); set when that is why nothing was cut.</summary>
        public bool needsBetterTool;
        /// <summary>Finds that became visible with this stroke (first glimpse).</summary>
        public List<GridArtifact> emerged;
        public List<Vector3Int> cut;    // cells cut by this stroke (drives Digger)
        /// <summary>The stroke trimmed leftover dig-surface material above the grid floor (a lump/peak in the pit).</summary>
        public bool leftover;
    }

    /// <summary>
    /// Gameplay truth of one dig site: a bounded layered cell grid (x/z columns, y layers from the surface down), each
    /// cell holding its remaining fill 0..255. Tools remove the top cell of the columns under the stroke; hardness
    /// gates which tool can dig what; artifacts lose condition when their cell (or, with heavy tools, the cell above)
    /// is cut. Pure C# so it is cheap to test and save; ExcavationSite renders it and mirrors strokes to Digger.
    /// </summary>
    public sealed class ExcavationGrid
    {
        public readonly int nx, nz, ny;
        public readonly float cell;
        public readonly byte[] fill;
        public readonly List<GridArtifact> artifacts = new List<GridArtifact>();
        public int strokes;
        /// <summary>Optional per-column surface height (m, local, x + nx*z) for sites on uneven terrain. Null = flat at 0.</summary>
        public float[] surface;
        /// <summary>How far (in cells) a stroke may be from a column's floor and still cut it (bigger with Digger meshes).</summary>
        public float tolerance = 1.5f;
        [Tooltip("How far (m) below a column's grid floor an aim may land and still dig it (Digger's visual hole can run deeper).")]
        public float belowSlack = 1f;
        /// <summary>Largest share of a find's cell a crude tool (excavator, burster) removes in one stroke.</summary>
        public const float CrudeFindBite = 0.5f;
        public float Surface(int x, int z) => surface != null ? surface[x + nx * z] : 0f;
        /// <summary>Local height of the column's current floor (surface minus dug depth).</summary>
        public float FloorY(int x, int z) => Surface(x, z) - TopDepth(x, z);

        /// <summary>Soil bands from the site style (top to bottom); the last grid layer is always bedrock.</summary>
        public SoilLayer[] layers = SoilLayer.DefaultProfile(new Color(0.47f, 0.47f, 0.48f));

        public ExcavationGrid(int nx, int nz, int ny, float cell, byte[] data = null)
        {
            this.nx = Mathf.Max(1, nx); this.nz = Mathf.Max(1, nz); this.ny = Mathf.Max(2, ny); this.cell = Mathf.Max(0.05f, cell);
            fill = new byte[this.nx * this.nz * this.ny];
            if (data != null && data.Length == fill.Length) Buffer.BlockCopy(data, 0, fill, 0, fill.Length);
            else for (int i = 0; i < fill.Length; i++) fill[i] = 255;
        }

        /// <summary>Grid for a site of <paramref name="radius"/> (square footprint 2r) dug to <paramref name="depth"/> + margin; last layer is bedrock.</summary>
        public static ExcavationGrid ForSite(float radius, float depth, float cell, SoilLayer[] profile = null)
        {
            cell = cell > 0.05f ? cell : 0.25f;
            int n = Mathf.Max(2, Mathf.RoundToInt(2f * radius / cell));
            int ly = Mathf.CeilToInt((depth + 0.75f) / cell) + 1;
            var g = new ExcavationGrid(n, n, ly, cell);
            if (profile != null && profile.Length > 0) g.layers = profile;
            return g;
        }

        public float SizeX => nx * cell;
        public float SizeZ => nz * cell;
        public int Index(int x, int y, int z) => x + nx * (z + nz * y);
        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < nx && z < nz;
        public byte Fill(int x, int y, int z) => fill[Index(x, y, z)];

        public SoilLayer LayerAt(int y)
        {
            if (y >= ny - 1 || layers == null || layers.Length == 0) return SoilLayer.Bedrock;
            float d = (y + 0.5f) * cell, acc = 0f;
            for (int i = 0; i < layers.Length - 1; i++) { acc += Mathf.Max(0f, layers[i].thickness); if (d < acc) return layers[i]; }
            return layers[layers.Length - 1];
        }

        public SoilMaterial MaterialAt(int y) => LayerAt(y).kind;

        public int TopLayer(int x, int z)
        {
            for (int y = 0; y < ny; y++) if (fill[Index(x, y, z)] > 0) return y;
            return ny;
        }

        /// <summary>Depth (m, positive down) of the column's current surface.</summary>
        public float TopDepth(int x, int z)
        {
            int y = TopLayer(x, z);
            if (y >= ny) return ny * cell;
            return (y + 1f - fill[Index(x, y, z)] / 255f) * cell;
        }

        public float Exposure(GridArtifact a)
        {
            float sum = 0f; int n = 0;
            for (int dx = 0; dx < a.footprint; dx++)
                for (int dz = 0; dz < a.footprint; dz++)
                {
                    int x = a.x + dx, z = a.z + dz;
                    if (!InBounds(x, z)) continue;
                    n++;
                    bool covered = false;
                    for (int y = 0; y < a.y; y++) if (fill[Index(x, y, z)] > 0) { covered = true; break; }
                    if (!covered) sum += 1f - fill[Index(x, a.y, z)] / 255f;
                }
            return n > 0 ? sum / n : 0f;
        }

        GridArtifact ArtifactAt(int x, int y, int z)
        {
            foreach (var a in artifacts)
                if (!a.recovered && y >= a.y && y < a.y + Mathf.Max(1, a.height) && x >= a.x && x < a.x + a.footprint && z >= a.z && z < a.z + a.footprint) return a;
            return null;
        }

        /// <summary>
        /// One tool stroke at grid-local (x, z) metres, aimed at surface depth <paramref name="hitDepth"/>. Removes from
        /// the top cell of every column within the tool radius whose surface is near the aimed depth.
        /// </summary>
        public StrokeResult Stroke(float lx, float lz, float hitDepth, DigToolSpec tool) => StrokeAt(lx, lz, -hitDepth, tool);

        /// <summary>Same as <see cref="Stroke"/> but aimed at a local height (works with per-column surfaces).</summary>
        [NonSerialized] readonly List<GridArtifact> _visibleScratch = new List<GridArtifact>();

        public StrokeResult StrokeAt(float lx, float lz, float hitY, DigToolSpec tool)
        {
            var r = new StrokeResult();
            int cx = Mathf.FloorToInt(lx / cell), cz = Mathf.FloorToInt(lz / cell);
            int span = Mathf.CeilToInt(tool.radius / cell) + 1;
            bool centreSet = false;
            var visibleBefore = _visibleScratch; visibleBefore.Clear();   // reused: no per-stroke allocation
            foreach (var a in artifacts) if (!a.recovered && Exposure(a) > 0.001f) visibleBefore.Add(a);
            for (int x = cx - span; x <= cx + span; x++)
                for (int z = cz - span; z <= cz + span; z++)
                {
                    if (!InBounds(x, z)) continue;
                    float px = (x + 0.5f) * cell, pz = (z + 0.5f) * cell;
                    float dist = Mathf.Sqrt((px - lx) * (px - lx) + (pz - lz) * (pz - lz));
                    bool centre = x == cx && z == cz;
                    if (!centre && dist > tool.radius) continue;
                    int y = TopLayer(x, z);
                    if (y >= ny) continue;
                    // Only reject aims clearly ABOVE this column's floor (rock, a wall, a find sticking up). Aims below it are
                    // fine: the visual (Digger) hole can be a little deeper/wider than the grid, and rejecting those was what
                    // stopped a held shovel after one stroke.
                    float off = hitY - FloorY(x, z);
                    if (off > cell * tolerance || off < -Mathf.Max(belowSlack, cell * tolerance)) continue;
                    float amount = tool.removal * (1f - 0.5f * Mathf.Clamp01(dist / Mathf.Max(0.01f, tool.radius)));
                    int maxCells = tool.cells > 0 ? tool.cells : ny;   // no per-tool depth cap
                    for (int c = 0; c < maxCells && y < ny && amount > 0.001f; c++, y++)
                    {
                        var layer = LayerAt(y);
                        if (centre || !centreSet) { r.material = layer.kind; r.layer = layer; centreSet |= centre; }
                        if (layer.kind == SoilMaterial.Bedrock) { if (c == 0) r.refused = true; break; }   // the site floor
                        if (!DigTools.CanCut(layer, tool)) { if (c == 0) { r.refused = true; r.needsBetterTool = true; } break; }
                        // Crude tools never go through a hidden find in one stroke: after cutting the soil above it they stop on
                        // top of it (the jar warning comes first), and they bite at most half a find cell per stroke. Without this
                        // the 2.0 shovel (ZEN-8) cleared the cell above AND the whole find cell in one hidden, damage-free stroke.
                        var crudeFind = tool.crude ? ArtifactAt(x, y, z) : null;
                        if (crudeFind != null && c > 0 && !visibleBefore.Contains(crudeFind)) break;
                        float res = DigTools.Resistance(layer.hardness, tool.maxHardness);
                        if (layer.hardness > tool.maxHardness) r.slowed = true;
                        int i = Index(x, y, z);
                        float before = fill[i] / 255f;
                        float take = Mathf.Min(before, amount / res);
                        // Minimum effective bite on the aimed column: hard bands are slow, never a wall.
                        if (centre && c == 0) take = Mathf.Min(before, Mathf.Max(take, tool.minBite > 0f ? tool.minBite : 0.03f));
                        if (crudeFind != null) take = Mathf.Min(take, CrudeFindBite);
                        if (take <= 0f) continue;
                        amount -= take * res;
                        fill[i] = (byte)Mathf.Clamp(Mathf.RoundToInt((before - take) * 255f), 0, 255);
                        if (fill[i] <= 2) fill[i] = 0;
                        r.removed += take; r.accepted = true;
                        r.cut ??= new List<Vector3Int>();
                        r.cut.Add(new Vector3Int(x, y, z));

                        // Damage: only a crude tool cutting a cell of a find that is already (partly) visible.
                        var hit = ArtifactAt(x, y, z);
                        if (hit != null && tool.crude && tool.damage > 0f && visibleBefore.Contains(hit))
                        {
                            float dmg = tool.damage * hit.fragility * take;
                            hit.condition = Mathf.Max(0.05f, hit.condition - dmg);
                            r.damageDealt += dmg; r.damaged = hit;
                        }
                        // Careless digging: a crude tool cutting just above a find that is still hidden jars it (15% of
                        // the visible-cut damage); one cell further up only warns. Fine tools are always safe.
                        if (hit == null && tool.crude && tool.damage > 0f)
                        {
                            var below1 = y + 1 < ny ? ArtifactAt(x, y + 1, z) : null;
                            var below2 = below1 == null && y + 2 < ny ? ArtifactAt(x, y + 2, z) : null;
                            if (below1 != null && !visibleBefore.Contains(below1))
                            {
                                float dmg = 0.15f * tool.damage * below1.fragility * take;
                                below1.condition = Mathf.Max(0.05f, below1.condition - dmg);
                                r.damageDealt += dmg; r.damaged = below1; r.hiddenBelow = below1; r.jarred = true;
                            }
                            else if (below2 != null && !visibleBefore.Contains(below2) && r.hiddenBelow == null) r.hiddenBelow = below2;
                        }
                        if (hit != null) break;      // never cut through a find in one stroke
                        if (fill[i] > 0) break;      // cell not finished: stop here
                    }
                }
            if (r.accepted)
            {
                strokes++;
                foreach (var a in artifacts)
                    if (!a.recovered && !visibleBefore.Contains(a) && Exposure(a) > 0.001f) (r.emerged ??= new List<GridArtifact>()).Add(a);
            }
            return r;
        }

        // ------------------------------------------------------------------ persistence (RLE)
        public string Encode()
        {
            var bytes = new List<byte>(64);
            int i = 0;
            while (i < fill.Length)
            {
                byte v = fill[i]; int run = 1;
                while (i + run < fill.Length && fill[i + run] == v && run < 255) run++;
                bytes.Add((byte)run); bytes.Add(v);
                i += run;
            }
            return Convert.ToBase64String(bytes.ToArray());
        }

        public bool Decode(string data)
        {
            if (string.IsNullOrEmpty(data)) return false;
            byte[] b;
            try { b = Convert.FromBase64String(data); } catch (FormatException) { return false; }
            int o = 0;
            for (int i = 0; i + 1 < b.Length; i += 2)
                for (int k = 0; k < b[i] && o < fill.Length; k++) fill[o++] = b[i + 1];
            return o == fill.Length;
        }
    }
}
