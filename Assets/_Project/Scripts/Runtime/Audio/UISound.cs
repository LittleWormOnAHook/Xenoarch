using System.Collections.Generic;
using DMI.Xenoarch.Scenes;
using DMI.Xenoarch.UI;
using UnityEngine;

namespace DMI.Xenoarch.Audio
{
    /// <summary>
    /// Central 2D UI sound player: a small AudioSource pool, per-event volume and pitch jitter, hover cooldowns,
    /// a master UI volume (<see cref="GameSettings.UiVolume"/>, under the existing master AudioListener volume),
    /// and silence during loading screens.
    /// </summary>
    public sealed class UISound : MonoBehaviour
    {
        const int PoolSize = 8;
        const string HostName = "[Xenoarch UISound]";

        static UISound _i;
        static UISoundCatalog _catalog;
        static bool _looked;
        readonly List<AudioSource> _pool = new List<AudioSource>(PoolSize);
        readonly Dictionary<int, float> _nextOk = new Dictionary<int, float>(16);
        int _cursor;
        object _hoverTarget;
        float _hoverUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _i = null;
            _catalog = null;
            _looked = false;
        }

        public static UISoundCatalog Catalog
        {
            get
            {
                if (!_looked)
                {
                    _looked = true;
                    var stub = Resources.Load<UISoundCatalogStub>(UISoundCatalogStub.ResourcesPath);
                    _catalog = stub != null ? stub.ui : null;
                    if (_catalog == null)
                        _catalog = Resources.Load<UISoundCatalog>("Audio/UISoundCatalog");
                }
                return _catalog;
            }
        }

        public static float MasterVolume =>
            (Catalog != null ? Catalog.volume : 1f) * GameSettings.UiVolume;

        public static void Play(UISoundEvent evt) => Instance()?.PlayNow(evt, 1f);

        public static void Play(UISoundEvent evt, float volumeScale) => Instance()?.PlayNow(evt, volumeScale);

        public static bool AllowHover(object target)
        {
            var i = Instance();
            if (i == null) return true;
            float now = Time.unscaledTime;
            if (ReferenceEquals(i._hoverTarget, target) && now < i._hoverUntil) return false;
            i._hoverTarget = target;
            i._hoverUntil = now + 0.12f;
            return true;
        }

        public static void ClearHover(object target)
        {
            if (_i != null && ReferenceEquals(_i._hoverTarget, target)) _i._hoverTarget = null;
        }

        static UISound Instance()
        {
            if (!Application.isPlaying) return null;
            if (_i != null) return _i;
            var go = GameObject.Find(HostName);
            if (go == null)
            {
                go = new GameObject(HostName);
                DontDestroyOnLoad(go);
            }
            _i = go.GetComponent<UISound>();
            if (_i == null) _i = go.AddComponent<UISound>();
            return _i;
        }

        void Awake()
        {
            if (_i != null && _i != this) { Destroy(this); return; }
            _i = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
            EnsurePool();
        }

        void EnsurePool()
        {
            while (_pool.Count < PoolSize)
            {
                var child = new GameObject("ui-sfx-" + _pool.Count);
                child.transform.SetParent(transform, false);
                var s = child.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.loop = false;
                s.dopplerLevel = 0f;
                s.ignoreListenerPause = true;
                s.priority = 64;
                _pool.Add(s);
            }
        }

        void PlayNow(UISoundEvent evt, float volumeScale)
        {
            var cat = Catalog;
            if (cat != null && cat.muteWhileLoading && SceneLoader.Busy) return;
            var entry = cat != null ? cat.Get(evt) : null;
            var clip = cat != null ? cat.Pick(evt) : null;
            if (clip == null) return;

            float now = Time.unscaledTime;
            float cd = entry != null && entry.cooldown > 0f ? entry.cooldown : DefaultCooldown(evt);
            int key = (int)evt;
            if (cd > 0f && _nextOk.TryGetValue(key, out var until) && now < until) return;
            if (cd > 0f) _nextOk[key] = now + cd;

            EnsurePool();
            var src = NextFree();
            float vol = MasterVolume * (entry != null ? entry.volume : 1f) * volumeScale;
            if (vol <= 0.0001f) return;
            var pitch = entry != null ? entry.pitch : new Vector2(0.97f, 1.03f);
            src.pitch = pitch.x >= pitch.y ? pitch.x : Random.Range(pitch.x, pitch.y);
            src.PlayOneShot(clip, Mathf.Clamp01(vol));
        }

        AudioSource NextFree()
        {
            for (int n = 0; n < _pool.Count; n++)
            {
                _cursor = (_cursor + 1) % _pool.Count;
                if (!_pool[_cursor].isPlaying) return _pool[_cursor];
            }
            _cursor = (_cursor + 1) % _pool.Count;
            return _pool[_cursor];
        }

        static float DefaultCooldown(UISoundEvent evt)
        {
            switch (evt)
            {
                case UISoundEvent.Hover: return 0.1f;
                case UISoundEvent.Slider: return 0.05f;
                case UISoundEvent.Typing: return 0.045f;
                case UISoundEvent.Click: return 0.04f;
                default: return 0f;
            }
        }
    }
}
