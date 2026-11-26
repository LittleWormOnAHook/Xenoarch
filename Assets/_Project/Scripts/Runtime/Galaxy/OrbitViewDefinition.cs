using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// A planet seen from orbit through the ship window: a lat-long textured sphere (PlanetPack02 style, URP shaders
    /// Xenoarch/Space/Planet and Xenoarch/Space/PlanetAtmosphere) at a given apparent size and window offset. Data only;
    /// <see cref="OrbitViewDirector"/> builds it at runtime. Later real orbiting / travel can drive the same materials.
    /// </summary>
    [CreateAssetMenu(menuName = "Xenoarch/Galaxy/Orbit View", fileName = "OrbitView_")]
    public sealed class OrbitViewDefinition : ScriptableObject
    {
        public Material planetMaterial;
        [Tooltip("Optional additive atmosphere shell (slightly larger sphere).")] public Material atmosphereMaterial;
        [Range(1f, 1.2f)] public float atmosphereScale = 1.035f;
        [Tooltip("Apparent diameter of the planet in degrees as seen from the cockpit (low orbit = big).")] [Range(2f, 120f)] public float apparentDiameterDeg = 55f;
        [Tooltip("Where it sits relative to the helm window direction: x = yaw (deg, + right), y = pitch (deg, + up).")] public Vector2 windowOffsetDeg = new Vector2(-18f, -32f);
        [Tooltip("Axial tilt (deg) and spin (deg/sec) of the sphere.")] public float tiltDeg = 18f;
        public float spinDegPerSec = 0.15f;
    }
}
