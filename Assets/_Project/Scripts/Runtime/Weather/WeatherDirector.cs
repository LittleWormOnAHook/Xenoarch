using System.Collections.Generic;
using DMI.Xenoarch.Core;
using DMI.Xenoarch.Galaxy;
using DMI.Xenoarch.Player;
using DMI.Xenoarch.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DMI.Xenoarch.Weather
{
    /// <summary>
    /// Weather for every landable planet and moon. Ported from ASH's weather director (state blending, scheduler with
    /// calm periods between storm-class events, shaped gusts, indoor muffling) and adapted to Xenoarch: it picks the
    /// <see cref="WeatherProfile"/> of the scene's body on every scene load, never runs in the ship, a station, the menu
    /// or while a loading screen is up, follows the player's camera, and feeds particles, sound, fog and sun dimming.
    /// Created at runtime (no scene objects are placed or edited). Lives under DontDestroyOnLoad.
    /// <para>Test controls (when debug hotkeys are enabled in XenoarchConfig): Ctrl+Shift + W next weather, E extreme
    /// preset, Q weather on/off, = / - intensity, R back to the scheduler, 0 reset test settings.</para>
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class WeatherDirector : MonoBehaviour
    {
        public static WeatherDirector Instance { get; private set; }

        // ---- public read-outs (a future compass / wind HUD can read these)
        public WeatherProfile Profile { get; private set; }
        public WeatherState Current => _cur;
        public string CurrentName { get; private set; } = "None";
        public bool Active { get; private set; }
        public Vector2 WindDirection { get; private set; } = new Vector2(1f, 0f);
        public float WindMS { get; private set; }
        public float Gust01 { get; private set; }
        public int Strikes { get; private set; }

        WeatherCatalog _cat;
        WeatherParticles _particles;
        WeatherAudio _audio;
        WeatherState _from, _to, _cur;
        WeatherPreset _toPreset;
        float _t = 1f, _dur = 1f, _phaseUntil, _calmElapsed, _calmNeeded, _windAngle, _windSeed;
        bool _inMajor;
        string _forcedName;
        System.Random _rng = new System.Random();
        string _sceneName = "", _bodyKey = "";
        float _nextResolve, _nextCam, _nextRover, _gravity = 9.81f;
        Transform _cam;
        bool _roverOcc;
        float _flash, _nextStrikeCheck;

        // fog / sun / ambient bookkeeping
        bool _fogCaptured; bool _baseFog; float _baseFogDensity; Color _baseFogColor; FogMode _baseFogMode;
        Light _sun; float _sunBase, _sunLast = -1f, _sunMul = 1f;
        float _ambBase; Color _ambBaseColor; AmbientMode _ambMode; bool _ambCaptured;
        float _settleAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null || FindAnyObjectByType<WeatherDirector>() != null) return;
            var go = new GameObject("[Xenoarch] Weather");
            DontDestroyOnLoad(go);
            go.AddComponent<WeatherDirector>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _cat = WeatherCatalog.Load();
            _particles = WeatherParticles.Create(transform);
            _audio = WeatherAudio.Create(transform, _cat);
            _windAngle = Random.value * 360f; _windSeed = Random.value * 100f;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (Instance == this) Instance = null;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            ResetForScene();
        }
        void OnSceneUnloaded(Scene s) { }

        void ResetForScene()
        {
            _sceneName = ""; _bodyKey = ""; _nextResolve = 0f; Profile = null; Active = false;
            _fogCaptured = false; _ambCaptured = false; _sun = null; _sunLast = -1f; _sunMul = 1f; _flash = 0f;
            _cam = null; _nextCam = 0f; _settleAt = Time.unscaledTime + 1.5f;
            if (_particles != null) _particles.ClearAll();
            if (_audio != null) _audio.Silence();
        }

        // ------------------------------------------------------------------ resolve

        static bool WeatherFreeScene(string scene)
        {
            return string.IsNullOrEmpty(scene) || scene == SceneNames.Bootstrap || scene == SceneNames.MainMenu
                || scene == SceneNames.ShipStarter || scene == SceneNames.TradeHubSol;
        }

        void Resolve()
        {
            var scene = SceneManager.GetActiveScene().name;
            var session = XenoarchRuntime.Session;
            string bodyId = "";
            CelestialBodyDefinition body = null;
            var cfg = session != null ? session.Config : null;
            if (!WeatherFreeScene(scene) && cfg != null && cfg.galaxy != null)
            {
                body = cfg.galaxy.FindBodyByScene(scene);
                if (body == null && session.Exploration != null) body = session.Exploration.CurrentBody;
                if (body != null) bodyId = body.Id;
            }
            string key = scene + "|" + bodyId;
            if (key == _bodyKey && Profile != null) return;
            if (key == _bodyKey && WeatherFreeScene(scene)) return;
            _bodyKey = key; _sceneName = scene;
            Profile = null;
            if (WeatherFreeScene(scene)) { CurrentName = "None"; return; }

            // gravity of this surface (particles and motes fall at it)
            var env = cfg != null ? EnvironmentDirector.Resolve(cfg, scene) : null;
            _gravity = env != null && env.gravity > 0.01f ? env.gravity : (body != null ? Mathf.Max(0.2f, body.gravityG * 9.81f) : 9.81f);

            Profile = _cat != null ? _cat.Find(bodyId) : null;
            if (Profile == null)
            {
                var row = WeatherDefaults.RowFor(bodyId);
                if (row != null) Profile = WeatherDefaults.Build(row);
                else
                {
                    var type = WeatherArchetype.Airless;
                    var rec = body != null ? ProceduralGalaxy.Recipe(body.Id) : null;
                    if (rec != null) type = WeatherDefaults.ArchetypeForPlanetType(rec.type.ToString());
                    else if (_cat != null && _cat.ForArchetype(WeatherArchetype.Airless) != null) type = WeatherArchetype.Airless;
                    Profile = (_cat != null ? _cat.ForArchetype(type) : null) ?? WeatherDefaults.BuildGeneric(bodyId, body != null ? body.displayName : scene, type, _gravity);
                }
            }
            StartProfile();
            Debug.Log($"[Xenoarch] Weather: '{Profile.displayName}' ({Profile.archetype}, {Profile.pressureKPa:0.#} kPa, g={_gravity:0.##}) for {scene}; starting '{CurrentName}'");
        }

        void StartProfile()
        {
            int seed = unchecked(_bodyKey.GetHashCode() * 31 + System.Environment.TickCount);
            _rng = new System.Random(seed);
            _inMajor = false; _calmElapsed = 0f; _calmNeeded = R(Profile.calmBetweenMajorMinutes.x, Profile.calmBetweenMajorMinutes.y);
            var first = Pick(false) ?? (Profile.presets.Count > 0 ? Profile.presets[0] : new WeatherPreset());
            if (!string.IsNullOrEmpty(_forcedName) && Profile.Find(_forcedName) != null) first = Profile.Find(_forcedName);
            if (WeatherSettings.Extreme && Profile.Strongest() != null) first = Profile.Strongest();
            Snap(first);
        }

        // ------------------------------------------------------------------ state machine

        void Snap(WeatherPreset p)
        {
            var s = p != null ? p.ToState() : default;
            _from = _to = _cur = s; _t = 1f; _toPreset = p; CurrentName = p != null ? p.name : "None";
            _phaseUntil = Time.time + (p != null ? R(p.minutes.x, p.minutes.y) * 60f : 300f);
            _inMajor = p != null && p.major;
        }

        void TransitionTo(WeatherPreset p, float seconds)
        {
            if (p == null) return;
            _from = _cur; _to = p.ToState(); _toPreset = p; _t = 0f;
            _dur = Mathf.Max(0.5f, seconds);
            CurrentName = p.name;
            _inMajor = p.major;
            float minutes = R(p.minutes.x, p.minutes.y);
            _phaseUntil = Time.time + _dur + minutes * 60f;
            Debug.Log($"[Xenoarch] Weather -> {p.name} for {minutes:0.#} min (+{_dur:0} s blend)");
        }

        WeatherPreset Pick(bool major, WeatherPreset avoid = null)
        {
            if (Profile == null) return null;
            float sum = 0f; var list = new List<WeatherPreset>();
            foreach (var p in Profile.presets)
                if (p != null && p.major == major && p.weight > 0f && p != avoid) { list.Add(p); sum += p.weight; }
            if (list.Count == 0) return null;
            float r = (float)_rng.NextDouble() * sum;
            foreach (var p in list) { r -= p.weight; if (r <= 0f) return p; }
            return list[list.Count - 1];
        }
        float R(float a, float b) => a + (float)_rng.NextDouble() * Mathf.Max(0f, b - a);

        void Schedule()
        {
            if (Profile == null || _t < 1f || Time.time < _phaseUntil) return;
            if (!string.IsNullOrEmpty(_forcedName) || WeatherSettings.Extreme) { _phaseUntil = Time.time + 60f; return; }
            WeatherPreset next;
            if (_inMajor) next = Pick(false, _toPreset);
            else
            {
                _calmElapsed += Mathf.Max(0f, (_toPreset != null ? (_toPreset.minutes.x + _toPreset.minutes.y) * 0.5f : 4f));
                bool wantMajor = _calmElapsed >= _calmNeeded && _rng.NextDouble() < Profile.majorChance;
                next = wantMajor ? Pick(true) : Pick(false, _toPreset);
                if (next != null && next.major) { _calmElapsed = 0f; _calmNeeded = R(Profile.calmBetweenMajorMinutes.x, Profile.calmBetweenMajorMinutes.y); }
            }
            if (next == null) next = Pick(false) ?? _toPreset;
            TransitionTo(next, Profile.transitionSeconds);
        }

        // ------------------------------------------------------------------ public test API

        /// <summary>Jump to a named state of the current world (blends over a few seconds). Empty name = back to the scheduler.</summary>
        public bool Force(string presetName, float seconds = 6f)
        {
            if (string.IsNullOrEmpty(presetName)) { _forcedName = null; _phaseUntil = 0f; return true; }
            if (Profile == null) return false;
            var p = Profile.Find(presetName);
            if (p == null) return false;
            _forcedName = p.name;
            TransitionTo(p, seconds);
            return true;
        }

        /// <summary>Cycles through the states of the current world (forces each in turn).</summary>
        public void NextWeather()
        {
            if (Profile == null || Profile.presets.Count == 0) return;
            int i = _toPreset != null ? Profile.presets.IndexOf(_toPreset) : -1;
            var p = Profile.presets[(i + 1) % Profile.presets.Count];
            _forcedName = p.name;
            TransitionTo(p, 5f);
        }

        public string Describe()
        {
            return $"{(Profile != null ? Profile.displayName : "-")}: {CurrentName} rain={_cur.rain:0.00} snow={_cur.snow:0.00} ash={_cur.ashfall:0.00} dust={_cur.dust:0.00} " +
                   $"wind={WindMS:0.0}m/s fog={_cur.fog:0.0} dark={_cur.darkness:0.00} strikes={Strikes} particles={(_particles != null ? _particles.LiveParticles : 0)} " +
                   $"intensity={WeatherSettings.Intensity:0.00}{(WeatherSettings.Extreme ? " EXTREME" : "")}{(WeatherSettings.Enabled ? "" : " OFF")}";
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            HandleHotkeys();
            float dt = Time.deltaTime;

            if (Time.unscaledTime >= _nextResolve) { _nextResolve = Time.unscaledTime + 1f; Resolve(); }

            var rt = XenoarchRuntime.Instance;
            bool loading = SceneLoader_Busy();
            bool sessionOk = rt != null && rt.SessionActive;
            if (Time.unscaledTime >= _nextCam || _cam == null || !_cam.gameObject.activeInHierarchy) { _nextCam = Time.unscaledTime + 1f; FindCamera(); }
            if (Time.unscaledTime >= _nextRover) { _nextRover = Time.unscaledTime + 0.5f; _roverOcc = RoverOccupied(); }

            Active = Profile != null && WeatherSettings.Enabled && sessionOk && !loading && _cam != null
                     && !WeatherFreeScene(_sceneName) && Time.unscaledTime >= _settleAt;

            if (Profile == null) { _particles.Tick(new WeatherParticles.Drive { active = false }, _cam); _audio.Tick(new WeatherAudio.Mix { active = false }); RestoreFog(); RestoreSun(); return; }

            // blend + schedule
            if (_t < 1f) { _t = Mathf.Min(1f, _t + dt / _dur); _cur = WeatherState.Lerp(_from, _to, Mathf.SmoothStep(0f, 1f, _t)); }
            if (Active) Schedule();

            float inten = WeatherSettings.Intensity * (WeatherSettings.Extreme ? 1.8f : 1f);
            var st = _cur;
            if (!Profile.HasAir) { st.rain = st.snow = st.ashfall = st.dust = st.devils = 0f; st.wind = 0f; st.fog = 0f; st.thunderPerMinute = 0f; }
            float motesBase = Profile.HasAir ? 0f : Mathf.Max(Profile.baselineMotes, st.motes);
            if (WeatherSettings.Extreme && !Profile.HasAir) motesBase = Mathf.Max(motesBase, 1f);

            // wind + gusts (ASH-style shaped noise)
            _windAngle += Mathf.Sin(Time.time * 0.013f + _windSeed) * 6f * dt;
            float a = _windAngle * Mathf.Deg2Rad;
            WindDirection = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            float n = Mathf.PerlinNoise(Time.time * Profile.gustFrequency, _windSeed);
            Gust01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.95f, n)) * Profile.gustiness;
            float windAmt = Mathf.Clamp01(st.wind * Mathf.Min(inten, 1.5f) + (WeatherSettings.Extreme ? 0.3f : 0f)) * (Profile.HasAir ? 1f : 0f);
            WindMS = windAmt * Profile.windSpeedMax * (1f + Gust01 * 1.4f);

            float act = Active ? 1f : 0f;
            float d01 = Profile.Density01;
            var drive = new WeatherParticles.Drive
            {
                active = Active,
                rain = st.rain * inten, snow = st.snow * inten, ash = st.ashfall * inten, dust = st.dust * inten,
                devils = st.devils * Mathf.Min(inten, 1.5f), motes = motesBase * inten, embers = Profile.emberShare * 35f,
                wind = new Vector3(WindDirection.x, 0f, WindDirection.y) * WindMS, gravity = _gravity, density01 = d01,
                size = Mathf.Max(0.3f, st.particleSize), extreme = WeatherSettings.Extreme,
                rainColor = Profile.rainColor, snowColor = Profile.snowColor, ashColor = Profile.ashColor, dustColor = Profile.dustColor,
            };
            _particles.Tick(drive, _cam);

            _audio.Tick(new WeatherAudio.Mix
            {
                active = Active, hasAir = Profile.HasAir, density01 = d01, intensity = inten,
                wind = windAmt, gust01 = Gust01, rain = st.rain, dust = st.dust, ash = st.ashfall, snow = st.snow, motes = motesBase,
                windGain = Profile.windVolume, windPitch = Profile.windPitch, windLowPassHz = Profile.windLowPassHz, rainGain = Profile.rainVolume,
                staticGain = Profile.staticVolume, muffle = _roverOcc ? 1f : 0f,
            });

            if (Active)
            {
                Lightning(st, inten, dt);
                ApplyFogAndSun(st, inten);
            }
            else { RestoreFog(); RestoreSun(); }
            _ = act;
        }

        static bool SceneLoader_Busy() { var l = XenoarchRuntime.Scenes; return l != null && l.IsLoading; }

        void FindCamera()
        {
            var c = Camera.main;
            if (c == null || !c.isActiveAndEnabled)
            {
                var rig = PlayerRigLocator.Find();
                c = rig != null ? rig.ViewCamera : null;
            }
            _cam = c != null ? c.transform : null;
        }

        static bool RoverOccupied()
        {
            foreach (var r in FindObjectsByType<RoverVehicle>()) if (r != null && r.Occupied) return true;
            return false;
        }

        // ------------------------------------------------------------------ lightning

        void Lightning(WeatherState st, float inten, float dt)
        {
            float perMin = st.thunderPerMinute * Mathf.Min(inten, 2f);
            if (perMin > 0.01f && Random.value < perMin / 60f * dt) Strike();
            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - dt * 3.2f);
            ApplyFlash();
        }

        public void Strike()
        {
            if (!Active) return;
            float dist = Random.Range(250f, 2500f);
            _flash = Mathf.Clamp01(1.25f - dist / 3200f); Strikes++;
            if (_audio != null) _audio.Thunder(dist, 0.9f);
        }

        void ApplyFlash()
        {
            if (!_ambCaptured) return;
            if (_ambMode == AmbientMode.Flat) RenderSettings.ambientLight = _ambBaseColor + Color.white * (_flash * _flash * 1.2f);
            else RenderSettings.ambientIntensity = _ambBase * (1f + _flash * _flash * 4f);
        }

        // ------------------------------------------------------------------ fog + sun

        void ApplyFogAndSun(WeatherState st, float inten)
        {
            if (!_fogCaptured)
            {
                _fogCaptured = true; _baseFog = RenderSettings.fog; _baseFogDensity = RenderSettings.fogDensity; _baseFogColor = RenderSettings.fogColor; _baseFogMode = RenderSettings.fogMode;
            }
            if (!_ambCaptured)
            {
                _ambCaptured = true; _ambMode = RenderSettings.ambientMode; _ambBase = RenderSettings.ambientIntensity; _ambBaseColor = RenderSettings.ambientLight;
            }
            float add = st.fog * Mathf.Min(inten, 1.6f) * 0.0015f;
            if (add > 0.00005f)
            {
                RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = (_baseFog ? _baseFogDensity : 0f) + add;
                float k = Mathf.Clamp01(st.fog / 4f);
                var from = _baseFog ? _baseFogColor : Profile.fogTint;
                RenderSettings.fogColor = Color.Lerp(from, Profile.fogTint, k);
            }
            else RestoreFog();

            // sun dimming (relative to whatever the sun was set to by the sun director)
            if (_sun == null) { var sd = SunDirector.Instance; _sun = sd != null && sd.SunLight != null ? sd.SunLight : RenderSettings.sun; _sunLast = -1f; }
            if (_sun != null)
            {
                float cur = _sun.intensity;
                if (_sunLast < 0f || Mathf.Abs(cur - _sunLast) > 0.01f * Mathf.Max(0.05f, _sunLast)) _sunBase = cur; // someone else set it: new base
                _sunMul = Mathf.Lerp(_sunMul, 1f - 0.7f * Mathf.Clamp01(st.darkness * Mathf.Min(inten, 1.4f)), Time.deltaTime * 2f);
                _sun.intensity = _sunBase * _sunMul; _sunLast = _sun.intensity;
            }
        }

        void RestoreFog()
        {
            if (!_fogCaptured) return;
            RenderSettings.fog = _baseFog; RenderSettings.fogDensity = _baseFogDensity; RenderSettings.fogColor = _baseFogColor; RenderSettings.fogMode = _baseFogMode;
        }

        void RestoreSun()
        {
            if (_sun != null && _sunLast >= 0f && Mathf.Abs(_sun.intensity - _sunLast) < 0.001f) { _sun.intensity = _sunBase; _sunLast = -1f; _sunMul = 1f; }
        }

        // ------------------------------------------------------------------ hotkeys

        void HandleHotkeys()
        {
            var cfg = XenoarchRuntime.Session != null ? XenoarchRuntime.Session.Config : null;
            if (cfg != null && !cfg.enableDebugHotkeys) return;
            var kb = Keyboard.current;
            if (kb == null || !kb.ctrlKey.isPressed || !kb.shiftKey.isPressed) return;
            if (kb.wKey.wasPressedThisFrame) { NextWeather(); Debug.Log("[Xenoarch] Weather: " + Describe()); }
            if (kb.eKey.wasPressedThisFrame) { WeatherSettings.Extreme = !WeatherSettings.Extreme; if (!WeatherSettings.Extreme) _phaseUntil = 0f; else if (Profile != null && Profile.Strongest() != null) TransitionTo(Profile.Strongest(), 4f); Debug.Log("[Xenoarch] Weather extreme preset: " + WeatherSettings.Extreme); }
            if (kb.qKey.wasPressedThisFrame) { WeatherSettings.Enabled = !WeatherSettings.Enabled; Debug.Log("[Xenoarch] Weather enabled: " + WeatherSettings.Enabled); }
            if (kb.equalsKey.wasPressedThisFrame) { WeatherSettings.Intensity += 0.25f; Debug.Log("[Xenoarch] Weather intensity x" + WeatherSettings.Intensity.ToString("0.00")); }
            if (kb.minusKey.wasPressedThisFrame) { WeatherSettings.Intensity -= 0.25f; Debug.Log("[Xenoarch] Weather intensity x" + WeatherSettings.Intensity.ToString("0.00")); }
            if (kb.rKey.wasPressedThisFrame) { Force(null); Debug.Log("[Xenoarch] Weather back to the scheduler"); }
            if (kb.digit0Key.wasPressedThisFrame) { WeatherSettings.ResetToDefaults(); Debug.Log("[Xenoarch] Weather test settings reset"); }
        }
    }
}
