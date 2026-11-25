using System;
using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    public enum SoilMaterial : byte { Dust, Loose, Compacted, Bedrock }

    /// <summary>Dig tool stats (Phase 1: fixed per tool item; see <see cref="DigTools"/>).</summary>
    [Serializable]
    public struct DigToolSpec
    {
        public string itemId, name;
        [Tooltip("Horizontal radius (m) of one stroke.")] public float radius;
        [Tooltip("Cell fraction removed per stroke from soft soil (divided by hardness).")] public float removal;
        public float maxHardness;
        [Tooltip("Condition loss factor when the stroke cuts an artifact's cell (x fragility x removed fraction).")] public float damage;
        [Tooltip("Strokes per second while the button is held.")] public float rate;
    }

    public static class DigTools
    {
        public static readonly DigToolSpec Shovel = new DigToolSpec { itemId = "item.tool.excavation_shovel", name = "Excavation shovel", radius = 0.35f, removal = 0.5f, maxHardness = 2f, damage = 0.6f, rate = 2.5f };
        public static readonly DigToolSpec Precision = new DigToolSpec { itemId = "item.tool.precision_tool", name = "Precision tool", radius = 0.14f, removal = 0.25f, maxHardness = 2f, damage = 0.05f, rate = 4f };
        public static readonly DigToolSpec Brush = new DigToolSpec { itemId = "item.tool.brush", name = "Brush", radius = 0.18f, removal = 0.15f, maxHardness = 1f, damage = 0f, rate = 5f };
        public static readonly DigToolSpec Drill = new DigToolSpec { itemId = "item.tool.powered_drill", name = "Powered drill", radius = 0.2f, removal = 0.6f, maxHardness = 4f, damage = 0.8f, rate = 4f };
        public static readonly DigToolSpec[] All = { Shovel, Precision, Brush, Drill };
    }

    /// <summary>An artifact embedded in the grid (cell coords; footprint = cells per side). Condition drops as tools cut its cell.</summary>
    public sealed class GridArtifact
    {
        public int index, x, y, z, footprint = 1;
        public float fragility = 0.5f, condition = 1f;
        public bool recovered;
    }

    public struct StrokeResult
    {
        public bool accepted, refused;
        public float removed;          // total cell fraction removed
        public SoilMaterial material;   // material at the stroke centre
        public float damageDealt;
        public GridArtifact damaged;
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

        public ExcavationGrid(int nx, int nz, int ny, float cell, byte[] data = null)
        {
            this.nx = Mathf.Max(1, nx); this.nz = Mathf.Max(1, nz); this.ny = Mathf.Max(2, ny); this.cell = Mathf.Max(0.05f, cell);
            fill = new byte[this.nx * this.nz * this.ny];
            if (data != null && data.Length == fill.Length) Buffer.BlockCopy(data, 0, fill, 0, fill.Length);
            else for (int i = 0; i < fill.Length; i++) fill[i] = 255;
        }

        /// <summary>Grid for a site of <paramref name="radius"/> (square footprint 2r) dug to <paramref name="depth"/> + margin; last layer is bedrock.</summary>
        public static ExcavationGrid ForSite(float radius, float depth, float cell)
        {
            int n = Mathf.Max(2, Mathf.RoundToInt(2f * radius / cell));
            int layers = Mathf.CeilToInt((depth + 0.75f) / cell) + 1;
            return new ExcavationGrid(n, n, layers, cell);
        }

        public float SizeX => nx * cell;
        public float SizeZ => nz * cell;
        public int Index(int x, int y, int z) => x + nx * (z + nz * y);
        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < nx && z < nz;
        public byte Fill(int x, int y, int z) => fill[Index(x, y, z)];

        public SoilMaterial MaterialAt(int y)
        {
            if (y >= ny - 1) return SoilMaterial.Bedrock;
            float d = y * cell;
            return d < 0.25f ? SoilMaterial.Dust : d < 1.0f ? SoilMaterial.Loose : SoilMaterial.Compacted;
        }

        public static float Hardness(SoilMaterial m) => m switch { SoilMaterial.Dust => 0.5f, SoilMaterial.Loose => 1f, SoilMaterial.Compacted => 2f, _ => 99f };

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
                if (!a.recovered && y == a.y && x >= a.x && x < a.x + a.footprint && z >= a.z && z < a.z + a.footprint) return a;
            return null;
        }

        /// <summary>
        /// One tool stroke at grid-local (x, z) metres, aimed at surface depth <paramref name="hitDepth"/>. Removes from
        /// the top cell of every column within the tool radius whose surface is near the aimed depth.
        /// </summary>
        public StrokeResult Stroke(float lx, float lz, float hitDepth, DigToolSpec tool)
        {
            var r = new StrokeResult();
            int cx = Mathf.FloorToInt(lx / cell), cz = Mathf.FloorToInt(lz / cell);
            int span = Mathf.CeilToInt(tool.radius / cell) + 1;
            bool centreSet = false;
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
                    if (Mathf.Abs(TopDepth(x, z) - hitDepth) > cell * 1.5f) continue;
                    var mat = MaterialAt(y);
                    if (centre || !centreSet) { r.material = mat; centreSet |= centre; }
                    float hard = Hardness(mat);
                    if (hard > tool.maxHardness) { r.refused = true; continue; }
                    float amount = tool.removal / Mathf.Max(0.25f, hard) * (1f - 0.5f * Mathf.Clamp01(dist / Mathf.Max(0.01f, tool.radius)));
                    int i = Index(x, y, z);
                    float before = fill[i] / 255f;
                    float take = Mathf.Min(before, amount);
                    if (take <= 0f) continue;
                    fill[i] = (byte)Mathf.Clamp(Mathf.RoundToInt((before - take) * 255f), 0, 255);
                    if (fill[i] <= 2) fill[i] = 0;
                    r.removed += take; r.accepted = true;

                    // Damage: cutting the artifact's own cell; heavy tools also bruise it through the cell above.
                    var hit = ArtifactAt(x, y, z);
                    float factor = 1f;
                    if (hit == null && tool.damage >= 0.5f && y + 1 < ny) { hit = ArtifactAt(x, y + 1, z); factor = 0.25f; }
                    if (hit != null && tool.damage > 0f)
                    {
                        float dmg = tool.damage * hit.fragility * take * factor;
                        hit.condition = Mathf.Max(0.05f, hit.condition - dmg);
                        r.damageDealt += dmg; r.damaged = hit;
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
