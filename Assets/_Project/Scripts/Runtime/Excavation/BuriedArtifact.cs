using DMI.Xenoarch.Artifacts;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.UI;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// A buried find inside a dig site (placeholder primitive visual, sized to the cells it fills: big finds show parts
    /// first). Hidden by the ground (Digger terrain or trench mesh) until dug out; once at
    /// least half exposed it can be recovered (hold E). Recovering before it is fully exposed pries it loose and costs
    /// condition. Recovery creates the artifact instance and adds it to the inventory; the site records it so it never
    /// respawns.
    /// </summary>
    public sealed class BuriedArtifact : MonoBehaviour, IInteractable
    {
        public const float MinExposure = 0.5f, SafeExposure = 0.9f;
        public ExcavationSite Site { get; private set; }
        public GridArtifact Grid { get; private set; }
        public ArtifactDefinition Definition { get; private set; }
        public float Exposure => Site != null && Grid != null ? Site.Exposure(Grid) : 0f;

        public Transform InteractionTransform => transform;

        public static BuriedArtifact Spawn(ExcavationSite site, GridArtifact g, ArtifactDefinition def)
        {
            var go = Visual(def);
            go.name = $"Buried {def.Id}";
            go.transform.SetParent(site.transform, false);
            float f = g.footprint, c = site.Grid.cell;
            int sx = Mathf.Min(site.Grid.nx - 1, g.x + g.footprint / 2), sz = Mathf.Min(site.Grid.nz - 1, g.z + g.footprint / 2);
            go.transform.position = site.transform.TransformPoint(new Vector3((g.x + f * 0.5f) * c, site.Grid.Surface(sx, sz) - (g.y + Mathf.Max(1, g.height) * 0.5f) * c, (g.z + f * 0.5f) * c));
            // Fit inside the cells it occupies.
            var e = Extents(def);
            float fit = Mathf.Min(1f, Mathf.Min(f * c * 0.98f / Mathf.Max(0.01f, Mathf.Max(e.x, e.z)), Mathf.Max(1, g.height) * c * 0.98f / Mathf.Max(0.01f, e.y)));
            go.transform.localScale = Vector3.one * fit;
            go.transform.rotation = Quaternion.Euler(0f, (g.index * 47) % 360, 0f);
            var b = go.AddComponent<BuriedArtifact>();
            b.Site = site; b.Grid = g; b.Definition = def;
            return b;
        }

        /// <summary>Bounding size (m) of the placeholder visual: x, height, z.</summary>
        public static Vector3 Extents(ArtifactDefinition def)
        {
            Shape(def, out var p, out var scale);
            bool tall = p == PrimitiveType.Cylinder || p == PrimitiveType.Capsule;
            return new Vector3(scale.x, tall ? scale.y * 2f : scale.y, scale.z);
        }

        static void Shape(ArtifactDefinition def, out PrimitiveType p, out Vector3 scale)
        {
            float s = def.size;
            switch (def.shape)
            {
                case ArtifactShape.Tablet: p = PrimitiveType.Cube; scale = new Vector3(s, s * 0.12f, s * 0.7f); break;
                case ArtifactShape.Rod: p = PrimitiveType.Cylinder; scale = new Vector3(s * 0.18f, s * 0.5f, s * 0.18f); break;
                case ArtifactShape.Disc: p = PrimitiveType.Cylinder; scale = new Vector3(s, s * 0.04f, s); break;
                case ArtifactShape.Box: p = PrimitiveType.Cube; scale = new Vector3(s, s * 0.6f, s * 0.8f); break;
                case ArtifactShape.Crystal: p = PrimitiveType.Cube; scale = new Vector3(s * 0.35f, s, s * 0.35f); break;
                case ArtifactShape.Ring: p = PrimitiveType.Cylinder; scale = new Vector3(s, s * 0.08f, s); break;
                case ArtifactShape.Device: p = PrimitiveType.Capsule; scale = new Vector3(s * 0.5f, s * 0.35f, s * 0.5f); break;
                default: p = PrimitiveType.Sphere; scale = new Vector3(s, s * 0.55f, s * 0.8f); break;
            }
        }

        static readonly System.Collections.Generic.Dictionary<string, Material> _artifactMats = new System.Collections.Generic.Dictionary<string, Material>();
        /// <summary>One shared material per artifact definition (was one per spawned piece), registered with the shader
        /// warm-up so a recovered piece never draws as the cyan placeholder.</summary>
        static Material ArtifactMaterial(ArtifactDefinition def)
        {
            string key = def.Id ?? "";
            if (_artifactMats.TryGetValue(key, out var m) && m != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return null;
            m = new Material(sh) { name = $"Artifact {def.Id}" };
            m.SetColor("_BaseColor", def.tint);
            bool metal = def.material != null && (def.material.Contains("alloy") || def.material.Contains("metal"));
            m.SetFloat("_Metallic", metal ? 0.8f : 0.1f);
            m.SetFloat("_Smoothness", metal ? 0.55f : 0.25f);
            if (def.artifactCategory == ArtifactCategory.DataCrystal || def.artifactCategory == ArtifactCategory.PowerSource)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", def.tint * 1.5f);
            }
            _artifactMats[key] = m;
            DMI.Xenoarch.Scenes.ShaderPrewarm.Warm(m);
            return m;
        }

        static GameObject Visual(ArtifactDefinition def)
        {
            Shape(def, out var p, out var scale);
            var root = new GameObject();
            var vis = GameObject.CreatePrimitive(p);
            Object.Destroy(vis.GetComponent<Collider>());
            vis.transform.SetParent(root.transform, false);
            vis.transform.localScale = scale;
            if (def.shape == ArtifactShape.Crystal) vis.transform.localRotation = Quaternion.Euler(0f, 45f, 20f);
            var m = ArtifactMaterial(def);
            if (m != null) vis.GetComponent<Renderer>().sharedMaterial = m;
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            float m2 = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            box.size = new Vector3(Mathf.Max(scale.x, 0.12f), Mathf.Max(scale.y, 0.12f), Mathf.Max(scale.z, 0.12f)) * (p == PrimitiveType.Cylinder || p == PrimitiveType.Capsule ? 2f : 1f);
            if (p == PrimitiveType.Cylinder) box.size = new Vector3(box.size.x * 0.5f, scale.y * 2f, box.size.z * 0.5f);
            box.size = Vector3.Max(box.size, Vector3.one * Mathf.Min(0.15f, m2));
            return root;
        }

        public void Refresh() { }

        public float GetPriority(in InteractionContext c) => Exposure >= MinExposure && c.Session != null ? 2f : -1f;

        public string GetPrompt(in InteractionContext c)
        {
            float e = Exposure;
            string name = Definition != null ? Definition.unidentifiedName : "object";
            if (e >= SafeExposure) return $"Recover {name} (condition {Grid.condition:P0})";
            return $"Pry out {name} ({e:P0} exposed, about -{PryLoss(e):P0} condition; brush to {SafeExposure:P0} for a clean lift)";
        }

        /// <summary>Condition lost by prying a find out before it is safely exposed (scaled by fragility).</summary>
        float PryLoss(float e) => Definition == null || e >= SafeExposure ? 0f : 0.25f * Definition.fragility * (SafeExposure - e) / (SafeExposure - MinExposure);

        /// <summary>Deliberate recovery: a clean lift takes 1.2 s; prying a half-buried find takes up to 2.5 s.</summary>
        public float GetHoldDuration(in InteractionContext c)
        {
            float e = Exposure;
            return 1.2f + 1.3f * Mathf.Clamp01((SafeExposure - e) / (SafeExposure - MinExposure));
        }

        public bool TryInteract(in InteractionContext c)
        {
            var s = c.Session;
            if (s == null || Definition == null || Grid.recovered) return false;
            float e = Exposure;
            if (e < MinExposure) return false;
            if (s.Inventory.FreeSlots <= 0) { Toast.Show("Inventory full."); return false; }
            float cond = Grid.condition;
            if (e < SafeExposure) cond = Mathf.Max(0.05f, cond - PryLoss(e));
            Grid.condition = cond;
            var inst = s.Artifacts.Recover(Definition, cond, Site.Definition.Id, Site.Definition.bodyId);
            if (inst == null || !s.Inventory.AddInstance(Definition.Id, inst.instanceId)) { Debug.LogWarning("[Xenoarch] Recovery failed (inventory)"); return false; }
            Site.OnRecovered(this, inst.instanceId);
            string quality = cond >= 0.9f ? "pristine" : cond >= 0.7f ? "good" : cond >= 0.4f ? "worn" : "badly damaged";
            Toast.Show($"RECOVERED: {Definition.unidentifiedName} ({quality}, condition {cond:P0}). Research it in the ship's lab.", 5f);
            Debug.Log($"[Xenoarch] Recovered {Definition.Id} as {inst.instanceId} at {e:P0} exposure, condition {cond:F2}");
            Destroy(gameObject);
            return true;
        }
    }
}
