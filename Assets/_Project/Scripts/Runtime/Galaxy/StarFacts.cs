using System.Collections.Generic;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// Real published data for the nearby Nav-dropdown systems (t92u), shown in the Nav terminal's right panel and
    /// drawn as non-clickable planets on their system maps. Sources (checked 2026-11-25): NASA Exoplanet Archive
    /// overview pages (Proxima Cen, Barnard's star, eps Eri), Basant et al. 2025, Suárez Mascareño et al. 2025,
    /// Mawet et al. 2019. Planet masses from radial velocity are minimum masses (m sin i).
    /// </summary>
    public static class StarFacts
    {
        public sealed class Planet
        {
            public string name, status, mass, period, note; public float au;
            public Planet(string name, string status, float au, string mass, string period, string note = null)
            { this.name = name; this.status = status; this.au = au; this.mass = mass; this.period = period; this.note = note; }
            public bool Candidate => status != "confirmed";
        }

        public sealed class Facts
        {
            public string type, distance, mass, radius, temperature, age, summary;
            public Planet[] planets;
        }

        static readonly Dictionary<string, Facts> Table = new Dictionary<string, Facts>
        {
            ["sys.proxima_centauri"] = new Facts
            {
                type = "M5.5Ve red dwarf (flare star)", distance = "4.25 ly", mass = "0.12 solar masses",
                radius = "0.14 solar radii", temperature = "about 2,900-3,050 K", age = "about 4.85 billion years",
                summary = "The closest star to the Sun, a small, faint flare star loosely bound to Alpha Centauri A and B.",
                planets = new[]
                {
                    new Planet("Proxima d", "confirmed", 0.029f, "at least 0.26 Earth masses", "5.12 days"),
                    new Planet("Proxima b", "confirmed", 0.049f, "at least 1.06 Earth masses", "11.2 days", "in the habitable zone"),
                    new Planet("Proxima c", "candidate", 1.49f, "at least 5.8 Earth masses", "about 1,900 days", "candidate, disputed"),
                },
            },
            ["sys.barnards_star"] = new Facts
            {
                type = "M4V red dwarf", distance = "5.96 ly", mass = "0.16 solar masses",
                radius = "0.19 solar radii", temperature = "about 3,200 K", age = "about 7-10 billion years",
                summary = "The closest single star to the Sun and the fastest-moving star across our sky (10.4 arcsec per year).",
                planets = new[]
                {
                    new Planet("Barnard d", "confirmed", 0.0188f, "at least 0.26 Earth masses", "2.34 days"),
                    new Planet("Barnard b", "confirmed", 0.0229f, "at least 0.30 Earth masses", "3.15 days"),
                    new Planet("Barnard c", "confirmed", 0.0274f, "at least 0.34 Earth masses", "4.12 days"),
                    new Planet("Barnard e", "confirmed", 0.0381f, "at least 0.19 Earth masses", "6.74 days"),
                },
            },
            ["sys.epsilon_eridani"] = new Facts
            {
                type = "K2V orange dwarf", distance = "10.5 ly", mass = "0.82 solar masses",
                radius = "0.74-0.77 solar radii", temperature = "about 5,100 K", age = "about 400-800 million years",
                summary = "A young, active Sun-like star with two asteroid belts and an outer dust ring.",
                planets = new[]
                {
                    new Planet("Epsilon Eridani b (AEgir)", "confirmed", 3.5f, "about 0.7-1.0 Jupiter masses", "about 7.4 years"),
                    new Planet("Epsilon Eridani c", "candidate", 40f, "unknown", "unknown", "candidate, inferred from the dust ring"),
                },
            },
        };

        public static Facts For(string systemId) => systemId != null && Table.TryGetValue(systemId, out var f) ? f : null;

        /// <summary>Info lines for the Nav right panel.</summary>
        public static List<string> Lines(Facts f)
        {
            var l = new List<string>
            {
                $"Star: {f.type}", $"Distance: {f.distance}", $"Mass: {f.mass}", $"Radius: {f.radius}",
                $"Temperature: {f.temperature}", $"Age: {f.age}",
            };
            int conf = 0; foreach (var p in f.planets) if (!p.Candidate) conf++;
            l.Add($"Known planets: {conf} confirmed" + (conf < f.planets.Length ? $", {f.planets.Length - conf} candidate" : ""));
            foreach (var p in f.planets)
                l.Add($"  {p.name}{(p.Candidate ? " (CANDIDATE)" : "")}: {p.mass}, orbit {p.period}" + (p.note != null && !p.Candidate ? $", {p.note}" : p.note != null ? $"; {p.note}" : ""));
            return l;
        }
    }
}
