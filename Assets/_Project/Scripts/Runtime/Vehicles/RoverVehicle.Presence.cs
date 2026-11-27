using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DMI.Xenoarch.Vehicles
{
    /// <summary>
    /// Runtime-only cabin presence (t227u): parked / just-deployed starts with every rover AudioSource, Light, and
    /// emissive material off; entering turns them back on, exiting turns them off again. Uses MaterialPropertyBlocks
    /// and AudioSource/Light enable so the Grasshopper prefab and shared materials are never written.
    /// </summary>
    public sealed partial class RoverVehicle
    {
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissiveId = Shader.PropertyToID("_EmissiveColor");
        static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");

        struct AudioSlot
        {
            public AudioSource src;
            public bool playOnAwake;
            public bool loop;
            public bool wasEnabled;
        }

        struct LightSlot
        {
            public Light light;
            public bool wasEnabled;
            public float intensity;
        }

        struct EmitSlot
        {
            public Renderer renderer;
            public int index;
            public int propertyId;
            public Color color;
        }

        readonly List<AudioSlot> _presenceAudio = new List<AudioSlot>();
        readonly List<LightSlot> _presenceLights = new List<LightSlot>();
        readonly List<EmitSlot> _presenceEmit = new List<EmitSlot>();
        readonly MaterialPropertyBlock _presenceBlock = new MaterialPropertyBlock();
        bool _presenceInit;
        bool _presenceOn;

        void ApplyPresence(bool on)
        {
            if (!Live) return;
            EnsurePresence();
            _presenceOn = on;
            for (int i = 0; i < _presenceAudio.Count; i++)
            {
                var a = _presenceAudio[i];
                if (a.src == null) continue;
                if (on)
                {
                    a.src.enabled = a.wasEnabled;
                    if (a.loop || a.playOnAwake) a.src.Play();
                }
                else
                {
                    a.src.Stop();
                    a.src.enabled = false;
                }
            }
            for (int i = 0; i < _presenceLights.Count; i++)
            {
                var l = _presenceLights[i];
                if (l.light == null) continue;
                if (on)
                {
                    l.light.enabled = l.wasEnabled;
                    l.light.intensity = l.intensity;
                }
                else
                {
                    l.light.enabled = false;
                }
            }
            for (int i = 0; i < _presenceEmit.Count; i++)
            {
                var e = _presenceEmit[i];
                if (e.renderer == null) continue;
                e.renderer.GetPropertyBlock(_presenceBlock, e.index);
                _presenceBlock.SetColor(e.propertyId, on ? e.color : Color.black);
                e.renderer.SetPropertyBlock(_presenceBlock, e.index);
            }
        }

        /// <summary>While parked, keep loops and lights off even if a prefab behaviour tries to restart them.</summary>
        void TickPresence()
        {
            if (_presenceOn || !_presenceInit) return;
            for (int i = 0; i < _presenceAudio.Count; i++)
            {
                var src = _presenceAudio[i].src;
                if (src == null) continue;
                if (src.isPlaying) src.Stop();
                if (src.enabled) src.enabled = false;
            }
            for (int i = 0; i < _presenceLights.Count; i++)
            {
                var l = _presenceLights[i].light;
                if (l != null && l.enabled) l.enabled = false;
            }
        }

        void EnsurePresence()
        {
            if (_presenceInit) return;
            _presenceInit = true;
            _presenceAudio.Clear();
            _presenceLights.Clear();
            _presenceEmit.Clear();

            var audios = GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audios.Length; i++)
            {
                var src = audios[i];
                if (src == null || IsPresenceExcluded(src.transform, true)) continue;
                _presenceAudio.Add(new AudioSlot
                {
                    src = src,
                    playOnAwake = src.playOnAwake,
                    loop = src.loop,
                    wasEnabled = src.enabled
                });
            }

            var lights = GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null || IsPresenceExcluded(l.transform, false)) continue;
                _presenceLights.Add(new LightSlot { light = l, wasEnabled = l.enabled, intensity = l.intensity });
            }

            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || IsPresenceExcluded(r.transform, false)) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null) continue;
                    if (TryEmission(mat, out int id, out var col))
                        _presenceEmit.Add(new EmitSlot { renderer = r, index = m, propertyId = id, color = col });
                }
            }
        }

        bool IsPresenceExcluded(Transform t, bool audio)
        {
            if (t == null) return true;
            // Never dim or mute the seated player if they are parented under the rover while boarding.
            if (_player != null && t.IsChildOf(_player.transform)) return true;
            // ConsolePower owns cockpit display emission and console lights. Audio on the rover, including
            // loops parented under the console, is still stopped and restored here (t227u: all rover audio).
            if (!audio && console != null && t != console && t.IsChildOf(console)) return true;
            return false;
        }

        static bool TryEmission(Material mat, out int propertyId, out Color color)
        {
            propertyId = 0;
            color = Color.black;
            if (mat.HasProperty(EmissionId))
            {
                var c = mat.GetColor(EmissionId);
                if (c.maxColorComponent > 0.002f || (mat.HasProperty(EmissionMapId) && mat.GetTexture(EmissionMapId) != null) || mat.IsKeywordEnabled("_EMISSION"))
                {
                    propertyId = EmissionId;
                    color = c;
                    return c.maxColorComponent > 0.002f || mat.IsKeywordEnabled("_EMISSION") || (mat.HasProperty(EmissionMapId) && mat.GetTexture(EmissionMapId) != null);
                }
            }
            if (mat.HasProperty(EmissiveId))
            {
                var c = mat.GetColor(EmissiveId);
                if (c.maxColorComponent > 0.002f)
                {
                    propertyId = EmissiveId;
                    color = c;
                    return true;
                }
            }
            return false;
        }
    }
}
