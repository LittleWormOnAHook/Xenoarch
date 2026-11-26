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
        [Tooltip("Shovel/precision dig up to 2, brush up to 1, drill up to 4.")] public float hardness;
        public Color color;
        [Tooltip("Digger texture (terrain layer) index painted by strokes in this band.")] public int diggerTexture;

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

    /// <summary>Dig tool stats (Phase 1: fixed per tool item; see <see cref="DigTools"/>).</summary>
    [Serializable]
    public struct DigToolSpec
    {
        public string itemId, name;
        [Tooltip("Horizontal radius (m) of one stroke.")] public float radius;
        [Tooltip("Cell fraction removed per stroke from soft soil (divided by hardness).")] public float removal;
        public float maxHardness;
        [Tooltip("Condition loss factor when the stroke cuts a visible artifact's cell (x fragility x removed fraction). Only crude tools damage.")] public float damage;
        [Tooltip("Crude tools (shovel, drill) damage finds once they are visible; fine tools never do.")] public bool crude;
        [Tooltip("Strokes per second while the button is held.")] public float rate;
        [Tooltip("Most cells one stroke can cut down through per column (0 = 1). Leftover removal carries into the next cell.")] public int cells;
    }

    public static class DigTools
    {
        public static readonly DigToolSpec Shovel = new DigToolSpec { itemId = "item.tool.excavation_shovel", name = "Excavation shovel", radius = 0.35f, removal = 0.5f, maxHardness = 2f, damage = 0.6f, rate = 2.5f, crude = true };
        public static readonly DigToolSpec Precision = new DigToolSpec { itemId = "item.tool.precision_tool", name = "Precision tool", radius = 0.14f, removal = 0.25f, maxHardness = 2f, damage = 0f, rate = 4f };
        public static readonly DigToolSpec Brush = new DigToolSpec { itemId = "item.tool.brush", name = "Brush", radius = 0.18f, removal = 0.15f, maxHardness = 1f, damage = 0f, rate = 5f };
        public static readonly DigToolSpec Drill = new DigToolSpec { itemId = "item.tool.powered_drill", name = "Powered drill", radius = 0.2f, removal = 0.6f, maxHardness = 4f, damage = 0.8f, rate = 4f, crude = true };
        public static readonly DigToolSpec[] All = { Shovel, Precision, Brush, Drill };
    }

    /// <summary>An artifact embedded in the grid: top cell (x, y, z), footprint = cells per side, height = layers.
    /// Big finds show parts first (exposure = uncovered share of the top layer). Condition only drops once part of it is
    /// visible and a crude tool cuts one of its cells.</summary>
    public sealed class GridArtifact
    {
        public int index, x, y, z, footprint = 1, height = 1;
        public float fragility = 0.5f, condition = 1f;
        public bool recovered;
    }

    public struct StrokeResult
    {
        public bool accepted, refused;
        public float removed;          // total cell fraction removed
        public SoilMaterial material;   // material at the stroke centre
        public SoilLayer layer;
        public float damageDealt;
        public GridArtifact damaged;
        public List<Vector3Int> cut;    // cells cut by this stroke (drives Digger)
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
        public StrokeResult StrokeAt(float lx, float lz, float hitY, DigToolSpec tool)
        {
            var r = new StrokeResult();
            int cx = Mathf.FloorToInt(lx / cell), cz = Mathf.FloorToInt(lz / cell);
            int span = Mathf.CeilToInt(tool.radius / cell) + 1;
            bool centreSet = false;
            var visibleBefore = new List<GridArtifact>();
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
                    int maxCells = Mathf.Max(1, tool.cells);
                    for (int c = 0; c < maxCells && y < ny && amount > 0.001f; c++, y++)
                    {
                        var layer = LayerAt(y);
                        if (centre || !centreSet) { r.material = layer.kind; r.layer = layer; centreSet |= centre; }
                        float hard = layer.hardness;
                        if (hard > tool.maxHardness) { if (c == 0) r.refused = true; break; }
                        int i = Index(x, y, z);
                        float before = fill[i] / 255f;
                        float take = Mathf.Min(before, amount / Mathf.Max(0.25f, hard));
                        if (take <= 0f) continue;
                        amount -= take * Mathf.Max(0.25f, hard);
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
                        if (hit != null) break;      // never cut through a find in one stroke
                        if (fill[i] > 0) break;      // cell not finished: stop here
                    }
                }
            if (r.accepted) strokes++;
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
