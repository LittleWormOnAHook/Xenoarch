using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.World
{
    /// <summary>
    /// t186u: builds the landing terrain of a generated planet (<see cref="ProceduralGalaxy"/>) in the shared
    /// Surface_Procedural scene, from the recipe of the ship's current body: heights, pads (base zone, dig sites, landing
    /// hints), generated colour layers with slope rock, holes over each dig site (the dig grid's trench mesh fills them),
    /// and a default spawn point at the base landing. Runs in Awake, so the terrain exists before the scene-loaded
    /// directors (shuttle landing, excavation, rocks) look for it. Nothing is saved: the terrain is rebuilt every landing.
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
                // Played directly in the editor: use the first generated planet that has a surface.
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
            int res = ProceduralGalaxy.HeightRes;
            float size = ProceduralGalaxy.TerrainSize, half = size * 0.5f, step = size / (res - 1);
            float top = Mathf.Max(1f, rec.relief);

            var data = new TerrainData { name = "Procedural " + rec.bodyId, hideFlags = HideFlags.DontSave };
            data.heightmapResolution = res;
            data.size = new Vector3(size, top, size);
            var h = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = rec.Height(-half + x * step, -half + z * step) / top;
            data.SetHeights(0, 0, h);

            // Holes: one per dig site (2 m cells; site centres sit on even metres so the hole matches the dig grid).
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

            // Layers: lowland, highland, rock (slope).
            var layers = new[]
            {
                Layer("Low", rec.low, rec.seed, 0.10f, 18f),
                Layer("High", rec.high, rec.seed + 1, 0.12f, 24f),
                Layer("Rock", rec.rock, rec.seed + 2, 0.22f, 10f),
            };
            data.terrainLayers = layers;
            int ar = 256;
            data.alphamapResolution = ar;
            var a = new float[ar, ar, 3];
            for (int z = 0; z < ar; z++)
                for (int x = 0; x < ar; x++)
                {
                    float nx = (x + 0.5f) / ar, nz = (z + 0.5f) / ar;
                    float steep = data.GetSteepness(nx, nz);
                    float ht = data.GetInterpolatedHeight(nx, nz) / top;
                    float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 34f, steep));
                    float hi = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, ht + (Mathf.PerlinNoise(nx * 23f + rec.seed % 97, nz * 23f) - 0.5f) * 0.2f));
                    a[z, x, 2] = rock;
                    a[z, x, 1] = (1f - rock) * hi;
                    a[z, x, 0] = (1f - rock) * (1f - hi);
                }
            data.SetAlphamaps(0, 0, a);

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

            // Default spawn / shuttle pad at the base landing.
            if (PlayerSpawnPoint.Find(PlayerSpawnIds.Default) == null)
            {
                var sp = new GameObject("Spawn_Default");
                sp.transform.SetParent(transform, false);
                sp.transform.position = new Vector3(0f, rec.Height(0f, -40f) + 0.1f, -40f);
                sp.AddComponent<PlayerSpawnPoint>();
            }
            Debug.Log($"[Xenoarch] ProceduralSurface: built {rec.bodyId} ({rec.type}, relief {rec.relief:0} m, {rec.holes.Count} dig sites)");
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
                    // tileable noise: sample a torus-like wrap by blending the four shifted copies
                    float u = (float)x / n, v = (float)y / n;
                    float s = Tile(u, v, ox, oz, 6f) * 0.7f + Tile(u, v, ox + 31f, oz + 7f, 17f) * 0.3f;
                    var col = c * (1f - contrast + 2f * contrast * s); col.a = 1f;
                    px[x + y * n] = col;
                }
            tex.SetPixels(px); tex.Apply(true, false);
            return new TerrainLayer { name = "ProcLayer " + name, diffuseTexture = tex, tileSize = new Vector2(tile, tile), hideFlags = HideFlags.DontSave };
        }

        static float Tile(float u, float v, float ox, float oz, float f)
        {
            float a = Mathf.PerlinNoise(ox + u * f, oz + v * f), b = Mathf.PerlinNoise(ox + (u - 1f) * f, oz + v * f);
            float c = Mathf.PerlinNoise(ox + u * f, oz + (v - 1f) * f), d = Mathf.PerlinNoise(ox + (u - 1f) * f, oz + (v - 1f) * f);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }
    }
}
