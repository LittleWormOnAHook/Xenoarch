using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DMI.Xenoarch.Footsteps
{
    /// <summary>
    /// Pooled boot prints. Prefers URP Decal Projectors (PC and Mobile renderers already have the
    /// Decal feature enabled). Falls back to a cheap transparent quad if the Decal shader is missing.
    /// Depth is faked with print size, opacity and normal-map strength from the surface table.
    /// </summary>
    public sealed class FootprintPool
    {
        const float ProjectDepth = 0.18f;

        Slot[] _slots;
        int _next;
        int _max;
        bool _decalMode;
        Shader _decalShader;
        Material _quadShared;
        readonly System.Collections.Generic.Dictionary<DecalKey, Material> _decalMats =
            new System.Collections.Generic.Dictionary<DecalKey, Material>(16);

        struct DecalKey : System.IEquatable<DecalKey>
        {
            public EntityId albedo, nrm;
            public int nrmStr;
            public bool Equals(DecalKey other) => albedo == other.albedo && nrm == other.nrm && nrmStr == other.nrmStr;
            public override bool Equals(object obj) => obj is DecalKey k && Equals(k);
            public override int GetHashCode() => albedo.GetHashCode() ^ (nrm.GetHashCode() * 397) ^ (nrmStr * 1009);
        }
        Mesh _quad;

        struct Slot
        {
            public GameObject go;
            public Transform t;
            public DecalProjector decal;
            public MeshRenderer mr;
            public MaterialPropertyBlock block;
            public float born;
            public float life;
            public float startFade;
            public bool used;
        }

        public FootprintPool(Transform owner, int max)
        {
            _max = Mathf.Clamp(max, 8, 128);
            _slots = new Slot[_max];
            _decalShader = Shader.Find("Shader Graphs/Decal");
            _decalMode = _decalShader != null;
            if (!_decalMode)
            {
                var quadShader = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Sprites/Default");
                if (quadShader != null)
                {
                    _quadShared = new Material(quadShader) { name = "XA_FootprintQuad" };
                    SetupTransparent(_quadShared);
                }
            }
        }

        public void Stamp(Vector3 point, Vector3 normal, Vector3 forward, bool left,
            FootstepSurfaceMap.Surface surface, FootstepSurfaceMap map)
        {
            if (surface == null || !surface.leavePrints || surface.printSize <= 0.01f || surface.printOpacity <= 0.01f)
                return;

            var albedo = left
                ? (map != null && map.printAlbedoLeft ? map.printAlbedoLeft : map != null ? map.printAlbedoRight : null)
                : (map != null && map.printAlbedoRight ? map.printAlbedoRight : map != null ? map.printAlbedoLeft : null);
            var nrm = left
                ? (map != null && map.printNormalLeft ? map.printNormalLeft : map != null ? map.printNormalRight : null)
                : (map != null && map.printNormalRight ? map.printNormalRight : map != null ? map.printNormalLeft : null);
            if (albedo == null) return;

            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            else normal.Normalize();
            var up = Vector3.ProjectOnPlane(forward, normal);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Vector3.right, normal);
            up.Normalize();

            int i = _next;
            _next = (_next + 1) % _max;
            if (_slots[i].go == null) BuildSlot(i);
            var s = _slots[i];
            s.used = true;
            s.born = Time.time;
            s.life = Mathf.Max(0.5f, surface.printLifetime);
            s.startFade = Mathf.Clamp01(surface.printOpacity);

            float length = Mathf.Clamp(surface.printSize, 0.05f, 1.2f);
            float width = length * 0.46f;
            var rot = Quaternion.LookRotation(-normal, up);
            s.t.SetPositionAndRotation(point + normal * (ProjectDepth * 0.45f), rot);
            s.go.SetActive(true);

            if (_decalMode && s.decal != null)
            {
                s.decal.material = DecalMaterial(albedo, nrm, surface);
                s.decal.size = new Vector3(width, length, ProjectDepth);
                s.decal.pivot = new Vector3(0f, 0f, 0.5f);
                s.decal.fadeFactor = s.startFade;
                s.decal.uvScale = Vector2.one;
                s.decal.drawDistance = 80f;
            }
            else if (s.mr != null)
            {
                s.t.position = point + normal * 0.015f;
                s.t.rotation = Quaternion.LookRotation(up, normal);
                s.t.localScale = new Vector3(width, 1f, length);
                if (s.block == null) s.block = new MaterialPropertyBlock();
                s.block.SetTexture("_BaseMap", albedo);
                s.block.SetColor("_BaseColor", new Color(1f, 1f, 1f, s.startFade));
                s.mr.SetPropertyBlock(s.block);
            }

            _slots[i] = s;
        }

        public void Tick()
        {
            float now = Time.time;
            for (int i = 0; i < _max; i++)
            {
                if (!_slots[i].used || _slots[i].go == null) continue;
                float age = now - _slots[i].born;
                float life = _slots[i].life;
                if (age >= life)
                {
                    _slots[i].used = false;
                    _slots[i].go.SetActive(false);
                    continue;
                }
                float fadeStart = life * 0.7f;
                float fade = age < fadeStart
                    ? _slots[i].startFade
                    : _slots[i].startFade * (1f - (age - fadeStart) / (life - fadeStart));
                if (_decalMode && _slots[i].decal != null)
                    _slots[i].decal.fadeFactor = fade;
                else if (_slots[i].mr != null && _slots[i].block != null)
                {
                    _slots[i].block.SetColor("_BaseColor", new Color(1f, 1f, 1f, fade));
                    _slots[i].mr.SetPropertyBlock(_slots[i].block);
                }
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].go != null) Object.Destroy(_slots[i].go);
            foreach (var m in _decalMats.Values)
                if (m != null) Object.Destroy(m);
            _decalMats.Clear();
            if (_quadShared != null) Object.Destroy(_quadShared);
        }

        void BuildSlot(int i)
        {
            var go = new GameObject("XA_Footprint_" + i);
            go.transform.SetParent(null, true);
            Object.DontDestroyOnLoad(go);
            var slot = new Slot { go = go, t = go.transform, used = false };

            if (_decalMode)
            {
                var dp = go.AddComponent<DecalProjector>();
                dp.drawDistance = 80f;
                dp.fadeScale = 0.9f;
                slot.decal = dp;
            }
            else
            {
                if (_quad == null) _quad = BuildQuad();
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _quad;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _quadShared;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                slot.mr = mr;
                slot.block = new MaterialPropertyBlock();
            }

            go.SetActive(false);
            _slots[i] = slot;
        }

        Material DecalMaterial(Texture2D albedo, Texture2D nrm, FootstepSurfaceMap.Surface surface)
        {
            var key = new DecalKey
            {
                albedo = albedo != null ? albedo.GetEntityId() : EntityId.None,
                nrm = nrm != null ? nrm.GetEntityId() : EntityId.None,
                nrmStr = Mathf.RoundToInt(surface.printNormalStrength * 20f)
            };
            if (_decalMats.TryGetValue(key, out var mat) && mat != null) return mat;
            mat = new Material(_decalShader) { name = "XA_FootprintDecal_" + surface.id };
            if (albedo != null) mat.SetTexture("Base_Map", albedo);
            if (nrm != null) mat.SetTexture("Normal_Map", nrm);
            mat.SetFloat("Normal_Blend", Mathf.Clamp01(surface.printNormalStrength / 2.5f));
            _decalMats[key] = mat;
            return mat;
        }

        static void SetupTransparent(Material m)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 2f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "XA_FootprintQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f)
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds();
            return m;
        }
    }
}
