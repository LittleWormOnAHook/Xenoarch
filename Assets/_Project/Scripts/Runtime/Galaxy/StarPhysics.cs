using System.Collections.Generic;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// Physical star numbers for the sun seen from a location: radius and luminosity (solar units) per system, the
    /// player's distance from the star (AU) and from that the sun's apparent angular diameter and the light flux.
    /// Earth / the Moon (1 AU, R = 1): 0.533 degrees, flux 1. Mars (1.52 AU): 0.35 degrees, flux 0.43.
    /// </summary>
    public static class StarPhysics
    {
        public const float SolarRadiusKm = 695700f;
        public const float AuKm = 149597870.7f;

        // radius (R_sun), luminosity (L_sun). Sources: NASA / ESO / the StarFacts table (Proxima 0.154, Barnard 0.196 etc.).
        static readonly Dictionary<string, Vector2> Table = new Dictionary<string, Vector2>
        {
            ["sys.sol"] = new Vector2(1f, 1f),
            ["sys.proxima_centauri"] = new Vector2(0.154f, 0.0017f),
            ["sys.barnards_star"] = new Vector2(0.196f, 0.0035f),
            ["sys.epsilon_eridani"] = new Vector2(0.735f, 0.34f),
            ["sys.alpha_centauri"] = new Vector2(1.22f, 1.52f),
            ["sys.sirius"] = new Vector2(1.71f, 25.4f),
            ["sys.tau_ceti"] = new Vector2(0.79f, 0.52f),
        };

        /// <summary>Star radius in solar radii: the system's own value if set, else the table, else 1.</summary>
        public static float RadiusSolar(StarSystemDefinition sys)
        {
            if (sys == null) return 1f;
            if (sys.starRadiusSolar > 0f) return sys.starRadiusSolar;
            return Table.TryGetValue(sys.Id ?? "", out var v) ? v.x : 1f;
        }

        /// <summary>Star luminosity in solar luminosities: the system's own value if set, else the table, else 1.</summary>
        public static float LuminositySolar(StarSystemDefinition sys)
        {
            if (sys == null) return 1f;
            if (sys.starLuminositySolar > 0f) return sys.starLuminositySolar;
            return Table.TryGetValue(sys.Id ?? "", out var v) ? v.y : 1f;
        }

        /// <summary>Approximate star colour from the spectral class letter (O..M).</summary>
        public static Color StarColor(StarSystemDefinition sys)
        {
            string c = sys != null && !string.IsNullOrEmpty(sys.stellarClass) ? sys.stellarClass.Trim().ToUpperInvariant() : "G";
            switch (c[0])
            {
                case 'O': return new Color(0.70f, 0.80f, 1f);
                case 'B': return new Color(0.78f, 0.86f, 1f);
                case 'A': return new Color(0.92f, 0.95f, 1f);
                case 'F': return new Color(1f, 0.98f, 0.94f);
                case 'K': return new Color(1f, 0.80f, 0.58f);
                case 'M': return new Color(1f, 0.62f, 0.38f);
                default: return new Color(1f, 0.96f, 0.90f); // G
            }
        }

        /// <summary>Distance (AU) of a body from its star: moons and stations use their parent planet's orbit.
        /// No body (open space in a system): the distance where the flux equals Earth's (sqrt(L) AU).</summary>
        public static float DistanceFromStarAU(GalaxyDefinition galaxy, CelestialBodyDefinition body, StarSystemDefinition sys)
        {
            if (body != null)
            {
                var b = body;
                for (int guard = 0; guard < 4 && b != null && !string.IsNullOrEmpty(b.parentId); guard++)
                {
                    var p = galaxy != null ? galaxy.FindBody(b.parentId) : null;
                    if (p == null) break;
                    b = p;
                }
                if (b != null && b.kind != CelestialKind.Star && b.orbitAU > 0f) return b.orbitAU;
            }
            return Mathf.Max(0.02f, Mathf.Sqrt(LuminositySolar(sys)));
        }

        /// <summary>Apparent angular diameter (degrees) of a star of the given radius seen from the given distance.</summary>
        public static float AngularDiameterDeg(float radiusSolar, float distanceAU)
        {
            float r = radiusSolar * SolarRadiusKm, d = Mathf.Max(0.001f, distanceAU) * AuKm;
            return 2f * Mathf.Atan(r / d) * Mathf.Rad2Deg;
        }

        /// <summary>Light flux relative to Earth's (L / d^2).</summary>
        public static float Flux(float luminositySolar, float distanceAU) => luminositySolar / Mathf.Max(1e-4f, distanceAU * distanceAU);

        public struct SunView
        {
            public StarSystemDefinition system;
            public float distanceAU, radiusSolar, luminositySolar, angularDiameterDeg, flux;
            public Color color;
        }

        public static SunView For(GalaxyDefinition galaxy, CelestialBodyDefinition body, StarSystemDefinition sys)
        {
            if (sys == null && galaxy != null && body != null) sys = galaxy.SystemOf(body.Id);
            var v = new SunView { system = sys, radiusSolar = RadiusSolar(sys), luminositySolar = LuminositySolar(sys) };
            v.distanceAU = DistanceFromStarAU(galaxy, body, sys);
            v.angularDiameterDeg = AngularDiameterDeg(v.radiusSolar, v.distanceAU);
            v.flux = Flux(v.luminositySolar, v.distanceAU);
            v.color = StarColor(sys);
            return v;
        }
    }
}
