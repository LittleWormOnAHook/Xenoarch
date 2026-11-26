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
        [Range(0f, 1.5f)] public float tunnelBrightness = 0.85f;

        /// <summary>Jump (hyperspace) seconds for a given distance: min at 0 AU, max at farDistanceAU, never below 5 s.</summary>
        public float JumpSecondsFor(float au)
        {
            float k = Mathf.Clamp01(au / Mathf.Max(0.01f, farDistanceAU));
            float c = durationCurve != null && durationCurve.length > 0 ? Mathf.Clamp01(durationCurve.Evaluate(k)) : k;
            return Mathf.Max(5f, Mathf.Lerp(minJumpSeconds, Mathf.Max(minJumpSeconds, maxJumpSeconds), c));
        }

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

            float au = x.DistanceAU(x.CurrentBody, to);
            float jumpSeconds = JumpSecondsFor(au);
            Debug.Log($"[Xenoarch] FTL: {au:0.###} AU -> {jumpSeconds:0.0}s jump");

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
            Material tunnelMat = tunnel ? BuildTunnel(root.transform, eye, windowDir) : null;
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
            for (float t = 0f; t < spoolSeconds; t += Time.deltaTime)
            {
                float k = t / spoolSeconds;
                if (!seated) rig.Teleport(rig.Root.position, Quaternion.Slerp(startRot, faceRot, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.5f))));
                audio.volume = k * 0.35f; audio.pitch = 0.6f + k * 0.3f;
                vol.weight = k * 0.25f;
                TunnelTick(tunnelMat, Mathf.Clamp01((k - 0.4f) / 0.6f) * 0.35f, tunnelSpinSign, 0.4f);
                yield return null;
            }

            // Jump
            var hud = CountdownHud.Show($"FTL: {to.displayName}", jumpSeconds, hum: false); // arrival countdown (beeps + chime)
            streaks.Play();
            if (dust != null) dust.SetActive(true);
            bool arrived = false;
            for (float t = 0f; t < jumpSeconds; t += Time.deltaTime)
            {
                float k = t / jumpSeconds;
                float env = Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI); // 0 -> 1 -> 0
                vol.weight = 0.25f + 0.75f * env;
                lens.intensity.value = -0.55f * env;
                ca.intensity.value = env;
                bloom.intensity.value = 1.5f + 3f * env;
                cam.fieldOfView = baseFov + 14f * env;
                audio.volume = 0.35f + 0.45f * env; audio.pitch = 0.9f + 0.5f * env;
                var em = streaks.emission; em.rateOverTime = Mathf.Lerp(150f, 1400f, env);
                if (hud != null) hud.Tick(t);
                float tin = Mathf.Clamp01(t / 0.8f), tout = Mathf.Clamp01((jumpSeconds - t) / 0.5f);
                TunnelTick(tunnelMat, Mathf.Lerp(0.35f, 1f, tin) * Mathf.Lerp(0.6f, 1f, env) * tout, tunnelSpinSign, 0.4f + 0.6f * tin);
                if (!arrived && k >= 0.5f)
                {
                    arrived = true;
                    if (!x.CommitTravel(to, out var why)) Debug.LogWarning($"[Xenoarch] FTL: arrival failed ({why})");
                    EnvironmentDirector.ApplySky(session, gameObject.scene.name);
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
                if (flash != null) flash.style.opacity = Mathf.Clamp01(1f - k * 1.4f) * 0.85f;
                audio.volume = (1f - k) * 0.4f;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov, k);
                vol.weight = (1f - k) * 0.3f;
                yield return null;
            }

            cam.fieldOfView = baseFov;
            if (tunnelMat != null) Destroy(tunnelMat);
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
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.85f, 1f), new Color(1f, 0.9f, 0.75f));
            main.maxParticles = 6000;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // orbital velocity spins around this emitter's view axis
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 22f; shape.radiusThickness = 0.85f; shape.arc = 360f;
            var em = ps.emission; em.rateOverTime = 150f;
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
        // A cone-shaped tube along the view axis (wide near the window, narrowing to a dark vanishing point), drawn from the
        // inside with a procedural additive streak texture in gold/orange, cyan and purple. The texture scrolls toward the
        // viewer and rotates around the axis. The far end fades to black, so there is no bright core. Ship walls occlude it,
        // so it only shows through the windows.
        const float TunnelLength = 320f, TunnelNearRadius = 34f, TunnelFarRadius = 3f;

        Material BuildTunnel(Transform parent, Vector3 eye, Vector3 dir)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) return null;
            var go = new GameObject("FTL Tunnel");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(dir, Vector3.up));
            go.AddComponent<MeshFilter>().sharedMesh = TunnelMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            var m = new Material(sh) { name = "FTL Tunnel (runtime)" };
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.One); m.SetInt("_ZWrite", 0);
            m.SetFloat("_Cull", 0f); m.SetInt("_Cull", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = 2990; // behind the streaks
            var tex = TunnelTexture();
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            m.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
            m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
            mr.sharedMaterial = m;
            return m;
        }

        void TunnelTick(Material m, float alpha, float spinSign, float speedK)
        {
            if (m == null) return;
            var off = m.GetTextureOffset("_BaseMap");
            off.y += tunnelRushSpeed * speedK * Time.deltaTime;          // rush toward the window
            off.x += tunnelSpin * spinSign * Mathf.Max(0.25f, speedK) * Time.deltaTime; // swirl
            off.x -= Mathf.Floor(off.x); off.y -= Mathf.Floor(off.y);
            m.SetTextureOffset("_BaseMap", off);
            var c = m.GetColor("_BaseColor");
            // ease toward the target so fade-out at drop is smooth rather than a pop
            c.a = Mathf.MoveTowards(c.a, alpha * tunnelBrightness, Time.deltaTime * 2.5f);
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
                float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / 0.08f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - 0.45f) / 0.5f)));
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

        static Texture2D _tunnelTex;
        static Texture2D TunnelTexture()
        {
            if (_tunnelTex != null) return _tunnelTex;
            const int W = 512, H = 256; // x = around the tube, y = along it
            var acc = new Color[W * H];
            var rng = new System.Random(1337);
            var palette = new[]
            {
                new Color(1f, 0.62f, 0.18f), new Color(1f, 0.78f, 0.3f), new Color(1f, 0.45f, 0.12f), // orange / gold
                new Color(0.25f, 0.85f, 1f), new Color(0.35f, 0.6f, 1f),                               // cyan / blue
                new Color(0.65f, 0.3f, 1f), new Color(0.85f, 0.35f, 0.95f),                            // purple / magenta
            };
            const float skew = 0.35f; // streaks lean around the tube (pixels in x per pixel in y) -> spiral
            for (int n = 0; n < 420; n++)
            {
                var c = palette[rng.Next(palette.Length)];
                float bright = 0.35f + (float)rng.NextDouble() * 0.65f;
                float x0 = (float)rng.NextDouble() * W, y0 = (float)rng.NextDouble() * H;
                int len = 25 + rng.Next(110);
                float width = 0.6f + (float)rng.NextDouble() * 1.8f;
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
            _tunnelTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "FTL Tunnel Streaks", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            _tunnelTex.SetPixels(acc);
            _tunnelTex.Apply(true, true);
            return _tunnelTex;
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
            var r = doc.rootVisualElement;
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
