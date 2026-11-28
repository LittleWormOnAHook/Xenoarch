using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Weather;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>
    /// Dust puffs (one ParticleSystem) and a small pool of physics rock chunks thrown from the dig spot, tinted by the soil
    /// layer being cut. Dust drift and chunk flight use the planet's gravity, so digging on a low-g moon looks floaty.
    /// Chunks sit on the Ignore Raycast layer and never collide with the player rig.
    /// </summary>
    public sealed class SonicDebris : MonoBehaviour
    {
        const int PoolSize = 24;
        sealed class Chunk
        {
            public Transform t; public Rigidbody rb; public Renderer r; public Collider col;
            public float until, life, unit = 1f;
        }

        static SonicDebris _i;
        static Transform _player;
        ParticleSystem _dust;
        readonly Chunk[] _pool = new Chunk[PoolSize];
        int _next;
        float _g = 9.81f, _gAt = -10f;
        MaterialPropertyBlock _mpb;
        EnvironmentDirector _env;
        Collider[] _playerCols;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _i = null; _player = null; }

        public static void Bind(Transform playerRoot)
        {
            _player = playerRoot;
            if (_i != null) _i._playerCols = null;
        }

        static SonicDebris Instance
        {
            get
            {
                if (_i != null) return _i;
                var go = new GameObject("Sonic Debris");
                _i = go.AddComponent<SonicDebris>();
                _i.Build();
                return _i;
            }
        }

        void Build()
        {
            _mpb = new MaterialPropertyBlock();
            var d = new GameObject("Dust");
            d.transform.SetParent(transform, false);
            _dust = d.AddComponent<ParticleSystem>();
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _dust.main;
            main.playOnAwake = false; main.loop = false; main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.9f; main.startSpeed = 0f; main.startSize = 0.1f;
            main.gravityModifier = 0.12f;
            var em = _dust.emission; em.enabled = false;
            var sh = _dust.shape; sh.enabled = false;
            var vol = _dust.velocityOverLifetime; vol.enabled = false;
            var lim = _dust.limitVelocityOverLifetime; lim.enabled = true; lim.dampen = 0.08f; lim.limit = 0.6f;
            var col = _dust.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.12f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = _dust.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.9f));
            var rend = d.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = SonicFxAssets.DustMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
            rend.sortingFudge = 2f;

            for (int i = 0; i < PoolSize; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Rock chunk"; go.layer = 2;
                go.transform.SetParent(transform, false);
                var c = new Chunk { t = go.transform, rb = go.AddComponent<Rigidbody>(), r = go.GetComponent<Renderer>(), col = go.GetComponent<Collider>() };
                var rocks = SonicFxAssets.Rocks;
                if (rocks.Length > 0)
                {
                    // real rock scans (Resources/World/Rocks), one random variant per pool slot
                    var rk = rocks[Random.Range(0, rocks.Length)];
                    go.GetComponent<MeshFilter>().sharedMesh = rk.mesh;
                    var bc = go.GetComponent<BoxCollider>(); if (bc != null) { bc.center = rk.center; bc.size = rk.size; }
                    c.r.sharedMaterial = rk.mat; c.unit = rk.unit;
                }
                else c.r.sharedMaterial = SonicFxAssets.ChunkMaterial;
                c.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.rb.mass = 0.05f; c.rb.useGravity = false; c.rb.linearDamping = 0.05f; c.rb.angularDamping = 0.2f;
                go.SetActive(false);
                _pool[i] = c;
            }
        }

        float Gravity()
        {
            if (Time.time - _gAt < 1f) return _g;
            _gAt = Time.time;
            if (_env == null) _env = FindAnyObjectByType<EnvironmentDirector>();
            if (_env != null && _env.Current != null && _env.Current.gravity > 0.05f) _g = _env.Current.gravity;
            return _g;
        }

        /// <summary>Throw dust and chunks from a dig point. radius scales the cloud and chunk size.</summary>
        public static void Burst(Vector3 point, Vector3 normal, Color tint, float radius, int dust, int chunks)
        {
            var d = Instance;
            float g = d.Gravity();
            d.EmitDust(point, normal, tint, radius, dust, g);
            for (int i = 0; i < chunks; i++) d.Throw(point, normal, tint, radius, g);
        }

        void EmitDust(Vector3 p, Vector3 n, Color tint, float radius, int count, float g)
        {
            if (count <= 0) return;
            var main = _dust.main;
            main.gravityModifier = Mathf.Clamp(g / 9.81f, 0.05f, 2f) * 0.12f;
            float lift = Mathf.Clamp(Mathf.Sqrt(9.81f / Mathf.Max(1f, g)), 0.7f, 2.2f);   // low gravity: the puff rises higher and hangs longer
            var ep = new ParticleSystem.EmitParams();
            var c = Color.Lerp(tint, new Color(0.85f, 0.8f, 0.72f), 0.35f); c.a = 0.55f;
            for (int i = 0; i < count; i++)
            {
                var disc = Random.insideUnitCircle * radius * 0.8f;
                var t1 = Vector3.Cross(n, Vector3.right); if (t1.sqrMagnitude < 0.01f) t1 = Vector3.Cross(n, Vector3.forward);
                t1.Normalize(); var t2 = Vector3.Cross(n, t1);
                ep.position = p + n * 0.05f + t1 * disc.x + t2 * disc.y;
                ep.velocity = (n * Random.Range(0.35f, 1.1f) * lift + (t1 * Random.Range(-1f, 1f) + t2 * Random.Range(-1f, 1f)) * 0.7f);
                ep.startLifetime = Random.Range(0.6f, 1.1f) * Mathf.Clamp(lift, 1f, 1.6f);
                ep.startSize = Random.Range(0.06f, 0.14f) * (0.7f + radius);
                ep.startColor = c;
                _dust.Emit(ep, 1);
            }
        }

        void Throw(Vector3 p, Vector3 n, Color tint, float radius, float g)
        {
            var c = _pool[_next]; _next = (_next + 1) % PoolSize;
            if (_player != null && _playerCols == null) _playerCols = _player.GetComponentsInChildren<Collider>(true);
            var go = c.t.gameObject;
            go.SetActive(true);
            // 3-12 cm rocks (longest side), random per-axis scale
            float s = Mathf.Clamp(Random.Range(0.03f, 0.12f) * Mathf.Lerp(0.85f, 1.1f, Mathf.Clamp01(radius)), 0.03f, 0.12f) * c.unit;
            c.t.localScale = new Vector3(s * Random.Range(0.8f, 1.25f), s * Random.Range(0.7f, 1.15f), s * Random.Range(0.8f, 1.25f));
            var rnd = Random.insideUnitSphere; rnd.y = Mathf.Abs(rnd.y);
            c.t.SetPositionAndRotation(p + n * 0.08f + rnd * radius * 0.4f, Random.rotation);
            if (_playerCols != null) foreach (var pc in _playerCols) if (pc != null && c.col != null) Physics.IgnoreCollision(c.col, pc, true);
            var t1 = Vector3.Cross(n, Vector3.right); if (t1.sqrMagnitude < 0.01f) t1 = Vector3.Cross(n, Vector3.forward);
            var dir = (n * Random.Range(0.8f, 1.4f) + t1.normalized * Random.Range(-1f, 1f) + Vector3.Cross(n, t1.normalized) * Random.Range(-1f, 1f)).normalized;
            c.rb.linearVelocity = dir * Random.Range(1.6f, 3.4f);
            c.rb.angularVelocity = Random.insideUnitSphere * 8f;
            c.life = Random.Range(1.4f, 2.2f); c.until = Time.time + c.life;
            var col = Color.Lerp(Color.white, Color.Lerp(tint, Color.black, Random.Range(0.05f, 0.35f)), 0.25f); col.a = 1f;   // keep the rock texture, only a hint of the soil colour
            _mpb.SetColor("_BaseColor", col); _mpb.SetColor("_Color", col);
            c.r.SetPropertyBlock(_mpb);
        }

        void FixedUpdate()
        {
            float g = Gravity();
            var acc = Vector3.down * g;
            for (int i = 0; i < PoolSize; i++)
            {
                var c = _pool[i];
                if (c.t.gameObject.activeSelf) c.rb.AddForce(acc, ForceMode.Acceleration);
            }
        }

        void Update()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                var c = _pool[i];
                if (!c.t.gameObject.activeSelf) continue;
                float left = c.until - Time.time;
                if (left <= 0f) c.t.gameObject.SetActive(false);
                else if (left < 0.4f) c.t.localScale *= 1f - Time.deltaTime * 3f;   // shrink out
            }
        }
    }
}
