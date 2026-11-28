using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.UI;
using UnityEngine;

namespace DMI.Xenoarch.Materials
{
    /// <summary>A planet material deposit (placeholder rocks). Hold E to collect; comes back after the material's respawn time.</summary>
    public sealed class ResourceNode : MonoBehaviour, IInteractable
    {
        public string NodeId { get; private set; }
        public MaterialDefinition Def { get; private set; }
        public int Units { get; private set; }
        GameObject _visual;
        Collider _collider;

        public Transform InteractionTransform => transform;

        public static ResourceNode Spawn(MaterialDefinition m, string nodeId, int units, Vector3 pos, float yaw, UnityEngine.SceneManagement.Scene scene)
        {
            var go = new GameObject($"Resource {m.DisplayNameOrId}");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var n = go.AddComponent<ResourceNode>();
            n.NodeId = nodeId; n.Def = m; n.Units = units;
            n.BuildVisual();
            return n;
        }

        static readonly System.Collections.Generic.Dictionary<string, Material> _nodeMats = new System.Collections.Generic.Dictionary<string, Material>();
        /// <summary>One shared material per resource kind (was one per node), registered with the shader warm-up.</summary>
        static Material NodeMaterial(MaterialDefinition def)
        {
            string key = def != null ? def.Id ?? "" : "";
            if (_nodeMats.TryGetValue(key, out var mat) && mat != null) return mat;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null || def == null) return null;
            mat = new Material(sh) { name = $"Node {def.Id}" };
            mat.SetColor("_BaseColor", def.tint);
            mat.SetFloat("_Smoothness", def.tint.b > 0.7f ? 0.7f : 0.15f);
            if (def.fuelValue > 0f) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", def.tint * 0.4f); }
            _nodeMats[key] = mat;
            DMI.Xenoarch.Scenes.ShaderPrewarm.Warm(mat);
            return mat;
        }

        void BuildVisual()
        {
            _visual = new GameObject("Visual");
            _visual.transform.SetParent(transform, false);
            var mat = NodeMaterial(Def);
            var rng = new System.Random(NodeId.GetHashCode());
            for (int i = 0; i < 3; i++)
            {
                var p = GameObject.CreatePrimitive(i == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere);
                Destroy(p.GetComponent<Collider>());
                p.transform.SetParent(_visual.transform, false);
                float s = 0.25f + 0.2f * (float)rng.NextDouble();
                p.transform.localPosition = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.5f, s * 0.35f, ((float)rng.NextDouble() - 0.5f) * 0.5f);
                p.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 40f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 40f);
                p.transform.localScale = new Vector3(s, s * 0.8f, s * 1.1f);
                if (mat != null) p.GetComponent<Renderer>().sharedMaterial = mat;
            }
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.2f, 0f); box.size = new Vector3(0.9f, 0.45f, 0.9f);
            _collider = box;
        }

        public void SetAvailable(bool on) { if (_visual != null) _visual.SetActive(on); if (_collider != null) _collider.enabled = on; }

        public float GetPriority(in InteractionContext c) => c.Session != null && _visual != null && _visual.activeSelf ? 1.5f : -1f;
        public string GetPrompt(in InteractionContext c) => $"Collect {Def.DisplayNameOrId} x{Units}";
        public float GetHoldDuration(in InteractionContext c) => 0.8f;

        public bool TryInteract(in InteractionContext c)
        {
            var s = c.Session;
            if (s == null || Def == null) return false;
            string bodyId = s.Exploration?.CurrentBody?.Id ?? s.Exploration?.Ship.currentBodyId;
            int left = s.Inventory.Add(Def.Id, Units, bodyId, NodeId, 0f);
            if (left == Units)
            {
                OverflowPrompt.OfferWorld(Def.Id, Units, bodyId, NodeId,
                    $"Backpack is full. Send {Def.DisplayNameOrId} x{Units} to the ship, or stash it in a nearby shuttle or rover.",
                    sent =>
                    {
                        if (sent <= 0) return;
                        int remain = Units - sent;
                        if (remain <= 0)
                        {
                            s.Materials.MarkHarvested(NodeId, s.PlaySeconds);
                            SetAvailable(false);
                        }
                        else Units = remain;
                    });
                return false;
            }
            int got = Units - left;
            s.Materials.MarkHarvested(NodeId, s.PlaySeconds);
            SetAvailable(false);
            if (left > 0)
            {
                OverflowPrompt.OfferWorld(Def.Id, left, bodyId, NodeId,
                    $"Backpack is full after collecting x{got}. Send the remaining x{left} to the ship or a nearby vehicle.",
                    null);
            }
            Toast.Show($"Collected {Def.DisplayNameOrId} x{got}{(left > 0 ? $" ({left} not in pack)" : "")}. {Hint(Def)}");
            return true;
        }

        static string Hint(MaterialDefinition m) =>
            m.fuelValue > 0f && m.reagentValue > 0 ? "Usable as fuel or lab reagent." :
            m.fuelValue > 0f ? "Usable as ship fuel." :
            m.reagentValue > 0 ? "Usable as lab reagent." : "Refine it in the ship's refiner.";
    }
}
