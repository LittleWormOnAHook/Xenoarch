# XENOARCH — Development Log

Times are US Eastern (ET).

## 2026-11-25 — Phase 1A foundation

**Inspected (read-only):** `A:\Dark Matter Genesis` (HDRP, Unity 6000.4.11f1; README, `Scripts/*` areas, save/core/building/environment code, packages, materials), `A:\ASH` (URP 17.4.0; README, `Docs/GenesisReuseAudit.md`, `Docs/PrototypeStatus.md`, interaction/inventory/registry/save/input/UI-state/deformation/weather code, packages). No writes, no git commands, not opened in Unity.

**Xenoarch at start:** fresh URP blank template (Unity 6000.4.11f1, URP 17.4.0, Input System 1.19.0, Input System only), `SampleScene` only, not under git, `.plastic` folder present (Unity Version Control workspace from Hub creation). Project open in the Editor during this work, so **no batchmode was run**.

**Decisions**
- Vendor first-person controller will be supplied by Anthony → no controller built or ported; `IPlayerRig` adapter seam + static capsule placeholder (arrow keys turn 45° for testing).
- ASH is the reuse source (URP, already decoupled from Genesis); all reused systems were rewritten into `DMI.Xenoarch.*` (no file copies, no vendor code).
- Plain C# models + single composition root (`XenoarchRuntime`), state restored before scene load.
- Definitions use authored dotted ids; instances use GUIDs; scene objects use `WorldObjectId`.
- Excavation: layered-cell diggable volumes for 1C, upgradeable to voxel meshing (see Excavation Design).
- Placeholder UI = IMGUI; recommend UI Toolkit for 1B+.

**Created:** 36 files under `Assets/_Project` (25 runtime .cs, 3 editor .cs, 1 test .cs, 3 asmdefs), `Docs/` (11 files), `.gitignore`, `.gitattributes`. Scenes/materials/item assets are generated inside the Editor by `Xenoarch > Setup > Run Full Phase 1A Setup` (also runs automatically once on first compile via `XenoarchFirstRun` if the Bootstrap scene is missing; scenes are created additively, the open scene is not touched).

**Verification:** see "Verification results" below (updated as results come in).

### Verification results
- Compile in open Editor: _pending — Unity had not refreshed the new scripts at time of writing (Editor window not focused; Unity imports on focus)._
- Smoke check: _pending (runs automatically after first compile)._
- EditMode tests: _not run._
- Play Mode: _not run._
