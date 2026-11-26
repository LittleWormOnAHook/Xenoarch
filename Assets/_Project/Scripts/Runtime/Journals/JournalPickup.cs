using DMI.Xenoarch.Interaction;
using UnityEngine;

namespace DMI.Xenoarch.Journals
{
    /// <summary>
    /// A survey-crew datapad lying on the ground: E "Read journal" adds its <see cref="JournalEntry"/> to the journal
    /// (Inventory, JOURNALS) and opens the note right away. Hidden once that entry is collected (on any body).
    /// The datapad visual (dark slate, glowing screen, faint light) is built in code with shared materials.
    /// </summary>
    public sealed class JournalPickup : MonoBehaviour, IInteractable
    {
        [Tooltip("JournalEntry id (journal.*).")] public string entryId;
        [Tooltip("Faint point light over the screen (0 = none).")] public float glow = 0.35f;

        public Transform InteractionTransform => transform;
        JournalEntry _entry;
        JournalEntry Entry => _entry != null ? _entry : (_entry = JournalService.Find(entryId));

        void Awake() { EnsureVisual(); EnsureCollider(); }

        void Start()
        {
            var s = Core.XenoarchRuntime.Session;
            if (s != null && s.Journals.Has(entryId)) gameObject.SetActive(false);
        }

        public float GetPriority(in InteractionContext c) => Entry != null && c.Session != null ? 1.2f : -1f;
        public string GetPrompt(in InteractionContext c) => "Read journal" + (Entry != null ? $": {Entry.title}" : "");
        public float GetHoldDuration(in InteractionContext c) => 0f;

        public bool TryInteract(in InteractionContext c)
        {
            var s = c.Session; var e = Entry;
            if (s == null || e == null) return false;
            bool isNew = s.Journals.Collect(e.Id);
            if (isNew) UI.Toast.Show($"Journal added: {e.title}  (Inventory > Journals)", 4f);
            UI.JournalReader.Show(e.Id);
            gameObject.SetActive(false);
            return true;
        }

        void EnsureCollider()
        {
            if (GetComponent<Collider>() != null) return;
            var bc = gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.08f, 0f); bc.size = new Vector3(0.5f, 0.2f, 0.6f); bc.isTrigger = true;
        }

        static Material _slate, _screen;
        static Material Mat(ref Material cache, string name, Color baseCol, Color emission)
        {
            if (cache != null) return cache;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return null;
            cache = new Material(sh) { name = name };
            cache.SetColor("_BaseColor", baseCol);
            cache.SetFloat("_Smoothness", 0.55f);
            if (emission.maxColorComponent > 0f) { cache.EnableKeyword("_EMISSION"); cache.SetColor("_EmissionColor", emission); }
            Scenes.ShaderPrewarm.Warm(cache);
            return cache;
        }

        void EnsureVisual()
        {
            if (transform.Find("Datapad") != null) return;
            var root = new GameObject("Datapad").transform; root.SetParent(transform, false);
            root.localPosition = new Vector3(0f, 0.012f, 0f); root.localRotation = Quaternion.Euler(4f, 0f, 0f);
            Part(root, PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.24f, 0.018f, 0.32f), Mat(ref _slate, "Journal Slate", new Color(0.12f, 0.13f, 0.15f), Color.black));
            var teal = new Color(0.41f, 0.87f, 0.84f);
            Part(root, PrimitiveType.Cube, new Vector3(0f, 0.0095f, 0.012f), new Vector3(0.2f, 0.002f, 0.25f), Mat(ref _screen, "Journal Screen", teal * 0.4f, teal * 1.6f));
            if (glow > 0f)
            {
                var l = new GameObject("Glow").AddComponent<Light>(); l.transform.SetParent(root, false); l.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                l.type = LightType.Point; l.color = teal; l.intensity = glow; l.range = 1.6f; l.shadows = LightShadows.None;
            }
        }

        static void Part(Transform parent, PrimitiveType t, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(t); g.name = t.ToString();
            var col = g.GetComponent<Collider>(); if (col != null) Destroy(col);
            g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            var r = g.GetComponent<Renderer>(); if (m != null) r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.41f, 0.87f, 0.84f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(new Vector3(0f, 0.02f, 0f), new Vector3(0.24f, 0.03f, 0.32f));
            Gizmos.DrawWireCube(new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 1f, 0.6f));
        }
#endif
    }
}
