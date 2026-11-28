using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Weather
{
    /// <summary>
    /// The pooled particle volumes of the weather: one long-lived ParticleSystem per kind (rain, snow, ash, embers,
    /// blowing dust, regolith motes) that follows the camera, plus a few pooled dust devils. Nothing is created or
    /// destroyed while the weather changes: only emission rates, drift and colours are rewritten (throttled), and
    /// everything is capped by a particle budget. Falling particles use the planet's gravity with a drag-limited
    /// terminal speed; airless motes are pure ballistic arcs.
    /// </summary>
    public sealed class WeatherParticles : MonoBehaviour
    {
        public enum Kind { Rain, Snow, Ash, Embers, Dust, Motes }

        sealed class Rig
        {
            public Kind kind;
            public ParticleSystem ps;
            public ParticleSystemRenderer pr;
            public int budget;
            public float lifetime = 5f, lastRate = -1f, lastFall = -1f;
        }

        const float Radius = 22f, Above = 10f, Below = 4f;

        readonly Dictionary<Kind, Rig> _rigs = new Dictionary<Kind, Rig>();
        readonly List<DustDevil> _devils = new List<DustDevil>();
        Transform _follow;
        WeatherCatalog _cat;
        float _nextDrive;

        public Transform Follow { get { return _follow; } set { _follow = value; } }

        public static WeatherParticles Create(Transform parent)
        {
            var go = new GameObject("[Xenoarch] Weather Particles");
            go.transform.SetParent(parent, false);
            var wp = go.AddComponent<WeatherParticles>();
            wp._cat = WeatherCatalog.Load();
            return wp;
        }

        public void SetCatalog(WeatherCatalog c) { _cat = c; }

        Rig Get(Kind k)
        {
            if (_rigs.TryGetValue(k, out var r)) return r;
            r = Build(k);
            _rigs[k] = r;
            return r;
        }

        Rig Build(Kind k)
        {
            var go = new GameObject("Weather_" + k);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            var rig = new Rig { kind = k, ps = ps, pr = pr };
            float scale = WeatherSettings.ParticleScale;

            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.prewarm = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f; main.gravityModifier = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.rotation = Vector3.zero;
            float h = Above + Below;
            sh.scale = new Vector3(Radius * 2f, h, Radius * 2f); sh.position = new Vector3(0f, Above - h * 0.5f, 0f);

            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pr.receiveShadows = false;
            pr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; pr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            pr.alignment = ParticleSystemRenderSpace.View;
            pr.sortMode = ParticleSystemSortMode.None;

            switch (k)
            {
                case Kind.Rain:
                    rig.budget = (int)(8000 * scale);
                    pr.sharedMaterial = WeatherVisuals.StreakMat(_cat);
                    pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.035f; pr.lengthScale = 2.2f; pr.cameraVelocityScale = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
                    pr.minParticleSize = 0.0004f;
                    Collide(ps);
                    break;
                case Kind.Snow:
                    rig.budget = (int)(14000 * scale);
                    pr.sharedMaterial = WeatherVisuals.Alpha(_cat); pr.renderMode = ParticleSystemRenderMode.Billboard;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.07f);
                    pr.minParticleSize = 0.0007f;
                    Flutter(ps, 0.9f, 0.35f);
                    Collide(ps);
                    break;
                case Kind.Ash:
                    rig.budget = (int)(14000 * scale);
                    pr.sharedMaterial = WeatherVisuals.Alpha(_cat); pr.renderMode = ParticleSystemRenderMode.Billboard;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.075f);
                    pr.minParticleSize = 0.0007f;
                    Flutter(ps, 0.7f, 0.3f);
                    Collide(ps);
                    break;
                case Kind.Embers:
                    rig.budget = (int)(900 * scale);
                    pr.sharedMaterial = WeatherVisuals.Additive(_cat); pr.renderMode = ParticleSystemRenderMode.Billboard;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                    main.startColor = new Color(1f, 0.5f, 0.15f, 1f);
                    pr.minParticleSize = 0.0007f;
                    Flutter(ps, 1.2f, 0.4f);
                    break;
                case Kind.Dust:
                    rig.budget = (int)(5000 * scale);
                    pr.sharedMaterial = WeatherVisuals.Alpha(_cat);
                    pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.06f; pr.lengthScale = 1.2f; pr.cameraVelocityScale = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.35f);
                    pr.minParticleSize = 0.0006f;
                    Flutter(ps, 2.2f, 0.6f);
                    sh.scale = new Vector3(Radius * 2f, 9f, Radius * 2f); sh.position = new Vector3(0f, 4f - 1f, 0f);
                    break;
                case Kind.Motes:
                    rig.budget = (int)(700 * scale);
                    pr.sharedMaterial = WeatherVisuals.Additive(_cat); pr.renderMode = ParticleSystemRenderMode.Billboard;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 4.5f);
                    main.startColor = new Color(1f, 0.97f, 0.9f, 1f);
                    pr.minParticleSize = 0.0005f;
                    sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 14f; sh.scale = Vector3.one; sh.position = new Vector3(0f, -1.6f, 0f);
                    sh.rotation = new Vector3(-90f, 0f, 0f); sh.radiusThickness = 1f;
                    Collide(ps);
                    break;
            }
            main.maxParticles = Mathf.Max(50, rig.budget);

            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            return rig;
        }

        static void Flutter(ParticleSystem ps, float strength, float freq)
        {
            var n = ps.noise; n.enabled = true; n.strength = strength; n.frequency = freq; n.scrollSpeed = 0.25f;
            n.damping = true; n.quality = ParticleSystemNoiseQuality.Low;
        }

        static void Collide(ParticleSystem ps)
        {
            var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
            c.quality = ParticleSystemCollisionQuality.Low; c.dampen = 0f; c.bounce = 0f; c.lifetimeLoss = 1f; c.radiusScale = 0.2f;
            c.maxCollisionShapes = 256; c.enableDynamicColliders = false; c.sendCollisionMessages = false; c.collidesWith = ~0;
        }

        // ---------------------------------------------------------------- per-frame

        /// <summary>Everything the volumes need, already scaled by intensity. Called each frame; only rewrites particle
        /// modules every 0.2 s.</summary>
        public struct Drive
        {
            public float rain, snow, ash, dust, motes, devils, embers;   // amounts (0-1.5)
            public Vector3 wind;                                         // m/s
            public float gravity;                                        // m/s^2
            public float density01;                                      // atmosphere density, 0-1
            public float size;                                           // flake size multiplier
            public Color rainColor, snowColor, ashColor, dustColor;
            public bool active;
            public bool extreme;
        }

        public void Tick(Drive d, Transform cam)
        {
            if (cam != null) transform.position = cam.position;
            transform.rotation = Quaternion.identity;
            if (Time.unscaledTime < _nextDrive) { TickDevils(d, cam); return; }
            _nextDrive = Time.unscaledTime + 0.2f;
            float gMod = Mathf.Max(0.02f, d.gravity / Mathf.Max(0.1f, Mathf.Abs(Physics.gravity.y)));
            float gRatio = Mathf.Max(0.02f, d.gravity / 9.81f);
            float thin = Mathf.Lerp(1.6f, 1f, Mathf.Clamp01(d.density01 * 2f)); // thinner air: less drag, faster fall
            float budget = d.extreme ? 1.8f : 1f;

            Push(Kind.Rain, d.active ? d.rain : 0f, d, gMod, Mathf.Clamp(8.5f * Mathf.Sqrt(gRatio) * thin, 1.5f, 14f), 2800f, d.rainColor, 1f, 0.7f, budget);
            Push(Kind.Snow, d.active ? d.snow : 0f, d, gMod, Mathf.Clamp(1.1f * Mathf.Sqrt(gRatio) * thin, 0.25f, 3f), 1500f, d.snowColor, 1f, 1f, budget);
            Push(Kind.Ash, d.active ? d.ash : 0f, d, gMod, Mathf.Clamp(0.8f * Mathf.Sqrt(gRatio) * thin, 0.2f, 2.5f), 1500f, d.ashColor, 1f, 1f, budget);
            Push(Kind.Embers, d.active ? d.embers * d.ash : 0f, d, gMod, Mathf.Clamp(0.6f * Mathf.Sqrt(gRatio), 0.15f, 2f), 90f, new Color(1f, 0.5f, 0.15f, 1f), 1f, 1f, budget);
            PushDust(d, budget);
            PushMotes(d, gMod, budget);
            TickDevils(d, cam);
        }

        void Push(Kind k, float amount, Drive d, float gMod, float terminal, float maxRate, Color color, float lifeScale, float windScale, float budgetMul)
        {
            if (amount < 0.003f && !_rigs.ContainsKey(k)) return;
            var r = Get(k);
            var ps = r.ps; var main = ps.main; var em = ps.emission;
            if (amount < 0.003f)
            {
                if (r.lastRate != 0f) { em.rateOverTime = 0f; r.lastRate = 0f; }
                if (ps.isPlaying && ps.particleCount == 0) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            float h = Above + Below;
            float life = Mathf.Clamp(h / Mathf.Max(0.1f, terminal) * 1.15f, 0.8f, 24f);
            float rate = Mathf.Min(maxRate * amount * WeatherSettings.ParticleScale, r.budget * budgetMul / life);
            // Fall speed = this world's drag-limited terminal speed (sqrt of gravity, thinner air = faster); the
            // wind adds sideways drift. (Gravity itself is only used for airless ballistic motes.)
            main.startLifetime = life; main.gravityModifier = 0f;
            em.rateOverTime = rate; r.lastRate = rate; r.lastFall = terminal; r.lifetime = life;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(d.wind.x * 0.7f * windScale, d.wind.x * 1.3f * windScale);
            vel.z = new ParticleSystem.MinMaxCurve(d.wind.z * 0.7f * windScale, d.wind.z * 1.3f * windScale);
            vel.y = new ParticleSystem.MinMaxCurve(-terminal * 1.15f, -terminal * 0.85f);            var sz = Mathf.Max(0.3f, d.size);
            if (k == Kind.Rain) main.startSize = new ParticleSystem.MinMaxCurve(0.035f * sz, 0.06f * sz);
            else if (k == Kind.Snow) main.startSize = new ParticleSystem.MinMaxCurve(0.02f * sz, 0.07f * sz);
            else if (k == Kind.Ash) main.startSize = new ParticleSystem.MinMaxCurve(0.02f * sz, 0.075f * sz);
            if (k != Kind.Embers) main.startColor = color;
            if (!ps.isPlaying) ps.Play(true);
        }

        void PushDust(Drive d, float budgetMul)
        {
            float amount0 = d.active ? d.dust : 0f;
            if (amount0 < 0.003f && !_rigs.ContainsKey(Kind.Dust)) return;
            var r = Get(Kind.Dust);
            var ps = r.ps; var main = ps.main; var em = ps.emission;
            float amount = d.active ? d.dust : 0f;
            if (amount < 0.003f)
            {
                if (r.lastRate != 0f) { em.rateOverTime = 0f; r.lastRate = 0f; }
                if (ps.isPlaying && ps.particleCount == 0) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            float speed = Mathf.Max(0.6f, new Vector2(d.wind.x, d.wind.z).magnitude);
            float life = Mathf.Clamp(Radius * 1.4f / speed, 1.2f, 6f);
            float rate = Mathf.Min(2600f * amount * WeatherSettings.ParticleScale, r.budget * budgetMul / life);
            main.startLifetime = life;
            main.gravityModifier = 0f;
            em.rateOverTime = rate; r.lastRate = rate;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(d.wind.x * 0.8f, d.wind.x * 1.2f);
            vel.z = new ParticleSystem.MinMaxCurve(d.wind.z * 0.8f, d.wind.z * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.5f);
            var c = d.dustColor; c.a *= Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(amount));
            main.startColor = new ParticleSystem.MinMaxGradient(c * 0.85f, c);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f * d.size, 0.35f * d.size);
            if (!ps.isPlaying) ps.Play(true);
        }

        void PushMotes(Drive d, float gMod, float budgetMul)
        {
            float amount0 = d.active ? d.motes : 0f;
            if (amount0 < 0.003f && !_rigs.ContainsKey(Kind.Motes)) return;
            var r = Get(Kind.Motes);
            var ps = r.ps; var main = ps.main; var em = ps.emission;
            float amount = d.active ? d.motes : 0f;
            if (amount < 0.003f)
            {
                if (r.lastRate != 0f) { em.rateOverTime = 0f; r.lastRate = 0f; }
                if (ps.isPlaying && ps.particleCount == 0) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            // ballistic: up at 1-4.5 m/s under this world's gravity, so low-g arcs are long and slow
            float up = 3f;
            float life = Mathf.Clamp(2f * up / Mathf.Max(0.3f, d.gravity), 1.5f, 9f);
            main.startLifetime = life; main.gravityModifier = gMod;
            em.rateOverTime = Mathf.Min(260f * amount * WeatherSettings.ParticleScale, r.budget * budgetMul / life); r.lastRate = em.rateOverTime.constant;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 4.5f);
            if (!ps.isPlaying) ps.Play(true);
        }

        // ---------------------------------------------------------------- dust devils

        void TickDevils(Drive d, Transform cam)
        {
            int want = (d.active && cam != null) ? Mathf.RoundToInt(Mathf.Clamp(d.devils, 0f, 1.5f) * (d.extreme ? 5f : 3f)) : 0;
            for (int i = _devils.Count - 1; i >= 0; i--)
            {
                var dv = _devils[i];
                bool keep = i < want && dv.Alive;
                dv.Tick(Time.deltaTime, d, cam, keep);
                if (dv.Done) { Destroy(dv.gameObject); _devils.RemoveAt(i); }
            }
            if (cam != null && _devils.Count < want && Time.unscaledTime >= _nextDevilSpawn)
            {
                _nextDevilSpawn = Time.unscaledTime + Random.Range(4f, 12f);
                _devils.Add(DustDevil.Spawn(transform, _cat, cam, d));
            }
        }
        float _nextDevilSpawn;

        /// <summary>Drops every live particle and devil (scene change, entering the ship).</summary>
        public void ClearAll()
        {
            foreach (var r in _rigs.Values) { r.ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); r.lastRate = -1f; r.lastFall = -1f; }
            foreach (var dv in _devils) if (dv != null) Destroy(dv.gameObject);
            _devils.Clear();
        }

        public int LiveParticles { get { int n = 0; foreach (var r in _rigs.Values) n += r.ps.particleCount; return n; } }

        // ---------------------------------------------------------------- one dust devil

        public sealed class DustDevil : MonoBehaviour
        {
            ParticleSystem _ps; float _age, _life, _fade, _speedMul; Vector2 _drift;
            public bool Alive => _age < _life;
            public bool Done { get; private set; }

            public static DustDevil Spawn(Transform parent, WeatherCatalog cat, Transform cam, Drive d)
            {
                var go = new GameObject("DustDevil");
                go.transform.SetParent(parent, false);
                var dv = go.AddComponent<DustDevil>();
                float ang = Random.value * Mathf.PI * 2f, dist = Random.Range(45f, 110f);
                var pos = cam.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
                pos.y = GroundY(pos, cam.position.y);
                go.transform.position = pos;
                dv._life = Random.Range(40f, 90f); dv._speedMul = Random.Range(0.25f, 0.5f);
                dv.Build(cat, d);
                return dv;
            }

            void Build(WeatherCatalog cat, Drive d)
            {
                _ps = gameObject.AddComponent<ParticleSystem>();
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _ps.main; main.loop = true; main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f); main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var c = d.dustColor; c.a = Mathf.Clamp(c.a * 0.55f, 0.12f, 0.4f);
                main.startColor = new ParticleSystem.MinMaxGradient(c * 0.8f, c);
                main.maxParticles = 900; main.gravityModifier = 0f;
                var em = _ps.emission; em.rateOverTime = 150f;
                var sh = _ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.9f; sh.rotation = new Vector3(-90f, 0f, 0f); sh.radiusThickness = 0.3f;
                var vol = _ps.velocityOverLifetime; vol.enabled = true; vol.space = ParticleSystemSimulationSpace.Local;
                vol.y = new ParticleSystem.MinMaxCurve(5f, 8f); vol.orbitalY = new ParticleSystem.MinMaxCurve(3.5f, 5f);
                vol.x = 0f; vol.z = 0f;
                var sol = _ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 2.6f)));
                var col = _ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.6f, 0.7f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var pr = GetComponent<ParticleSystemRenderer>();
                pr.sharedMaterial = WeatherVisuals.Alpha(cat); pr.renderMode = ParticleSystemRenderMode.Billboard;
                pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pr.receiveShadows = false;
                _ps.Play(true);
            }

            public void Tick(float dt, Drive d, Transform cam, bool keep)
            {
                _age += dt;
                var em = _ps.emission;
                float target = keep ? Mathf.Clamp01(Mathf.Min(_age / 4f, (_life - _age) / 5f)) : 0f;
                _fade = Mathf.MoveTowards(_fade, target, dt * 0.4f);
                em.rateOverTime = 150f * _fade;
                var wind = new Vector2(d.wind.x, d.wind.z);
                var step = wind * _speedMul * dt + new Vector2(-wind.y, wind.x).normalized * Mathf.Sin(_age * 0.4f) * 0.8f * dt;
                var p = transform.position; p.x += step.x; p.z += step.y;
                p.y = Mathf.Lerp(p.y, GroundY(p, cam != null ? cam.position.y : p.y), dt * 4f);
                transform.position = p;
                if (cam != null && (p - cam.position).sqrMagnitude > 190f * 190f) _age = _life;
                if (!keep && _fade <= 0.001f && _ps.particleCount == 0) Done = true;
            }

            static float GroundY(Vector3 p, float fallback)
            {
                var t = Terrain.activeTerrain;
                if (t != null) return t.SampleHeight(p) + t.transform.position.y;
                return Physics.Raycast(new Vector3(p.x, fallback + 200f, p.z), Vector3.down, out var hit, 500f) ? hit.point.y : fallback - 1.6f;
            }
        }
    }
}



