using DMI.Xenoarch.Core;
using DMI.Xenoarch.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DMI.Xenoarch.Excavation
{
    /// <summary>Sonic tool behaviour of the dig tool: dials, energy/heat gating, beam and debris FX, brush cleaning.</summary>
    public sealed partial class DigTool
    {
        SonicBeam _beam;
        SonicAudio _sfx;
        SonicHud _hud;
        SonicToolKind _kind;
        GridArtifact _nearFind;
        float _lockAt;

        void SonicAwake()
        {
            _sfx = gameObject.AddComponent<SonicAudio>();
            _beam = gameObject.AddComponent<SonicBeam>();
        }

        void OnSonicEquip()
        {
            _kind = Equipped >= 0 ? SonicTools.KindOf(DigTools.All[Equipped].itemId) : SonicToolKind.None;
            if (_kind == SonicToolKind.None) return;
            SonicTools.ActiveKind = _kind;
            SonicDebris.Bind(transform.root);
            _sfx?.Shot(SonicSfx.Equip);
        }

        void OnSonicHolster()
        {
            _kind = SonicToolKind.None;
            if (_beam != null) _beam.Hide();
            if (_sfx != null) _sfx.StopLoops();
            if (_hud != null) _hud.Refresh(false, SonicToolKind.None, 0f, null, 0);
        }

        /// <summary>Hotbar key press for a dig tool. The legacy precision item (old saves) opens the excavator in Fine focus.</summary>
        void KeyEquip(int i, string slotItem)
        {
            if (slotItem == SonicTools.LegacyPrecisionId)
            {
                if (Equipped == i && SonicTools.Focus == SonicFocus.Fine) { Equip(-1); return; }
                if (Equipped != i) Equip(i);
                if (Equipped == i) SonicTools.SetFocus(SonicFocus.Fine);
                return;
            }
            Equip(Equipped == i ? -1 : i);
        }

        void SonicInput(Keyboard kb, Mouse mouse)
        {
            if (UIState.BlocksGameplay) return;
            bool exc = _kind == SonicToolKind.Excavator;
            if (kb != null)
            {
                if (exc && kb[Key.Q].wasPressedThisFrame) { SonicTools.CycleHead(); _sfx?.Shot(SonicSfx.Change); }
                if (kb[Key.R].wasPressedThisFrame) { SonicTools.CyclePower(); _sfx?.Shot(SonicSfx.Change); }
                if (exc && kb[Key.T].wasPressedThisFrame) { SonicTools.CycleFocus(); _sfx?.Shot(SonicSfx.Change); }
                if (kb[Key.X].wasPressedThisFrame)
                {
                    if (SonicTools.UseCell()) { Toast.Show($"Power Cell used: suit power {SonicTools.Energy01:P0}. {SonicTools.Cells} left.", 2.5f); _sfx?.Shot(SonicSfx.Equip); }
                    else Toast.Show(SonicTools.Cells < 1 ? "No Power Cells. Craft them aboard the ship." : "Suit power is already full.", 2.5f);
                }
            }
            if (mouse == null) return;
            float sy = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(sy) < 0.01f) return;
            int dir = sy > 0f ? 1 : -1;
            bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            if (shift) SonicTools.AdjustReach(dir);
            else if (exc) SonicTools.AdjustSize(dir);
            else if (_kind == SonicToolKind.Brush) SonicTools.AdjustSpread(dir);
            else SonicTools.AdjustReach(dir);
        }

        void SonicLocked(string why)
        {
            if (Time.time - _lockAt < 1.5f) return;
            _lockAt = Time.time;
            Flash(why);
            _sfx?.Shot(SonicTools.Locked(_kind) ? SonicSfx.Overheat : SonicSfx.Empty);
        }

        static Color LayerTint(SoilMaterial m) => m switch
        {
            SoilMaterial.Dust => new Color(0.78f, 0.68f, 0.54f),
            SoilMaterial.Loose => new Color(0.62f, 0.50f, 0.38f),
            SoilMaterial.Compacted => new Color(0.50f, 0.40f, 0.32f),
            SoilMaterial.Ice => new Color(0.80f, 0.92f, 0.97f),
            SoilMaterial.Rock => new Color(0.45f, 0.43f, 0.42f),
            _ => new Color(0.30f, 0.30f, 0.32f)
        };

        /// <summary>Dust and chunks where a stroke landed, tinted by the band that was cut.</summary>
        void SonicFx(ExcavationSite site, in RaycastHit hit, in DigToolSpec tool)
        {
            var lay = site.LayerAtPoint(hit.point, out _, out _);
            var tint = LayerTint(lay.kind);
            bool fine = SonicTools.IsFine(_kind);
            int dust = fine ? 2 : Mathf.RoundToInt(4f + tool.radius * 14f);
            int chunks = _kind == SonicToolKind.Brush ? 0 : fine ? 0 : (Random.value < 0.6f ? 1 : 2) + (SonicTools.Power == SonicPower.High ? 1 : 0) + (_kind == SonicToolKind.Burster ? 1 : 0);
            SonicDebris.Burst(hit.point, hit.normal, tint, tool.radius, dust, chunks);
        }

        /// <summary>The brush sweeps dust off an exposed find: its cleaned share rises towards 100%.</summary>
        void BrushClean(ExcavationSite site, Vector3 p)
        {
            bool any = false;
            foreach (var a in site.Grid.artifacts)
            {
                if (a.recovered || a.clean >= 1f) continue;
                if (Vector3.Distance(site.CellCentre(a.x, a.y, a.z), p) > 1.2f) continue;
                float e = site.Exposure(a);
                if (e < 0.15f) continue;
                a.clean = Mathf.Min(1f, a.clean + 0.04f * Mathf.Clamp01(e * 1.5f));
                any = true;
            }
            if (any) site.NotifyChanged();
        }

        void SonicFrame(bool aimed, bool inReach, bool held, string lockWhy, in RaycastHit hit, in DigToolSpec tool, Camera cam, ExcavationSite site)
        {
            bool firing = held && inReach && site != null && lockWhy == null && !UIState.BlocksGameplay;
            float rad = tool.radius;
            if (site != null && site.MirrorsToSurface) rad = Mathf.Max(rad, site.CarveRadius * 0.8f);
            if (_beam != null)
            {
                _beam.Tick(new SonicBeam.Frame
                {
                    kind = _kind, aimed = aimed && !UIState.BlocksGameplay, inReach = inReach, firing = firing,
                    nearFind = _nearFind != null || _beam.HeadNear, locked = lockWhy != null,
                    point = hit.point, normal = hit.normal, radius = rad, spread = tool.radius, cam = cam,
                    head = _kind == SonicToolKind.Burster ? SonicHead.Cone : SonicTools.Head
                });
            }
            if (_sfx != null)
            {
                float pitch = SonicTools.Power == SonicPower.Low ? 0.9f : SonicTools.Power == SonicPower.High ? 1.15f : 1f;
                _sfx.SetLoop(firing, SonicTools.IsFine(_kind) ? SonicSfx.FanLoop : SonicSfx.BeamLoop, pitch);
            }
            if (_hud != null) _hud.Refresh(true, _kind, rad, lockWhy, SonicTools.Cells);
        }
    }
}
