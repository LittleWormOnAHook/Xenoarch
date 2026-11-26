using System;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// ZEN-17: how the orbit view (planet outside the ship window) varies per arrival and moves while parked.
    /// On each arrival (FTL or in-system travel) a saved seed picks a small offset from the view's authored framing,
    /// a slightly different apparent size, a different visible face and a different sun angle (terminator). While
    /// parked, the planet turns (from the body's real rotation period, sped up) and the planet and sun drift slowly.
    /// Everything stays within the clamp below so the planet stays framed in the window. config.orbitViewMotion.
    /// </summary>
    [Serializable]
    public sealed class OrbitViewMotionSettings
    {
        public bool enabled = true;
        [Header("Per arrival (seeded, saved)")]
        [Tooltip("Random yaw / pitch (deg, +/-) added to the view's windowOffsetDeg.")] public Vector2 placementJitterDeg = new Vector2(8f, 5f);
        [Tooltip("Random apparent size change (fraction, +/-).")] [Range(0f, 0.4f)] public float sizeJitter = 0.12f;
        [Tooltip("Random sun yaw / pitch (deg, +/-) added to the ship sun offset: moves the day/night line.")] public Vector2 sunJitterDeg = new Vector2(18f, 6f);
        [Tooltip("Random starting longitude (deg, 0..this): a different face of the planet.")] [Range(0f, 360f)] public float faceRandomDeg = 360f;
        [Tooltip("Random axial tilt change (deg, +/-).")] public float tiltJitterDeg = 6f;

        [Header("Real-time while parked")]
        [Tooltip("Planet spin = 360 / (body rotation period) x this. Bodies without a period use the view's spinDegPerSec.")] public float spinSpeedup = 600f;
        [Tooltip("Spin limits (deg/sec) so very slow / fast rotators stay visible but calm.")] public Vector2 spinClampDegPerSec = new Vector2(0.01f, 0.4f);
        [Tooltip("Orbital drift of the planet in the window: amplitude yaw / pitch (deg).")] public Vector2 driftDeg = new Vector2(3f, 1.5f);
        [Tooltip("Seconds for one full drift cycle.")] public float driftPeriodSec = 900f;
        [Tooltip("Sun drift amplitude yaw / pitch (deg); the light, disc and flare follow.")] public Vector2 sunDriftDeg = new Vector2(5f, 2f);

        [Header("Framing")]
        [Tooltip("Final offset from the window direction never exceeds the authored offset length + this (deg), so it stays in the window.")]
        public float maxExtraOffsetDeg = 8f;
        [Tooltip("Hard limit for the planet's offset from the window direction (deg).")] public float maxOffsetDeg = 45f;
    }
}
