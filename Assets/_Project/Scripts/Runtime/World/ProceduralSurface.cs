using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.World
{
    /// <summary>
    /// t186u: builds the landing terrain of a generated planet (<see cref="ProceduralGalaxy"/>) in the shared
    /// Surface_Procedural scene, from the recipe of the ship's current body: heights, pads (base zone, dig sites, landing
    /// hints), planet-type layers (PBR catalog when present, otherwise generated matte colour noise with slope rock),
    /// holes over each dig site (the dig grid's trench mesh fills them when Digger is not in the scene),
    /// and a default spawn point at the base landing. Runs in Awake, so the terrain exists before the scene-loaded
    /// directors (shuttle landing, excavation, rocks) look for it. Nothing is saved: the terrain is rebuilt every landing.
    /// Baked per-planet scenes do not use this component.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class ProceduralSurface : MonoBehaviour
    {
        [Tooltip("URP Terrain/Lit material (assigned by the setup script). Falls back to Shader.Find at runtime.")]
        public Material terrainMaterial;
        [Tooltip("Body used when the scene is played directly without a generated body as the ship's location.")]
        public string fallbackBodyId;

        public Terrain Terrain { get; private set; }
        public ProceduralPlanetRecipe Recipe { get; private set; }

        void Awake()
        {
            var s = XenoarchRuntime.Session;
            string bodyId = s != null && s.Exploration != null ? s.Exploration.Ship.currentBodyId : null;
            var rec = ProceduralGalaxy.Recipe(bodyId) ?? ProceduralGalaxy.Recipe(fallbackBodyId);
            if (rec == null && s != null && s.Exploration != null && s.Exploration.Galaxy != null)
            {
                foreach (var sys in s.Exploration.Galaxy.AllSystems)
                {
                    foreach (var b in sys.AllBodies) if ((rec = ProceduralGalaxy.Recipe(b.Id)) != null) break;
                    if (rec != null) break;
                }
            }
            if (rec == null) { Debug.LogWarning("[Xenoarch] ProceduralSurface: no generated body to build (ship body '" + bodyId + "')."); return; }
            Recipe = rec;
            Build(rec);
        }

        void Build(ProceduralPlanetRecipe rec)
        {
            float size = ProceduralGalaxy.TerrainSize, half = size * 0.5f;
            var data = CreateTerrainData(rec, cutHoles: true);
            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Procedural Terrain";
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(-half, 0f, -half);
            Terrain = go.GetComponent<Terrain>();
            var mat = terrainMaterial;
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (sh != null) mat = new Material(sh) { name = "Procedural Terrain (runtime)", hideFlags = HideFlags.DontSave };
            }
            if (mat != null) Terrain.materialTemplate = mat;
            Terrain.drawInstanced = true;
            Terrain.heightmapPixelError = 4f;
            Terrain.basemapDistance = 400f;
            Terrain.Flush();

            if (PlayerSpawnPoint.Find(PlayerSpawnIds.Default) == null)
            {
                var sp = new GameObject("Spawn_Default");
                sp.transform.SetParent(transform, false);
                sp.transform.position = new Vector3(0f, rec.Height(0f, -40f) + 0.1f, -40f);
                sp.AddComponent<PlayerSpawnPoint>();
            }
            Debug.Log($"[Xenoarch] ProceduralSurface: built {rec.bodyId} ({rec.type}, relief {rec.relief:0} m, {rec.holes.Count} dig sites)");
        }

        /// <summary>Heightmap, splat layers and optional trench-mesh holes for a generated planet. Used by the runtime
        /// shared scene and by the Editor bake (which saves the TerrainData as an asset and does not cut holes).</summary>
        public static TerrainData CreateTerrainData(ProceduralPlanetRecipe rec, bool cutHoles)
        {
            int res = ProceduralGalaxy.HeightRes;
            float size = ProceduralGalaxy.TerrainSize, half = size * 0.5f, step = size / (res - 1);
            float top = Mathf.Max(1f, rec.relief);

            var data = new TerrainData { name = "Procedural " + rec.bodyId };
            if (!cutHoles) { /* bake: keep the asset visible */ }
            else data.hideFlags = HideFlags.DontSave;
            data.heightmapResolution = res;
            data.size = new Vector3(size, top, size);
            var h = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = rec.Height(-half + x * step, -half + z * step) / top;
            data.SetHeights(0, 0, h);

            if (cutHoles)
            {
                int hr = res - 1;
                var solid = new bool[hr, hr];
                for (int z = 0; z < hr; z++) for (int x = 0; x < hr; x++) solid[z, x] = true;
                foreach (var hole in rec.holes)
                {
                    int x0 = Mathf.RoundToInt((hole.x - hole.z + half) / step), x1 = Mathf.RoundToInt((hole.x + hole.z + half) / step);
                    int z0 = Mathf.RoundToInt((hole.y - hole.z + half) / step), z1 = Mathf.RoundToInt((hole.y + hole.z + half) / step);
                    for (int z = Mathf.Max(0, z0); z < Mathf.Min(hr, z1); z++)
                        for (int x = Mathf.Max(0, x0); x < Mathf.Min(hr, x1); x++) solid[z, x] = false;
                }
                data.SetHoles(0, 0, solid);
            }

            var layers = BuildLayers(rec, out bool generated);
            data.terrainLayers = layers;
            int n = layers.Length, ar = 256;
            data.alphamapResolution = ar;
            var a = new float[ar, ar, n];
            for (int z = 0; z < ar; z++)
                for (int x = 0; x < ar; x++)
                {
                    float nx = (x + 0.5f) / ar, nz = (z + 0.5f) / ar;
                    float steep = data.GetSteepness(nx, nz);
                    float ht = data.GetInterpolatedHeight(nx, nz) / top;
                    float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 34f, steep));
                    float hi = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, ht + (Mathf.PerlinNoise(nx * 23f + rec.seed % 97, nz * 23f) - 0.5f) * 0.2f));
                    // 0 low/regolith, 1 high or soil (kept low on the surface: soil is for dig walls), last rock, optional crust/ice patches.
                    int rockIdx = n > 2 ? 2 : n - 1;
                    int patchIdx = n > 3 ? 3 : -1;
                    float patch = 0f;
                    if (patchIdx >= 0)
                    {
                        float nzp = Mathf.PerlinNoise(rec.seed + nx * 9f, rec.seed + nz * 9f) * 0.7f + Mathf.PerlinNoise(rec.seed * 2 + nx * 31f, nz * 31f) * 0.3f;
                        patch = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.68f, nzp)) * (1f - rock) * 0.85f;
                    }
                    float remain = 1f - rock - patch;
                    a[z, x, 0] = remain * (generated ? (1f - hi) : 1f);
                    if (n > 1) a[z, x, 1] = generated ? remain * hi : 0f; // catalog slot 1 is usually dig-wall soil, not painted on the surface
                    if (rockIdx >= 0) a[z, x, rockIdx] += rock;
                    if (patchIdx >= 0) a[z, x, patchIdx] += patch;
                }
            data.SetAlphamaps(0, 0, a);
            return data;
        }

        static TerrainLayer[] BuildLayers(ProceduralPlanetRecipe rec, out bool generated)
        {
            var entry = PlanetSurfaceCatalog.For(rec.type);
            var list = new System.Collections.Generic.List<TerrainLayer>();
            if (entry != null && entry.layers != null)
                foreach (var l in entry.layers) if (l != null) list.Add(l);
            if (entry != null && entry.generateIceLayer && list.Count < 8)
                list.Add(Layer("Ice", entry.iceColor, rec.seed + 9, 0.08f, 14f));
            if (list.Count >= 2) { generated = false; return list.ToArray(); }
            generated = true;
            return new[]
            {
                Layer("Low", rec.low, rec.seed, 0.10f, 18f),
                Layer("High", rec.high, rec.seed + 1, 0.12f, 24f),
                Layer("Rock", rec.rock, rec.seed + 2, 0.22f, 10f),
            };
        }

        static TerrainLayer Layer(string name, Color c, int seed, float contrast, float tile)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "ProcLayer " + name, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            var px = new Color[n * n];
            float ox = seed % 251, oz = (seed / 251) % 251;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (float)x / n, v = (float)y / n;
                    float s = Tile(u, v, ox, oz, 6f) * 0.7f + Tile(u, v, ox + 31f, oz + 7f, 17f) * 0.3f;
                    // Alpha must stay 0: URP Terrain/Lit defaults smoothness to diffuse alpha, which made these worlds glossy.
                    var col = c * (1f - contrast + 2f * contrast * s); col.a = 0f;
                    px[x + y * n] = col;
                }
            tex.SetPixels(px); tex.Apply(true, false);
            return new TerrainLayer
            {
                name = "ProcLayer " + name,
                diffuseTexture = tex,
                tileSize = new Vector2(tile, tile),
                hideFlags = HideFlags.DontSave,
                metallic = 0f,
                smoothness = 0f,
                specular = Color.black,
                smoothnessSource = TerrainLayerSmoothnessSource.Constant,
            };
        }

        static float Tile(float u, float v, float ox, float oz, float f)
        {
            float a = Mathf.PerlinNoise(ox + u * f, oz + v * f), b = Mathf.PerlinNoise(ox + (u - 1f) * f, oz + v * f);
            float c = Mathf.PerlinNoise(ox + u * f, oz + (v - 1f) * f), d = Mathf.PerlinNoise(ox + (u - 1f) * f, oz + (v - 1f) * f);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }
    }
}