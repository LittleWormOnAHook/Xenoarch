using System.Collections;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DMI.Xenoarch.Ship
{
    /// <summary>
    /// In-ship FTL jump. Spool (the player turns to face the window, the swirl tunnel fades in), jump (swirling streak
    /// tunnel, skewed star streaks and dust rushing past the window, lens distortion, chromatic aberration, bloom, FOV
    /// kick; length scales with distance, min 5 s), arrival at the midpoint (fuel spent,
    /// sky swapped to the destination's orbit sky), drop-out flash. Then an autosave. Everything is runtime-only and
    /// cleaned up afterwards.
    /// </summary>
    public sealed class ShipFtlSequence : MonoBehaviour
    {
        public float spoolSeconds = 1.4f;
        public float dropSeconds = 0.9f;
        public string autosaveSlot = "auto";

        [Header("Jump duration (scales with distance)")]
        [Tooltip("Jump time for a hop of ~0 AU (e.g. Earth to the Moon). Never shorter than this.")] public float minJumpSeconds = 5f;
        [Tooltip("Jump time at farDistanceAU and beyond.")] public float maxJumpSeconds = 15f;
        [Tooltip("Distance (AU) that gets maxJumpSeconds.")] public float farDistanceAU = 30f;
        [Tooltip("Maps distance (0 = here, 1 = farDistanceAU) to duration (0 = min, 1 = max).")]
        public AnimationCurve durationCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.1f, 0.35f, 1.5f, 1.5f), new Keyframe(1f, 1f, 0.4f, 0f));

        [Header("Star streaks")]
        public float streakSpeed = 220f;
        [Tooltip("Sideways (orbital) speed of the streaks around the view axis, radians/sec. 0 = purely radial; higher = more skewed, curving with the tunnel swirl.")]
        [Range(0f, 3f)] public float streakSwirl = 1.2f;
        [Tooltip("Streak stretch: length multiplier on the particle velocity.")] [Range(0.02f, 0.3f)] public float streakStretch = 0.11f;

        [Header("Swirl tunnel")]
        public bool tunnel = true;
        [Tooltip("Also show the ship's ftlDustPrefab (Creepy Cat Particle_Dust_Space: huge grey nebula sprites). Off by default: it washes the tunnel out grey.")]
        public bool vendorDust = false;
        [Tooltip("How fast the tunnel streaks rush toward the window (texture repeats/sec).")] public float tunnelRushSpeed = 1.6f;
        [Tooltip("How fast the tunnel rotates around the view axis (turns/sec). Same direction as the star streak swirl.")] public float tunnelSpin = 0.12f;
        [Range(0f, 4f)] public float tunnelBrightness = 1.6f;
        [Tooltip("Bright cyan-white core with a violet / blue inner ring at the tunnel's vanishing point.")] public bool tunnelCore = true;
        [Range(0f, 8f)] public float coreBrightness = 3.2f;

        /// <summary>Jump (hyperspace) seconds for a given distance: min at 0 AU, max at farDistanceAU, never below 5 s.</summary>
        public float JumpSecondsFor(float au)
        {
            float k = Mathf.Clamp01(au / Mathf.Max(0.01f, farDistanceAU));
            float c = durationCurve != null && durationCurve.length > 0 ? Mathf.Clamp01(durationCurve.Evaluate(k)) : k;
            return Mathf.Max(5f, Mathf.Lerp(minJumpSeconds, Mathf.Max(minJumpSeconds, maxJumpSeconds), c));
        }

        [Header("Interstellar jump (t186u: a longer version of the same sequence)")]
        [Tooltip("Spool multiplier for a jump to another star system.")] public float interstellarSpoolScale = 2.5f;
        [Tooltip("Jump time for the nearest star systems.")] public float interstellarMinSeconds = 25f;
        [Tooltip("Jump time at interstellarFarLy and beyond.")] public float interstellarMaxSeconds = 45f;
        public float interstellarFarLy = 15f;

        /// <summary>Hyperspace seconds for a jump between star systems (always longer than any in-system hop).</summary>
        public float InterstellarSecondsFor(float ly) =>
            Mathf.Lerp(interstellarMinSeconds, Mathf.Max(interstellarMinSeconds, interstellarMaxSeconds), Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ly / Mathf.Max(0.1f, interstellarFarLy))));

        public bool Running { get; private set; }
        public const string PanelId = "ftl";

        public bool Play(IPlayerRig rig, CelestialBodyDefinition to, Vector3 windowDir)
        {
            if (Running || rig == null || to == null) return false;
            StartCoroutine(Run(rig, to, windowDir));
            return true;
        }

        IEnumerator Run(IPlayerRig rig, CelestialBodyDefinition to, Vector3 windowDir)
        {
            Running = true;
            var session = XenoarchRuntime.Session;
            var x = session.Exploration;
            var shipDef = x.ShipDefinition;
            var cam = rig.ViewCamera;
            windowDir.y = 0f;
            if (windowDir.sqrMagnitude < 0.01f) windowDir = cam.transform.forward;
            windowDir.y = 0f; windowDir.Normalize();

            bool interstellar = x.IsInterstellar(to);
            float au = x.DistanceAU(x.CurrentBody, to), ly = interstellar ? x.DistanceLy(to) : 0f;
            float jumpSeconds = interstellar ? InterstellarSecondsFor(ly) : JumpSecondsFor(au);
            float spool = interstellar ? spoolSeconds * Mathf.Max(1f, interstellarSpoolScale) : spoolSeconds;
            Debug.Log(interstellar ? $"[Xenoarch] FTL: interstellar {ly:0.##} ly -> {jumpSeconds:0.0}s jump" : $"[Xenoarch] FTL: {au:0.###} AU -> {jumpSeconds:0.0}s jump");

            rig.SetInputEnabled(false);
            var root = new GameObject("[FTL Sequence]");
            SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            Vector3 eye = cam.transform.position;

            // Effects
            var streaks = BuildStreaks(root.transform, eye, windowDir, shipDef != null ? shipDef.ftlStreakMaterial : null);
            GameObject dust = null;
            if (vendorDust && shipDef != null && shipDef.ftlDustPrefab != null)
            {
                dust = Instantiate(shipDef.ftlDustPrefab, eye + windowDir * 25f, Quaternion.LookRotation(-windowDir), root.transform);
                dust.SetActive(false);
            }
            // (The old spool glow, Creepy Cat Particle_Afterburner_Small, is no longer used: its Flare material is URP/Lit with
            // no texture, so it drew as small grey quads in the middle of the window.)
            Material tunnelMat = tunnel ? BuildTunnel(root.transform, eye, windowDir, 1f, TunnelTexture(), "FTL Tunnel") : null;
            // inner layer: a narrower violet / blue tube spinning faster -> thick, layered vortex
            Material innerMat = tunnel ? BuildTunnel(root.transform, eye, windowDir, 0.42f, TunnelTexture(true), "FTL Tunnel Inner") : null;
            if (innerMat != null) innerMat.SetTextureScale("_BaseMap", new Vector2(2f, 4f));
            Material coreMat = tunnel && tunnelCore ? BuildCore(root.transform, eye, windowDir) : null;
            float tunnelSpinSign = -1f; // matches the streak orbit direction as seen from the cockpit
            var vol = BuildVolume(root, out var lens, out var ca, out var bloom);
            var audio = root.AddComponent<AudioSource>();
            audio.clip = shipDef != null && shipDef.ftlAudio != null ? shipDef.ftlAudio : MakeRumble();
            audio.loop = true; audio.volume = 0f; audio.spatialBlend = 0f; audio.Play();
            var flash = BuildFlash(root);
            float baseFov = cam.fieldOfView;

            // Spool: turn to face the window, engines wind up.
            var startRot = rig.Root.rotation;
            bool seated = rig is IPlayerSeat st && st.Seated; // in a cockpit chair already facing the window: don't turn
            var faceRot = Quaternion.LookRotation(windowDir, Vector3.up);
            for (float t = 0f; t < spool; t += Time.deltaTime)
            {
                float k = t / spool;
                if (!seated) rig.Teleport(rig.Root.position, Quaternion.Slerp(startRot, faceRot, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.5f))));
                audio.volume = k * 0.35f; audio.pitch = 0.6f + k * 0.3f;
                vol.weight = k * 0.25f;
                TunnelTick(tunnelMat, Mathf.Clamp01((k - 0.4f) / 0.6f) * 0.35f, tunnelSpinSign, 0.4f);
                TunnelTick(innerMat, Mathf.Clamp01((k - 0.6f) / 0.4f) * 0.25f, tunnelSpinSign * 1.8f, 0.5f, InnerTint);
                CoreTick(coreMat, Mathf.Clamp01((k - 0.5f) / 0.5f) * 0.3f, t);
                yield return null;
            }

            // Jump
            var hud = CountdownHud.Show(interstellar ? $"INTERSTELLAR JUMP: {to.displayName}" : $"FTL: {to.displayName}", jumpSeconds, hum: false); // arrival countdown (beeps + chime)
            streaks.Play();
            DMI.Xenoarch.Galaxy.SunDirector.Hidden = true; // no sun / lens flare inside the tunnel
            if (dust != null) dust.SetActive(true);
            bool arrived = false;
            for (float t = 0f; t < jumpSeconds; t += Time.deltaTime)
            {
                float k = t / jumpSeconds;
                float env = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI); // 0 -> 1 -> 0
                vol.weight = 0.25f + 0.75f * env;
                lens.intensity.value = -0.55f * env;
                ca.intensity.value = env;
                bloom.intensity.value = 2f + 5f * env;
                cam.fieldOfView = baseFov + 14f * env;
                audio.volume = 0.35f + 0.45f * env; audio.pitch = 0.9f + 0.5f * env;
                var em = streaks.emission; em.rateOverTime = Mathf.Lerp(600f, 5200f, env);
                if (hud != null) hud.Tick(t);
                float tin = Mathf.Clamp01(t / 0.8f), tout = Mathf.Clamp01((jumpSeconds - t) / 0.5f);
                float tunnelA = Mathf.Lerp(0.35f, 1f, tin) * Mathf.Lerp(0.7f, 1f, env) * tout;
                TunnelTick(tunnelMat, tunnelA, tunnelSpinSign, 0.4f + 0.6f * tin);
                TunnelTick(innerMat, tunnelA * 0.9f, tunnelSpinSign * 1.8f, 0.6f + 0.9f * tin, InnerTint);
                CoreTick(coreMat, Mathf.Lerp(0.3f, 1f, tin) * Mathf.Lerp(0.75f, 1f, env) * tout, t);
                if (!arrived && k >= 0.5f)
                {
                    arrived = true;
                    if (!x.CommitTravel(to, out var why)) Debug.LogWarning($"[Xenoarch] FTL: arrival failed ({why})");
                    EnvironmentDirector.ApplySky(session, gameObject.scene.name);
                    DMI.Xenoarch.Galaxy.SunDirector.Refresh();       // new star distance / size / light
                    DMI.Xenoarch.Galaxy.OrbitViewDirector.Refresh(); // planet outside the window (if any)
                    Debug.Log($"[Xenoarch] FTL: arrived at {to.displayName} (fuel {x.Ship.fuel:0.#})");
                }
                yield return null;
            }

            // Drop-out flash
            if (hud != null) hud.Finish($"Arrived: {to.displayName}");
            streaks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (dust != null) dust.SetActive(false);
            for (float t = 0f; t < dropSeconds; t += Time.deltaTime)
            {
                float k = t / dropSeconds;
                TunnelTick(tunnelMat, 0f, tunnelSpinSign, 0f);
                TunnelTick(innerMat, 0f, tunnelSpinSign * 1.8f, 0f, InnerTint);
                CoreTick(coreMat, 0f, t);
                if (k > 0.25f) DMI.Xenoarch.Galaxy.SunDirector.Hidden = false;
                if (flash != null) flash.style.opacity = Mathf.Clamp01(1f - k * 1.4f) * 0.85f;
                audio.volume = (1f - k) * 0.4f;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov, k);
                vol.weight = (1f - k) * 0.3f;
                yield return null;
            }

            cam.fieldOfView = baseFov;
            if (tunnelMat != null) Destroy(tunnelMat);
            if (innerMat != null) Destroy(innerMat);
            if (coreMat != null) Destroy(coreMat);
            DMI.Xenoarch.Galaxy.SunDirector.Hidden = false;
            Destroy(root);
            rig.SetInputEnabled(true);
            Running = false;
            var rt = XenoarchRuntime.Instance;
            if (rt != null && !string.IsNullOrEmpty(autosaveSlot)) rt.SaveGame(autosaveSlot);
        }

        ParticleSystem BuildStreaks(Transform parent, Vector3 eye, Vector3 dir, Material mat)
        {
            var go = new GameObject("FTL Streaks");
            go.transform.SetParent(parent, false);
            float spawnDist = 140f, killDist = 6f;
            go.transform.SetPositionAndRotation(eye + dir * spawnDist, Quaternion.LookRotation(-dir));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = true; main.duration = 1f;
            main.startSpeed = streakSpeed;
            main.startLifetime = (spawnDist - killDist) / streakSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.95f, 1f), new Color(1f, 0.97f, 0.9f)); // white star streaks
            main.maxParticles = 14000;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // orbital velocity spins around this emitter's view axis
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 30f; shape.radiusThickness = 0.9f; shape.arc = 360f;
            var em = ps.emission; em.rateOverTime = 600f;
            if (streakSwirl > 0f)
            {
                // Tangential (orbital) speed around the view axis so streaks curve with the tunnel swirl instead of running
                // purely radially. The streaks stretch along their velocity, so this also angles them.
                var vel = ps.velocityOverLifetime;
                vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = 0f; vel.y = 0f; vel.z = 0f;
                // All three orbital curves must share one mode (TwoConstants), else Unity warns every frame.
                vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f); vel.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
                vel.orbitalZ = new ParticleSystem.MinMaxCurve(streakSwirl * 0.75f, streakSwirl * 1.25f);
                vel.radial = -streakSwirl * 3f; // a little inward pull keeps the spiral from flinging out of the window
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = streakStretch; r.lengthScale = 2.6f;
            r.sharedMaterial = mat != null ? mat : RuntimeAdditive();
            return ps;
        }

        // ---------- Swirl tunnel ----------
        // Two cone-shaped tubes along the view axis (wide near the window, narrowing to the vanishing point), drawn from the
        // inside with procedural additive streak textures: the outer vortex mostly orange / gold, a narrower inner layer
        // cyan / blue / violet spinning faster. They scroll toward the viewer and rotate around the axis, and a bright
        // cyan-white core with a violet ring sits at the far end (BuildCore). HDR tints so bloom makes it glow. Ship walls
        // occlude it, so it only shows through the windows.
        const float TunnelLength = 320f, TunnelNearRadius = 46f, TunnelFarRadius = 2.5f;
        static readonly Color InnerTint = new Color(0.9f, 0.95f, 1.25f); // inner layer a touch cooler / brighter

        Material BuildTunnel(Transform parent, Vector3 eye, Vector3 dir, float radiusScale, Texture2D tex, string name)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(dir, Vector3.up));
            go.transform.localScale = new Vector3(radiusScale, radiusScale, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = TunnelMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            var m = new Material(sh) { name = name + " (runtime)" };
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.One); m.SetInt("_ZWrite", 0);
            m.SetFloat("_Cull", 0f); m.SetInt("_Cull", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = radiusScale < 1f ? 2991 : 2990; // behind the streaks
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            m.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
            m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
            mr.sharedMaterial = m;
            return m;
        }

        void TunnelTick(Material m, float alpha, float spinSign, float speedK) => TunnelTick(m, alpha, spinSign, speedK, Color.white);

        void TunnelTick(Material m, float alpha, float spinSign, float speedK, Color tint)
        {
            if (m == null) return;
            var off = m.GetTextureOffset("_BaseMap");
            off.y += tunnelRushSpeed * speedK * Time.deltaTime;          // rush toward the window
            off.x += tunnelSpin * spinSign * Mathf.Max(0.25f, speedK) * Time.deltaTime; // swirl
            off.x -= Mathf.Floor(off.x); off.y -= Mathf.Floor(off.y);
            m.SetTextureOffset("_BaseMap", off);
            var c = m.GetColor("_BaseColor");
            // ease toward the target so fade-out at drop is smooth rather than a pop
            // alpha = fade (<= 1); brightness goes into HDR rgb so bloom makes it glow
            c.a = Mathf.MoveTowards(c.a, Mathf.Clamp01(alpha), Time.deltaTime * 2.5f);
            c.r = tint.r * tunnelBrightness; c.g = tint.g * tunnelBrightness; c.b = tint.b * tunnelBrightness;
            m.SetColor("_BaseColor", c);
        }

        static Mesh _tunnelMesh;
        static Mesh TunnelMesh()
        {
            if (_tunnelMesh != null) return _tunnelMesh;
            const int around = 64, along = 48;
            var v = new Vector3[(around + 1) * (along + 1)];
            var uv = new Vector2[v.Length];
            var col = new Color[v.Length];
            var tri = new int[around * along * 6];
            for (int j = 0; j <= along; j++)
            {
                float s = j / (float)along;
                float z = 4f + s * TunnelLength;
                float r = Mathf.Lerp(TunnelNearRadius, TunnelFarRadius, Mathf.Pow(s, 0.55f));
                // fade in from the window, full in the middle, fade to black at the vanishing point (no bright core)
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 0.06f)) * (1f - 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - 0.55f) / 0.45f)));
                for (int i = 0; i <= around; i++)
                {
                    float u = i / (float)around;
                    float ang = u * Mathf.PI * 2f;
                    int k = j * (around + 1) + i;
                    v[k] = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, z);
                    uv[k] = new Vector2(u, s * 2.2f);
                    col[k] = new Color(1f, 1f, 1f, a);
                }
            }
            int t = 0;
            for (int j = 0; j < along; j++)
                for (int i = 0; i < around; i++)
                {
                    int a0 = j * (around + 1) + i, a1 = a0 + 1, b0 = a0 + around + 1, b1 = b0 + 1;
                    tri[t++] = a0; tri[t++] = b0; tri[t++] = a1;
                    tri[t++] = a1; tri[t++] = b0; tri[t++] = b1;
                }
            _tunnelMesh = new Mesh { name = "FTL Tunnel" };
            _tunnelMesh.vertices = v; _tunnelMesh.uv = uv; _tunnelMesh.colors = col; _tunnelMesh.triangles = tri;
            _tunnelMesh.RecalculateBounds();
            _tunnelMesh.bounds = new Bounds(new Vector3(0f, 0f, TunnelLength * 0.5f), new Vector3(TunnelNearRadius * 2f, TunnelNearRadius * 2f, TunnelLength + 10f));
            return _tunnelMesh;
        }

        static Texture2D _tunnelTex, _tunnelTexInner;
        static Texture2D TunnelTexture() => TunnelTexture(false);
        /// <param name="inner">false = the outer vortex (mostly orange / gold, some cyan and purple); true = the inner
        /// layer (cyan / blue / violet) that frames the core.</param>
        static Texture2D TunnelTexture(bool inner)
        {
            if (!inner && _tunnelTex != null) return _tunnelTex;
            if (inner && _tunnelTexInner != null) return _tunnelTexInner;
            const int W = 512, H = 256; // x = around the tube, y = along it
            var acc = new Color[W * H];
            var rng = new System.Random(inner ? 4242 : 1337);
            var palette = inner ? new[]
            {
                new Color(0.25f, 0.85f, 1f), new Color(0.35f, 0.6f, 1f), new Color(0.55f, 0.35f, 1f),
                new Color(0.7f, 0.4f, 1f), new Color(0.8f, 0.95f, 1f),
            } : new[]
            {
                new Color(1f, 0.62f, 0.18f), new Color(1f, 0.78f, 0.3f), new Color(1f, 0.45f, 0.12f), // orange / gold
                new Color(1f, 0.70f, 0.22f), new Color(1f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.45f), // (weighted: mostly gold)
                new Color(0.25f, 0.85f, 1f), new Color(0.35f, 0.6f, 1f),                               // cyan / blue
                new Color(0.65f, 0.3f, 1f), new Color(0.85f, 0.35f, 0.95f),                            // purple / magenta
            };
            const float skew = 0.35f; // streaks lean around the tube (pixels in x per pixel in y) -> spiral
            for (int n = 0; n < 1500; n++)
            {
                var c = palette[rng.Next(palette.Length)];
                float bright = 0.35f + (float)rng.NextDouble() * 0.65f;
                float x0 = (float)rng.NextDouble() * W, y0 = (float)rng.NextDouble() * H;
                int len = 25 + rng.Next(110);
                float width = 0.8f + (float)rng.NextDouble() * 2.2f;
                for (int d = 0; d < len; d++)
                {
                    float f = d / (float)len;
                    float inten = bright * Mathf.Sin(f * Mathf.PI) * (0.4f + 0.6f * f); // brighter toward the head
                    float cx = x0 + d * skew;
                    int yy = ((int)(y0 + d) % H + H) % H;
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        float fall = Mathf.Exp(-(dx * dx) / (width * width));
                        if (fall < 0.02f) continue;
                        int xx = (((int)cx + dx) % W + W) % W;
                        acc[yy * W + xx] += c * (inten * fall);
                    }
                }
            }
            for (int i = 0; i < acc.Length; i++)
            {
                var c = acc[i];
                float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (m > 1f) c /= m; // keep hue, avoid white-out where streaks overlap
                c.a = Mathf.Clamp01(m);
                acc[i] = c;
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = inner ? "FTL Tunnel Streaks Inner" : "FTL Tunnel Streaks", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            tex.SetPixels(acc);
            tex.Apply(true, true);
            if (inner) _tunnelTexInner = tex; else _tunnelTex = tex;
            return tex;
        }

        // ---------- Core ----------
        // Camera-facing additive disc deep in the tunnel: a white-cyan centre, a violet / blue ring around it and a soft
        // halo. HDR, so bloom turns it into the bright vanishing point of the vortex.
        Material BuildCore(Transform parent, Vector3 eye, Vector3 dir)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) return null;
            var go = new GameObject("FTL Core");
            go.transform.SetParent(parent, false);
            float d = TunnelLength * 0.7f;
            go.transform.SetPositionAndRotation(eye + dir * d, Quaternion.LookRotation(dir, Vector3.up));
            float size = 2f * d * Mathf.Tan(13f * Mathf.Deg2Rad); // ~26 degrees across incl. halo
            go.transform.localScale = new Vector3(size, size, 1f);
            var q = new Mesh { name = "FTL Core Quad" };
            q.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            q.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            q.triangles = new[] { 0, 2, 1, 2, 3, 1, 0, 1, 2, 2, 1, 3 }; // both sides
            q.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = q;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            var m = new Material(sh) { name = "FTL Core (runtime)" };
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.One); m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = 2992;
            var tex = CoreTexture();
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
            mr.sharedMaterial = m;
            return m;
        }

        void CoreTick(Material m, float alpha, float time)
        {
            if (m == null) return;
            var c = m.GetColor("_BaseColor");
            c.a = Mathf.MoveTowards(c.a, Mathf.Clamp01(alpha), Time.deltaTime * 2.5f);
            float pulse = 1f + 0.12f * Mathf.Sin(time * 7.3f) + 0.06f * Mathf.Sin(time * 17.1f);
            c.r = c.g = c.b = coreBrightness * pulse;
            m.SetColor("_BaseColor", c);
        }

        static Texture2D _coreTex;
        static Texture2D CoreTexture()
        {
            if (_coreTex != null) return _coreTex;
            const int N = 256;
            var px = new Color[N * N];
            var white = new Color(0.92f, 1f, 1f); var cyan = new Color(0.35f, 0.9f, 1f);
            var violet = new Color(0.55f, 0.3f, 1f); var blue = new Color(0.25f, 0.4f, 1f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float core = Mathf.Exp(-r * r / 0.006f);                       // white-hot centre
                    float glow = Mathf.Exp(-r * r / 0.035f);                       // cyan glow
                    float ringR = 0.30f + 0.015f * Mathf.Sin(ang * 9f);            // slightly wavy violet ring
                    float ring = Mathf.Exp(-(r - ringR) * (r - ringR) / 0.0025f);
                    float halo = Mathf.Exp(-r * r / 0.22f) * 0.35f;                // soft blue halo
                    var c = white * core * 1.0f + cyan * glow * 0.8f + violet * ring * 0.75f + blue * halo;
                    float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    float a = Mathf.Clamp01(m) * Mathf.Clamp01((1f - r) / 0.25f);
                    if (m > 1f) c /= m;
                    c.a = a;
                    px[y * N + x] = c;
                }
            _coreTex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "FTL Core", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _coreTex.SetPixels(px);
            _coreTex.Apply(true, true);
            return _coreTex;
        }

        static Material _additive;
        static Material RuntimeAdditive()
        {
            if (_additive != null) return _additive;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) return null;
            _additive = new Material(sh) { name = "FTL Streak (runtime)" };
            _additive.SetFloat("_Surface", 1f); _additive.SetFloat("_Blend", 2f); // transparent, additive
            _additive.SetOverrideTag("RenderType", "Transparent");
            _additive.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); _additive.SetInt("_DstBlend", (int)BlendMode.One); _additive.SetInt("_ZWrite", 0);
            _additive.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); _additive.EnableKeyword("_BLENDMODE_ADD");
            _additive.renderQueue = 3000;
            _additive.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.9f));
            return _additive;
        }

        static Volume BuildVolume(GameObject root, out LensDistortion lens, out ChromaticAberration ca, out Bloom bloom)
        {
            var vol = root.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 100f; vol.weight = 0f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            lens = p.Add<LensDistortion>(true); lens.intensity.Override(0f); lens.scale.Override(1f);
            ca = p.Add<ChromaticAberration>(true); ca.intensity.Override(0f);
            bloom = p.Add<Bloom>(true); bloom.intensity.Override(1.5f); bloom.threshold.Override(0.9f);
            var mb = p.Add<MotionBlur>(true); mb.intensity.Override(0.6f);
            vol.profile = p;
            return vol;
        }

        static VisualElement BuildFlash(GameObject root)
        {
            var doc = root.AddComponent<UIDocument>();
            doc.panelSettings = XenoarchUI.LoadPanelSettings();
            doc.sortingOrder = 60;
            var r = doc.rootVisualElement; DMI.Xenoarch.UI.XenoarchUI.FillRoot(r);
            if (r == null) return null;
            r.pickingMode = PickingMode.Ignore;
            var f = new VisualElement { pickingMode = PickingMode.Ignore };
            f.style.position = Position.Absolute; f.style.left = 0; f.style.top = 0; f.style.right = 0; f.style.bottom = 0;
            f.style.backgroundColor = new Color(0.85f, 0.92f, 1f);
            f.style.opacity = 0f;
            r.Add(f);
            return f;
        }

        static AudioClip _rumble;
        static AudioClip MakeRumble()
        {
            if (_rumble != null) return _rumble;
            const int rate = 22050; int n = rate * 2;
            var d = new float[n]; var rng = new System.Random(7); float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.05f;
                float t = i / (float)rate;
                d[i] = lp * 2.2f + Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.15f;
            }
            _rumble = AudioClip.Create("FTL Rumble", n, 1, rate, false);
            _rumble.SetData(d, 0);
            return _rumble;
        }
    }
}
