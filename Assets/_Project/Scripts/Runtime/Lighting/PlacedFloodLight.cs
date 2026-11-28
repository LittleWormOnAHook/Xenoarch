using DMI.Xenoarch.Core;
using DMI.Xenoarch.Interaction;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;

namespace DMI.Xenoarch.Lighting
{
    /// <summary>
    /// Placeable flood light on a stand (inventory item <see cref="PlayerLighting.FloodLightId"/>).
    /// Placed from the inventory DETAILS button or a hotbar / tool-wheel slot; snapped to the ground in
    /// front of the player, facing their look direction. E picks it back up. The tripod mesh is a
    /// PLACEHOLDER built from primitives until a real model is assigned. Runtime only: not written into
    /// scenes and not saved (same as dropped items).
    /// </summary>
    public sealed class PlacedFloodLight : MonoBehaviour, IInteractable
    {
        [Header("Lamp (wide ranges for testing on dark planets)")]
        [Range(1f, 200f)] public float range = 30f;
        [Range(5f, 179f)] public float spotAngle = 100f;
        [Range(0f, 179f)] public float innerAngle = 55f;
        [Range(0.01f, 5000f)] public float intensity = 18f;
        public Color color = new Color(1f, 0.93f, 0.8f);
        [Tooltip("Downward pitch of the beam from the look direction (degrees).")]
        [Range(0f, 80f)] public float aimDown = 25f;

        Light _light;

        public Transform InteractionTransform => transform;

        public float GetPriority(in InteractionContext c) =>
            c.Session != null ? 2f : -1f;

        public string GetPrompt(in InteractionContext c) => "Pick up Flood Light";

        public float GetHoldDuration(in InteractionContext c) => 0f;

        public bool TryInteract(in InteractionContext c)
        {
            var s = c.Session;
            if (s == null) return false;
            int left = s.Inventory.Add(PlayerLighting.FloodLightId, 1);
            if (left != 0)
            {
                Toast.Show("Inventory full.");
                return false;
            }
            Debug.Log("[FlashlightDiag] flood light picked up");
            Destroy(gameObject);
            return true;
        }

        void LateUpdate()
        {
            if (_light == null) _light = GetComponentInChildren<Light>();
            if (_light == null) return;
            _light.type = LightType.Spot;
            _light.range = Mathf.Max(0.1f, range);
            _light.spotAngle = Mathf.Clamp(spotAngle, 1f, 179f);
            _light.innerSpotAngle = Mathf.Clamp(innerAngle, 0f, _light.spotAngle);
            _light.intensity = Mathf.Max(0f, intensity);
            _light.color = color;
            _light.shadows = LightShadows.None;
            _light.bounceIntensity = 0f;
        }

        /// <summary>Places one flood light on the ground in front of the player and consumes one from inventory.</summary>
        public static bool TryPlaceFromPlayer(out string why)
        {
            why = null;
            var s = XenoarchRuntime.Session;
            if (s == null) { why = "No game session."; return false; }
            if (s.Inventory.Count(PlayerLighting.FloodLightId) <= 0)
            {
                why = DigToolNotOwned();
                return false;
            }
            var rig = PlayerRigLocator.Find();
            if (rig == null || rig.Root == null) { why = "No player."; return false; }

            Vector3 look = rig.ViewCamera != null ? rig.ViewCamera.transform.forward : rig.Root.forward;
            Vector3 flat = Vector3.ProjectOnPlane(look, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.ProjectOnPlane(rig.Root.forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            flat.Normalize();

            const float distance = 2.2f;
            Vector3 probe = rig.Root.position + flat * distance + Vector3.up * 3f;
            if (!Physics.Raycast(probe, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore))
            {
                why = "No ground in front of you.";
                return false;
            }

            if (s.Inventory.Remove(PlayerLighting.FloodLightId, 1) < 1)
            {
                why = "Couldn't take the flood light from inventory.";
                return false;
            }

            var rot = Quaternion.LookRotation(flat, Vector3.up);
            var go = Build(hit.point, rot);
            var lamp = go.GetComponent<PlacedFloodLight>();
            if (lamp != null && lamp._light != null)
                lamp._light.transform.localRotation = Quaternion.Euler(lamp.aimDown, 0f, 0f);
            Debug.Log($"[FlashlightDiag] flood light placed at {hit.point} yaw={rot.eulerAngles.y:0.#}");
            return true;
        }

        static string DigToolNotOwned() =>
            DMI.Xenoarch.Excavation.DigTool.NotOwnedText("flood light");

        /// <summary>PLACEHOLDER tripod: primitive legs, pole and lamp head. Flagged in the object name.</summary>
        public static GameObject Build(Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("Flood Light (placeholder)");
            root.transform.SetPositionAndRotation(position, rotation);
            var lamp = root.AddComponent<PlacedFloodLight>();
            var mats = MakeMaterials();

            // Three splayed legs and feet.
            float hipY = 0.42f;
            Vector3 hip = new Vector3(0f, hipY, 0f);
            Vector3[] feet =
            {
                new Vector3(0f, 0f, 0.28f),
                new Vector3(0.24f, 0f, -0.16f),
                new Vector3(-0.24f, 0f, -0.16f)
            };
            for (int i = 0; i < feet.Length; i++)
            {
                AddBar(root.transform, "Leg_" + i, hip, feet[i], 0.028f, mats.metalDark);
                AddSphere(root.transform, "Foot_" + i, feet[i] + Vector3.up * 0.03f, 0.04f, mats.metal);
            }

            // Pole and knuckle.
            AddBar(root.transform, "Pole", new Vector3(0f, hipY - 0.02f, 0f), new Vector3(0f, 1.12f, 0f), 0.03f, mats.metal);
            AddSphere(root.transform, "Knuckle", new Vector3(0f, 1.14f, 0.02f), 0.055f, mats.metalDark);

            // Lamp head (housing + lens), slightly in front of the pole.
            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.16f, 0.08f);
            head.transform.localRotation = Quaternion.Euler(lamp.aimDown, 0f, 0f);
            AddBox(head.transform, "Housing", new Vector3(0f, 0f, 0f), new Vector3(0.28f, 0.18f, 0.22f), mats.metal);
            AddSphere(head.transform, "Lens", new Vector3(0f, 0f, 0.13f), 0.08f, mats.lens);

            var lightGo = new GameObject("FloodSpot");
            lightGo.transform.SetParent(head.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.14f);
            lightGo.transform.localRotation = Quaternion.identity;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Spot;
            l.range = lamp.range;
            l.spotAngle = lamp.spotAngle;
            l.innerSpotAngle = lamp.innerAngle;
            l.intensity = lamp.intensity;
            l.color = lamp.color;
            l.shadows = LightShadows.None;
            l.bounceIntensity = 0f;
            lamp._light = l;

            // Interact volume covering the stand (Interactor default range is 2.5 m).
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.65f, 0.05f);
            col.size = new Vector3(0.7f, 1.3f, 0.7f);

            return root;
        }

        struct Mats { public Material metal, metalDark, lens; }

        static Mats MakeMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            Material M(string name, Color c, float metallic, float smooth, Color? emit = null)
            {
                var m = new Material(lit) { hideFlags = HideFlags.HideAndDontSave, name = name };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                if (emit.HasValue && m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emit.Value);
                }
                return m;
            }
            return new Mats
            {
                metal = M("FloodMetal", new Color(0.72f, 0.74f, 0.76f), 0.8f, 0.55f),
                metalDark = M("FloodMetalDark", new Color(0.28f, 0.30f, 0.32f), 0.7f, 0.4f),
                lens = M("FloodLens", new Color(1f, 0.92f, 0.7f), 0.1f, 0.85f, new Color(2.2f, 1.7f, 0.7f))
            };
        }

        static void AddBar(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            Vector3 mid = (a + b) * 0.5f;
            Vector3 d = b - a;
            float len = d.magnitude;
            go.transform.localPosition = mid;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.sqrMagnitude > 1e-6f ? d : Vector3.up);
            go.transform.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
            ApplyMat(go, mat);
        }

        static void AddSphere(Transform parent, string name, Vector3 local, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * (radius * 2f);
            ApplyMat(go, mat);
        }

        static void AddBox(Transform parent, string name, Vector3 local, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = size;
            ApplyMat(go, mat);
        }

        static void ApplyMat(GameObject go, Material mat)
        {
            var r = go.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }
    }
}
