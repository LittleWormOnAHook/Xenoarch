using System.Collections.Generic;
using DMI.Xenoarch.Player;
using UnityEngine;

namespace DMI.Xenoarch.Footsteps
{
    /// <summary>
    /// Kade footsteps: detects foot plants from the Humanoid feet (works with no animation events,
    /// foot IK, or root motion), falls back to a stride timer, resolves the dominant terrain layer
    /// or mesh (Digger / ship / rover), plays pooled 3D audio, and stamps a depth-scaled print.
    /// Detection and alphamap sampling follow ASH.Player.ASHFootsteps and Genesis DMFootstepManager;
    /// rewritten for Xenoarch (URP decals, planetary layer map, no Invector).
    /// Low-gravity bodies still play steps — triggering uses planar speed and sole contact, not gravity.
    /// Animation events can also fire OnFootstep / OnFootstepLeft / OnFootstepRight if clips gain them.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFootsteps : MonoBehaviour
    {
        public FootstepSurfaceMap map;

        [Header("Detection")]
        [Tooltip("Sole height above ground (m) that counts as planted.")]
        [Range(0.01f, 0.25f)] public float plantHeight = 0.08f;
        [Tooltip("Sole must rise above this (m) before the next plant on that foot.")]
        [Range(0.02f, 0.5f)] public float liftHeight = 0.12f;
        [Tooltip("Minimum horizontal speed (m/s) for walking steps. Landing still plays at 0.")]
        [Range(0f, 2f)] public float minSpeed = 0.28f;
        [Range(0.1f, 1.2f)] public float minInterval = 0.26f;
        [Tooltip("Speed treated as a full run (m/s) for volume.")]
        [Range(1f, 12f)] public float runSpeed = 5.8f;
        [Tooltip("Stride length used when foot bones are missing (walk / run).")]
        public Vector2 strideDistance = new Vector2(1.15f, 1.85f);
        public LayerMask groundLayers = ~0;

        [Header("Audio")]
        [Range(0f, 2f)] public float masterVolume = 0.9f;
        [Range(1, 8)] public int voices = 4;
        [Range(1f, 80f)] public float maxDistance = 24f;
        [Range(0f, 1f)] public float walkVolume = 0.62f;

        [Header("Debug")]
        [Tooltip("Logs [FootstepDiag] when the resolved surface changes.")]
        public bool logSurfaceChanges = true;

        public string LastSurface { get; private set; } = "";
        public int StepCount { get; private set; }

        FirstPersonController _fpc;
        Animator _anim;
        Transform _lf, _rf;
        float _lb, _rb;
        bool _lUp = true, _rUp = true;
        float _lt, _rt;
        bool _wasGrounded = true;
        float _airTime;
        float _strideAcc;
        bool _useBones;
        readonly List<AudioSource> _src = new List<AudioSource>(4);
        int _nextVoice;
        readonly Dictionary<string, int> _lastClip = new Dictionary<string, int>(16);
        FootprintPool _prints;
        float[,,] _splat = new float[1, 1, 1];
        Terrain _cachedTerrain;
        int _cachedMapX = int.MinValue, _cachedMapZ = int.MinValue;
        string _cachedLayerName = "";

        public void OnFootstep() => StepFromController(true);
        public void OnFootstepLeft() => Plant(_lf, _lb, true);
        public void OnFootstepRight() => Plant(_rf, _rb, false);

        void Awake()
        {
            if (map == null) map = FootstepSurfaceMap.Live;
            else map.EnsureDefaults();
            _fpc = GetComponent<FirstPersonController>() ?? GetComponentInParent<FirstPersonController>();
            _anim = GetComponentInChildren<Animator>();
            if (_anim != null && _anim.isHuman)
            {
                _lf = _anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                _rf = _anim.GetBoneTransform(HumanBodyBones.RightFoot);
                _lb = Mathf.Max(0.02f, _anim.leftFeetBottomHeight);
                _rb = Mathf.Max(0.02f, _anim.rightFeetBottomHeight);
                _useBones = _lf != null && _rf != null;
            }
            int n = Mathf.Clamp(voices, 1, 8);
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("XA_FootstepVoice" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 1.4f;
                s.maxDistance = maxDistance;
                s.dopplerLevel = 0f;
                _src.Add(s);
            }
            _prints = new FootprintPool(transform, map != null ? map.maxPrints : 64);
        }

        void OnDestroy()
        {
            _prints?.Dispose();
        }

        void LateUpdate()
        {
            _prints?.Tick();
            if (_fpc != null && _fpc.Seated) return;
            if (!isActiveAndEnabled) return;

            bool grounded = _fpc == null || _fpc.IsGrounded;
            float speed = _fpc != null ? _fpc.PlanarSpeed : 0f;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            if (!grounded)
            {
                _airTime += dt;
                _wasGrounded = false;
                _strideAcc = 0f;
                if (_useBones)
                {
                    _lUp = true;
                    _rUp = true;
                }
                return;
            }

            if (!_wasGrounded && _airTime > 0.12f)
                StepFromController(true);
            _airTime = 0f;
            _wasGrounded = true;

            if (_useBones)
            {
                Foot(_lf, _lb, ref _lUp, ref _lt, true, speed);
                Foot(_rf, _rb, ref _rUp, ref _rt, false, speed);
            }
            else if (speed >= minSpeed)
            {
                _strideAcc += speed * dt;
                float stride = Mathf.Lerp(strideDistance.x, strideDistance.y, Mathf.Clamp01(speed / Mathf.Max(0.01f, runSpeed)));
                if (_strideAcc >= stride && Time.time - Mathf.Max(_lt, _rt) >= minInterval)
                {
                    _strideAcc = 0f;
                    bool left = StepCount % 2 == 0;
                    if (left) _lt = Time.time; else _rt = Time.time;
                    StepFromController(left);
                }
            }
            else _strideAcc = 0f;
        }

        void Foot(Transform f, float bottom, ref bool up, ref float lastT, bool left, float speed)
        {
            if (f == null) return;
            var sole = f.position - Vector3.up * bottom;
            if (!Ground(sole, out var hit)) return;
            float h = sole.y - hit.point.y;
            if (h > liftHeight) { up = true; return; }
            if (up && h < plantHeight && speed >= minSpeed && Time.time - lastT >= minInterval)
            {
                up = false;
                lastT = Time.time;
                Step(hit.point, hit.normal, hit, left, speed);
            }
        }

        void Plant(Transform f, float bottom, bool left)
        {
            var pos = f != null ? f.position - Vector3.up * bottom : transform.position;
            float speed = _fpc != null ? _fpc.PlanarSpeed : 0f;
            if (!Ground(pos, out var hit)) return;
            Step(hit.point, hit.normal, hit, left, speed);
        }

        void StepFromController(bool left)
        {
            var origin = transform.position + Vector3.up * 0.2f;
            float speed = _fpc != null ? _fpc.PlanarSpeed : 0f;
            if (!Ground(origin, out var hit)) return;
            Step(hit.point, hit.normal, hit, left, speed);
        }

        bool Ground(Vector3 origin, out RaycastHit hit)
        {
            var start = origin + Vector3.up * 0.6f;
            if (Physics.Raycast(start, Vector3.down, out hit, 2.0f, groundLayers, QueryTriggerInteraction.Ignore))
                return true;
            hit = default;
            return false;
        }

        void Step(Vector3 point, Vector3 normal, RaycastHit hit, bool left, float speed)
        {
            var surf = Resolve(point, hit, out string id);
            LastSurface = id;
            StepCount++;
            float s01 = Mathf.Clamp01(speed / Mathf.Max(0.01f, runSpeed));

            if (surf != null && surf.clips != null && surf.clips.Length > 0)
            {
                float vol = masterVolume * surf.volume * Mathf.Lerp(walkVolume, 1f, s01) * (1f + surf.softVolumeBoost * (1f - s01) * 0.5f);
                float pitch = Random.Range(surf.pitch.x, surf.pitch.y) - surf.softPitchDrop * (1f - s01) * 0.5f;
                Play(Pick(surf.id, surf.clips), point, vol, pitch);
            }

            if (surf != null && surf.leavePrints && _prints != null)
            {
                var fwd = _fpc != null && _fpc.PlanarVelocity.sqrMagnitude > 0.05f
                    ? _fpc.PlanarVelocity
                    : transform.forward;
                _prints.Stamp(point, normal, fwd, left, surf, map);
            }
        }

        AudioClip Pick(string id, AudioClip[] clips)
        {
            if (clips.Length == 1) return clips[0];
            _lastClip.TryGetValue(id, out int last);
            int i = Random.Range(0, clips.Length - 1);
            if (i >= last) i++;
            _lastClip[id] = i;
            return clips[i];
        }

        void Play(AudioClip clip, Vector3 pos, float vol, float pitch)
        {
            if (!clip || _src.Count == 0) return;
            var s = _src[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _src.Count;
            s.transform.position = pos;
            s.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            s.maxDistance = maxDistance;
            s.PlayOneShot(clip, Mathf.Clamp01(vol));
        }

        FootstepSurfaceMap.Surface Resolve(Vector3 pos, RaycastHit hit, out string id)
        {
            id = "none";
            if (map == null) return null;

            var col = hit.collider;
            Terrain terrain = null;
            if (col != null)
            {
                var tc = col as TerrainCollider;
                if (tc != null) terrain = tc.GetComponent<Terrain>();
                if (terrain == null) terrain = col.GetComponentInParent<Terrain>();
            }

            bool diggerLike = col != null && IsDiggerMesh(col);
            if (terrain == null && (diggerLike || col == null))
                terrain = TerrainAt(pos);

            if (terrain != null && terrain.terrainData != null && (col is TerrainCollider || diggerLike || col == null))
            {
                string layerName = DominantLayerName(terrain, pos);
                string sid = FootstepSurfaceMap.Match(map.terrainLayerRules, layerName);
                var surf = map.Get(sid ?? map.terrainDefault);
                id = surf != null ? surf.id : (sid ?? map.terrainDefault);
                LogIfChanged(id, layerName, col);
                return surf;
            }

            if (col != null)
            {
                string sid = null;
                if (col.sharedMaterial) sid = FootstepSurfaceMap.Match(map.physicMaterialRules, col.sharedMaterial.name);
                if (sid == null) sid = FootstepSurfaceMap.Match(map.tagRules, col.tag, true);
                if (sid == null && map.layerRules != null)
                {
                    int bit = 1 << col.gameObject.layer;
                    for (int i = 0; i < map.layerRules.Count; i++)
                        if ((map.layerRules[i].layers.value & bit) != 0) { sid = map.layerRules[i].surface; break; }
                }
                if (sid == null)
                {
                    for (var tr = col.transform; tr != null && sid == null; tr = tr.parent)
                        sid = FootstepSurfaceMap.Match(map.objectNameRules, tr.name);
                    if (sid == null)
                    {
                        var r = col.GetComponentInParent<Renderer>();
                        if (r != null && r.sharedMaterial != null)
                            sid = FootstepSurfaceMap.Match(map.objectNameRules, r.sharedMaterial.name);
                    }
                }

                if (sid == null)
                {
                    var nearby = TerrainAt(pos);
                    if (nearby != null && nearby.terrainData != null)
                    {
                        string layerName = DominantLayerName(nearby, pos);
                        sid = FootstepSurfaceMap.Match(map.terrainLayerRules, layerName) ?? map.terrainDefault;
                    }
                }

                var surf = map.Get(sid ?? map.meshDefault);
                id = surf != null ? surf.id : (sid ?? map.meshDefault);
                LogIfChanged(id, col != null ? col.name : "", col);
                return surf;
            }

            var fallback = map.Get(map.meshDefault);
            id = fallback != null ? fallback.id : map.meshDefault;
            LogIfChanged(id, "", null);
            return fallback;
        }

        static bool IsDiggerMesh(Collider col)
        {
            for (var t = col.transform; t != null; t = t.parent)
            {
                var n = t.name;
                if (n.IndexOf("digger", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("voxel", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("chunk", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            var mat = col.sharedMaterial;
            if (mat != null)
            {
                var n = mat.name;
                if (n.IndexOf("digger", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("soil", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        static Terrain TerrainAt(Vector3 pos)
        {
            var active = Terrain.activeTerrains;
            Terrain best = null;
            float bestD = float.MaxValue;
            if (active != null)
            {
                for (int i = 0; i < active.Length; i++)
                {
                    var t = active[i];
                    if (t == null || t.terrainData == null) continue;
                    var o = t.GetPosition();
                    var size = t.terrainData.size;
                    if (pos.x < o.x || pos.z < o.z || pos.x > o.x + size.x || pos.z > o.z + size.z)
                    {
                        float dx = 0f, dz = 0f;
                        if (pos.x < o.x) dx = o.x - pos.x;
                        else if (pos.x > o.x + size.x) dx = pos.x - (o.x + size.x);
                        if (pos.z < o.z) dz = o.z - pos.z;
                        else if (pos.z > o.z + size.z) dz = pos.z - (o.z + size.z);
                        float d = dx * dx + dz * dz;
                        if (d < bestD) { bestD = d; best = t; }
                        continue;
                    }
                    return t;
                }
            }
            if (best != null && bestD < 16f) return best;
            return Terrain.activeTerrain;
        }

        string DominantLayerName(Terrain t, Vector3 pos)
        {
            var td = t.terrainData;
            var origin = t.GetPosition();
            int mapX = Mathf.Clamp(
                (int)(((pos.x - origin.x) / td.size.x) * td.alphamapWidth),
                0, Mathf.Max(0, td.alphamapWidth - 1));
            int mapZ = Mathf.Clamp(
                (int)(((pos.z - origin.z) / td.size.z) * td.alphamapHeight),
                0, Mathf.Max(0, td.alphamapHeight - 1));

            if (t == _cachedTerrain && mapX == _cachedMapX && mapZ == _cachedMapZ)
                return _cachedLayerName;

            _splat = td.GetAlphamaps(mapX, mapZ, 1, 1);
            int best = 0;
            float bv = -1f;
            int count = _splat.GetLength(2);
            for (int i = 0; i < count; i++)
                if (_splat[0, 0, i] > bv) { bv = _splat[0, 0, i]; best = i; }

            var layers = td.terrainLayers;
            string name = "";
            if (layers != null && best < layers.Length && layers[best] != null)
            {
                name = layers[best].name;
                if (layers[best].diffuseTexture != null)
                    name += " " + layers[best].diffuseTexture.name;
            }

            _cachedTerrain = t;
            _cachedMapX = mapX;
            _cachedMapZ = mapZ;
            _cachedLayerName = name;
            return name;
        }

        void LogIfChanged(string surface, string detail, Collider col)
        {
            if (!logSurfaceChanges) return;
            if (string.Equals(surface, LastSurface, System.StringComparison.Ordinal)) return;
            string extra = string.IsNullOrEmpty(detail) ? "" : " source=" + detail;
            string colName = col != null ? " collider=" + col.name : "";
            Debug.Log($"[FootstepDiag] surface={surface}{extra}{colName}");
        }
    }
}
