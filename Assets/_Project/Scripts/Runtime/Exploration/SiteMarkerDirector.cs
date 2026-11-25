using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>
    /// Places a beacon on every pinpointed archaeological site of the loaded scene (state from ExplorationService, so
    /// marks survive scene reloads and saves). Placeholder visuals: an emissive pole snapped to the ground.
    /// </summary>
    public sealed class SiteMarkerDirector : MonoBehaviour
    {
        public static SiteMarkerDirector Instance { get; private set; }
        readonly Dictionary<string, GameObject> _markers = new Dictionary<string, GameObject>();
        static Material _mat;

        void Awake() => Instance = this;
        void OnEnable() => SceneManager.sceneLoaded += OnLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnLoaded;
        void Start() => Refresh();
        void OnLoaded(Scene s, LoadSceneMode m) { _markers.Clear(); Refresh(); }

        public void Refresh()
        {
            var x = XenoarchRuntime.Session?.Exploration;
            if (x == null) return;
            string scene = SceneManager.GetActiveScene().name;
            foreach (var site in x.SitesInScene(scene))
            {
                if (x.Site(site.Id).revealLevel < (int)SiteRevealLevel.Pinpointed) continue;
                if (_markers.TryGetValue(site.Id, out var go) && go != null) continue;
                _markers[site.Id] = Spawn(site);
            }
        }

        static GameObject Spawn(ArchaeologicalSiteDefinition site)
        {
            Vector3 p = site.position;
            if (Physics.Raycast(p + Vector3.up * 200f, Vector3.down, out var hit, 400f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
            var root = new GameObject($"SiteMarker {site.Id}");
            SceneManager.MoveGameObjectToScene(root, SceneManager.GetActiveScene());
            root.transform.position = p;
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            pole.transform.localScale = new Vector3(0.06f, 1.25f, 0.06f);
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ring.transform.localScale = new Vector3(site.radius * 2f, 0.01f, site.radius * 2f);
            var mat = MarkerMaterial();
            if (mat != null) { pole.GetComponent<Renderer>().sharedMaterial = mat; ring.GetComponent<Renderer>().sharedMaterial = mat; }
            return root;
        }

        static Material MarkerMaterial()
        {
            if (_mat != null) return _mat;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) return null;
            _mat = new Material(sh) { name = "SiteMarker (runtime)" };
            var c = new Color(1f, 0.62f, 0.15f, 0.55f);
            _mat.SetColor("_BaseColor", c);
            _mat.SetFloat("_Surface", 1f); _mat.SetFloat("_Blend", 0f);
            _mat.SetOverrideTag("RenderType", "Transparent");
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_ZWrite", 0);
            _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _mat.renderQueue = 3000;
            return _mat;
        }
    }
}
