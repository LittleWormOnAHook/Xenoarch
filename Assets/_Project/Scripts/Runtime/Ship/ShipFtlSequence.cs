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
    /// In-ship FTL jump. Spool (engine glow, the player turns to face the window), jump (star streaks and dust rushing
    /// past the window, lens distortion, chromatic aberration, bloom, FOV kick), arrival at the midpoint (fuel spent,
    /// sky swapped to the destination's orbit sky), drop-out flash. Then an autosave. Everything is runtime-only and
    /// cleaned up afterwards.
    /// </summary>
    public sealed class ShipFtlSequence : MonoBehaviour
    {
        public float spoolSeconds = 1.4f;
        public float dropSeconds = 0.9f;
        public float streakSpeed = 220f;
        public string autosaveSlot = "auto";

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
            float jumpSeconds = Mathf.Clamp(2.5f + Mathf.Log(1f + au * 4f) * 1.2f, 2.5f, 7f);

            rig.SetInputEnabled(false);
            var root = new GameObject("[FTL Sequence]");
            SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            Vector3 eye = cam.transform.position;

            // Effects
            var streaks = BuildStreaks(root.transform, eye, windowDir, shipDef != null ? shipDef.ftlStreakMaterial : null);
            GameObject dust = null, spool = null;
            if (shipDef != null && shipDef.ftlDustPrefab != null)
            {
                dust = Instantiate(shipDef.ftlDustPrefab, eye + windowDir * 25f, Quaternion.LookRotation(-windowDir), root.transform);
                dust.SetActive(false);
            }
            if (shipDef != null && shipDef.ftlSpoolPrefab != null)
                spool = Instantiate(shipDef.ftlSpoolPrefab, eye + windowDir * 14f + Vector3.down * 1.5f, Quaternion.LookRotation(-windowDir), root.transform);
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
                yield return null;
            }

            // Jump
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
            streaks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (dust != null) dust.SetActive(false);
            for (float t = 0f; t < dropSeconds; t += Time.deltaTime)
            {
                float k = t / dropSeconds;
                if (flash != null) flash.style.opacity = Mathf.Clamp01(1f - k * 1.4f) * 0.85f;
                audio.volume = (1f - k) * 0.4f;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov, k);
                vol.weight = (1f - k) * 0.3f;
                yield return null;
            }

            cam.fieldOfView = baseFov;
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
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 22f; shape.radiusThickness = 0.85f; shape.arc = 360f;
            var em = ps.emission; em.rateOverTime = 150f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f; r.lengthScale = 2f;
            r.sharedMaterial = mat != null ? mat : RuntimeAdditive();
            return ps;
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
