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

        public static float HandheldRange()
        {
            var s = XenoarchRuntime.Session; var d = s?.Config.surfaceScanner;
            if (d == null) return 30f;
            return Mathf.Max(d.range, Pick(d.rangeByLevel, s.Exploration.Ship.handheldScannerLevel, d.range));
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
