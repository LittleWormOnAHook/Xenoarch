using UnityEngine;
using UnityEngine.Rendering;

namespace DMI.Xenoarch.Exploration
{
    /// <summary>
    /// Runs the shuttle prefab's own "Afterburner" particle systems (the two reactor nozzles, inactive in the prefab) while it
    /// descends, then cuts them on touchdown. The vendor "Flare" material has no texture and draws grey quads, so on the
    /// runtime instance only (never the prefab asset) the renderers get a soft additive sprite material and a warm colour.
    /// Added at runtime by <see cref="ShuttleDirector"/>; the prefab is never modified.
    /// </summary>
    public sealed class ShuttleThrusters : MonoBehaviour
    {
        const string NozzleName = "Afterburner";
        ParticleSystem[] _systems;
        bool _styled;
        float _offAt = -1f;

        public static ShuttleThrusters For(Transform holder)
        {
            if (holder == null) return null;
            var t = holder.GetComponent<ShuttleThrusters>();
            return t != null ? t : holder.gameObject.AddComponent<ShuttleThrusters>();
        }

        /// <summary>Thrusters on: nozzle objects active, particles emitting.</summary>
        public void Ignite()
        {
            _offAt = -1f;
            Collect();
            if (_systems == null || _systems.Length == 0) { Debug.LogWarning("[ShuttleDiag] thrusters: no 'Afterburner' particle systems found on the shuttle"); return; }
            foreach (var nozzle in Nozzles()) nozzle.gameObject.SetActive(true);
            foreach (var ps in _systems)
            {
                if (ps == null) continue;
                ps.Clear(true);
                ps.Play(true);
            }
            Debug.Log($"[ShuttleDiag] thrusters ON ({_systems.Length} particle systems)");
        }

        /// <summary>Thrusters off: stop emitting now (existing particles fade out), deactivate the nozzles shortly after.</summary>
        public void Cut()
        {
            if (_systems == null) return;
            foreach (var ps in _systems) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _offAt = Time.time + 1.5f;
            Debug.Log("[ShuttleDiag] thrusters OFF");
        }

        void Update()
        {
            if (_offAt < 0f || Time.time < _offAt) return;
            _offAt = -1f;
            foreach (var nozzle in Nozzles()) if (nozzle != null) nozzle.gameObject.SetActive(false);
        }

        System.Collections.Generic.IEnumerable<Transform> Nozzles()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == NozzleName) yield return t;
        }

        void Collect()
        {
            if (_systems != null && _systems.Length > 0) return;
            var list = new System.Collections.Generic.List<ParticleSystem>();
            foreach (var nozzle in Nozzles())
                foreach (var ps in nozzle.GetComponentsInChildren<ParticleSystem>(true)) if (!list.Contains(ps)) list.Add(ps);
            _systems = list.ToArray();
            Style();
        }

        void Style()
        {
            if (_styled) return;
            _styled = true;
            var mat = SoftAdditive();
            if (mat == null) return;
            foreach (var ps in _systems)
            {
                if (ps == null) continue;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null) { r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
                var main = ps.main;
                bool glow = ps.name == "Glow";
                main.startColor = glow ? new Color(0.55f, 0.75f, 1f, 0.55f) : new Color(1f, 0.62f, 0.22f, 0.85f);
            }
        }

        static Material _soft;
        static Material SoftAdditive()
        {
            if (_soft != null) return _soft;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) return null;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "Thruster soft sprite (runtime)", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a *= a;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply(false, true);
            _soft = new Material(sh) { name = "Thruster additive (runtime)" };
            _soft.SetFloat("_Surface", 1f); _soft.SetFloat("_Blend", 2f);
            _soft.SetOverrideTag("RenderType", "Transparent");
            _soft.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); _soft.SetInt("_DstBlend", (int)BlendMode.One); _soft.SetInt("_ZWrite", 0);
            _soft.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); _soft.EnableKeyword("_BLENDMODE_ADD");
            _soft.renderQueue = 3000;
            _soft.SetColor("_BaseColor", Color.white);
            _soft.SetTexture("_BaseMap", tex);
            return _soft;
        }
    }
}