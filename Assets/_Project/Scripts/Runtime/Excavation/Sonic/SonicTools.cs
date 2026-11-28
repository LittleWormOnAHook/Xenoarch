using DMI.Xenoarch.Core;
using UnityEngine;

namespace DMI.Xenoarch.Excavation
{
    public enum SonicToolKind { None, Excavator, Brush, Burster }
    public enum SonicHead { Sphere, Disc, Cube, Cone }
    public enum SonicPower { Low, Med, High }
    public enum SonicFocus { Wide, Mid, Tight, Fine }

    /// <summary>
    /// Sonic dig tools (Docs/Design/SonicDigTools.md): the shovel item is the Sonic Excavator (head shape, size, reach,
    /// Power and Focus dials; Focus Fine is the old precision tool), the brush is the Ultrasonic Brush. This holds the
    /// session dial settings, turns them into the effective <see cref="DigToolSpec"/> for a stroke, and runs the shared
    /// suit energy pool plus per-tool heat. The cell grid stays the gameplay truth; this only shapes the stroke.
    /// </summary>
    public static class SonicTools
    {
        public const string ExcavatorId = "item.tool.excavation_shovel";
        public const string LegacyPrecisionId = "item.tool.precision_tool";   // merged into Focus Fine; still honoured in old saves
        public const string BrushId = "item.tool.brush";
        public const string BursterId = "item.tool.powered_drill";

        public const float SizeMin = 0.15f, SizeMax = 0.8f, ReachMin = 1.2f, ReachMax = 3f, SpreadMin = 0.25f, SpreadMax = 0.7f;
        public const float EnergyMax = 100f;

        public static SonicHead Head = SonicHead.Sphere;
        public static SonicPower Power = SonicPower.Med;
        public static SonicFocus Focus = SonicFocus.Wide;
        public static float Size = 0.45f, Reach = 3f, Spread = 0.45f;
        public static string MkText => "I";

        // Suit energy (shared by all sonic tools) and per-tool heat (0..100).
        public static float Energy = EnergyMax;
        static readonly float[] _heat = new float[4];
        static readonly bool[] _locked = new bool[4];
        static float _lastSpendAt = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Head = SonicHead.Sphere; Power = SonicPower.Med; Focus = SonicFocus.Wide;
            Size = 0.45f; Reach = 3f; Spread = 0.45f; Energy = EnergyMax; _lastSpendAt = -10f;
            for (int i = 0; i < 4; i++) { _heat[i] = 0f; _locked[i] = false; }
        }

        public static SonicToolKind KindOf(string itemId) => itemId switch
        {
            ExcavatorId => SonicToolKind.Excavator,
            LegacyPrecisionId => SonicToolKind.Excavator,
            BrushId => SonicToolKind.Brush,
            _ => SonicToolKind.None
        };

        /// <summary>Fine (precision) mode: a wave of fine lines from a point instead of a chisel head.</summary>
        public static bool IsFine(SonicToolKind k) => k == SonicToolKind.Brush || (k == SonicToolKind.Excavator && Focus == SonicFocus.Fine);

        public static float Heat(SonicToolKind k) => _heat[(int)k];
        public static float Heat01(SonicToolKind k) => Mathf.Clamp01(_heat[(int)k] / 100f);
        public static bool Locked(SonicToolKind k) => _locked[(int)k];
        public static float Energy01 => Mathf.Clamp01(Energy / EnergyMax);

        // ---- dials ----
        public static void CycleHead() => Head = (SonicHead)(((int)Head + 1) % 4);
        public static void CyclePower() => Power = (SonicPower)(((int)Power + 1) % 3);
        public static void CycleFocus() => Focus = (SonicFocus)(((int)Focus + 1) % 4);
        public static void SetFocus(SonicFocus f) => Focus = f;
        public static void AdjustSize(int dir) => Size = Mathf.Clamp(Mathf.Round((Size + dir * 0.05f) * 100f) / 100f, SizeMin, SizeMax);
        public static void AdjustReach(int dir) => Reach = Mathf.Clamp(Mathf.Round((Reach + dir * 0.2f) * 10f) / 10f, ReachMin, ReachMax);
        public static void AdjustSpread(int dir) => Spread = Mathf.Clamp(Mathf.Round((Spread + dir * 0.05f) * 100f) / 100f, SpreadMin, SpreadMax);

        static float PowerFactor => Power == SonicPower.Low ? 0.65f : Power == SonicPower.High ? 1.5f : 1f;

        public static ExcavationBrush BrushFor(SonicHead h) => h switch
        {
            SonicHead.Disc => ExcavationBrush.Disc,
            SonicHead.Cube => ExcavationBrush.Cube,
            SonicHead.Cone => ExcavationBrush.Cone,
            _ => ExcavationBrush.Sphere
        };

        /// <summary>The stroke spec for the equipped sonic tool; other tools pass through unchanged.</summary>
        public static DigToolSpec Effective(DigToolSpec t)
        {
            var kind = KindOf(t.itemId);
            if (kind == SonicToolKind.None) return t;
            float pw = PowerFactor;
            if (kind == SonicToolKind.Brush)
            {
                t.radius = Spread;
                t.removal *= 0.7f + 0.3f * pw;
                return t;
            }
            if (Focus == SonicFocus.Fine)
            {
                // The old precision tool: small shallow controlled bites that never damage a find.
                var p = DigTools.Precision;
                t.radius = Mathf.Clamp(Size * 0.3f, 0.08f, 0.22f);
                t.removal = p.removal * pw;
                t.maxHardness = pw > 1f ? 3f : p.maxHardness;
                t.damage = 0f; t.crude = false; t.rate = p.rate; t.cells = 1; t.minBite = p.minBite;
                t.brush = ExcavationBrush.Sphere;
                return t;
            }
            float f = Focus == SonicFocus.Wide ? 1f : Focus == SonicFocus.Mid ? 0.5f : 0.22f;       // chunk size / risk
            float rf = Focus == SonicFocus.Wide ? 1f : Focus == SonicFocus.Mid ? 0.8f : 0.6f;       // head tightening
            t.radius = Mathf.Max(0.12f, Size * rf);
            t.removal = t.removal * pw * f;
            t.damage = t.damage * pw * f;
            t.maxHardness = Power == SonicPower.High ? t.maxHardness + 1f : Power == SonicPower.Low ? t.maxHardness - 0.5f : t.maxHardness;
            t.brush = BrushFor(Head);
            return t;
        }

        // ---- energy and heat ----
        public static void StrokeCost(SonicToolKind k, out float energy, out float heat)
        {
            float pw = PowerFactor, hp = Power == SonicPower.Low ? 0.55f : Power == SonicPower.High ? 1.8f : 1f;
            if (k == SonicToolKind.Brush) { energy = 0.12f; heat = 0.18f; return; }
            if (Focus == SonicFocus.Fine) { energy = 0.22f * pw; heat = 0.4f * hp; return; }
            float sz = 0.6f + Size * 0.9f;
            energy = 0.5f * pw * sz; heat = 0.85f * hp * sz;
        }

        /// <summary>Null when the tool can fire, else the reason (shown on the HUD).</summary>
        public static string Blocked(SonicToolKind k)
        {
            if (_locked[(int)k]) return "Emitter overheated: cooling down. Ease off or lower Power (R).";
            if (Energy <= 0.05f) return "Suit power is empty. A Power Cell (or the ship) recharges it.";
            return null;
        }

        public static void Spend(SonicToolKind k)
        {
            StrokeCost(k, out float e, out float h);
            Energy = Mathf.Max(0f, Energy - e);
            _heat[(int)k] = Mathf.Min(100f, _heat[(int)k] + h);
            if (_heat[(int)k] >= 99.9f) _locked[(int)k] = true;
            _lastSpendAt = Time.time;
        }

        public static void AddEnergy(float amount) => Energy = Mathf.Clamp(Energy + amount, 0f, EnergyMax);

        /// <summary>Cooling and recharge, every frame (also while holstered).</summary>
        public static void Advance(float dt)
        {
            bool active = Time.time - _lastSpendAt < 0.3f;
            float cool = active ? 4f : 9f;
            for (int i = 1; i < 4; i++)
            {
                if (_heat[i] > 0f) _heat[i] = Mathf.Max(0f, _heat[i] - cool * dt);
                if (_locked[i] && _heat[i] <= 60f) _locked[i] = false;
            }
            if (!active)
            {
                var s = XenoarchRuntime.Session;
                bool ship = s != null && s.AboardShip;
                Energy = Mathf.Min(EnergyMax, Energy + (ship ? 25f : 0.5f) * dt);   // small field trickle so the suit never soft-locks
            }
        }
    }
}
