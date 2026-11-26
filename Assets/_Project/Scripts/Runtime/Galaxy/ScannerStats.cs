using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Galaxy
{
    /// <summary>
    /// Upgradable scan radii. Handheld: <see cref="SurfaceScannerDefinition.rangeByLevel"/>; orbital:
    /// <see cref="ShipDefinition.orbitalScanRadiusByLevel"/>; levels live in <see cref="ShipState"/> (saved). The
    /// upgrade calls are what crafting / the ship hold will use (Batch E).
    /// </summary>
    public static class ScannerStats
    {
        public static float Pick(float[] table, int level, float fallback)
        {
            if (table == null || table.Length == 0) return fallback;
            return table[Mathf.Clamp(level, 0, table.Length - 1)];
        }

        /// <summary>Handheld scanner level: upgrades + installed alien tech (Phase 1D).</summary>
        public static int HandheldLevel(GameSession s) => s == null ? 0 : s.Exploration.Ship.handheldScannerLevel + s.Artifacts.TechBonus(DMI.Xenoarch.Artifacts.TechInstallTarget.HandheldScanner);

        /// <summary>Like <see cref="Pick"/>, but levels past the table end add 15% of the last entry each (installed tech on a maxed scanner still helps).</summary>
        public static float PickExtended(float[] table, int level, float fallback)
        {
            if (table == null || table.Length == 0) return fallback * (1f + 0.15f * Mathf.Max(0, level));
            int over = level - (table.Length - 1);
            return over > 0 ? table[table.Length - 1] * (1f + 0.15f * over) : table[Mathf.Max(0, level)];
        }

        public static float HandheldRange()
        {
            var s = XenoarchRuntime.Session; var d = s?.Config.surfaceScanner;
            if (d == null) return 30f;
            return Mathf.Max(d.range, PickExtended(d.rangeByLevel, HandheldLevel(s), d.range));
        }

        /// <summary>Accuracy grows with the handheld scanner level (+0.06 per level, max 0.95), so range upgrades also
        /// tighten readings.</summary>
        public static float HandheldAccuracy()
        {
            var s = XenoarchRuntime.Session; var d = s?.Config.surfaceScanner;
            if (d == null) return 0.7f;
            return Mathf.Min(0.95f, d.accuracy + 0.06f * HandheldLevel(s));
        }

        /// <summary>Max detectable depth grows by 0.5 m per handheld scanner level.</summary>
        public static float HandheldMaxDepth()
        {
            var s = XenoarchRuntime.Session; var d = s?.Config.surfaceScanner;
            if (d == null) return 3f;
            return d.maxDepth + 0.5f * HandheldLevel(s);
        }

        public static int MaxHandheldLevel { get { var d = XenoarchRuntime.Session?.Config.surfaceScanner; return d != null && d.rangeByLevel != null ? Mathf.Max(0, d.rangeByLevel.Length - 1) : 0; } }
        public static int MaxOrbitalLevel { get { var d = XenoarchRuntime.Session?.Config.starterShip; return d != null && d.orbitalScanRadiusByLevel != null ? Mathf.Max(0, d.orbitalScanRadiusByLevel.Length - 1) : 0; } }

        public static bool UpgradeHandheld()
        {
            var x = XenoarchRuntime.Session?.Exploration;
            if (x == null || x.Ship.handheldScannerLevel >= MaxHandheldLevel) return false;
            x.Ship.handheldScannerLevel++; return true;
        }

        public static bool UpgradeOrbital()
        {
            var x = XenoarchRuntime.Session?.Exploration;
            if (x == null || x.Ship.orbitalRangeLevel >= MaxOrbitalLevel) return false;
            x.Ship.orbitalRangeLevel++; return true;
        }
    }
}
