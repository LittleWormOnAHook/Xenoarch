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

### Verification results (actual)
- **Compile (open Editor, no batchmode): PASS.** Unity imported the scripts and built `Library/ScriptAssemblies/DMI.Xenoarch.Runtime.dll` (12:39:54 PM ET), `DMI.Xenoarch.Editor.dll` and `DMI.Xenoarch.Tests.EditMode.dll` (12:39:55 PM ET). Unity only writes these when the assembly compiles without errors.
- **First-run setup: ran** (marker `Library/XenoarchFirstRun.done` at 12:40:02 PM ET). Verified on disk: 6 scenes in `Assets/_Project/Scenes` (Bootstrap 2 objects, MainMenu 3, Ship_Starter 34, TradeHub_Sol 19, Moon 14, Mars 14), Build Settings = exactly those 6 with Bootstrap first, 7 item definitions + ItemRegistry, `Resources/XenoarchConfig` (registry + 4 starting items), 7 URP/Lit placeholder materials, pickup `wo.ship_starter.pickup_brush` in Ship_Starter.
- **Smoke check: result not captured.** `Editor.log` was truncated to 0 bytes at 12:40:42 PM ET (cause unknown, not by this work), losing the console lines. The smoke check now also writes `Logs/xenoarch_smoke.txt`; a re-run is queued via `Temp/xenoarch_smoke.trigger` and runs on the next script reload (Unity reloads when its window gets focus). Manual: `Xenoarch > Validate > Run Foundation Smoke Check`.
- **EditMode tests (5): not run** (Test Runner needs the Editor UI).
- **Play Mode: not run.**

## 2026-11-25 (afternoon, ET) — Digger PRO check, placeholder art
- Digger PRO installed by Anthony (`Assets/Digger`). Compiles cleanly on Unity 6000.4 / URP 17.4 (Burst, Collections, Mathematics, AI Navigation present). URP 17 shader package available; Digger auto-imports it on `Tools > Digger > Setup terrains`. Vendor files untouched.
- Added `IExcavationSurface` (runtime) and `DiggerExcavationSurface` adapter (own asmdef `DMI.Xenoarch.Integrations.Digger`). Excavation design updated to the hybrid (cell grid = gameplay truth, Digger = surface/collision) and the Moon/Mars Digger sculpting workflow.
- Placeholder art copied read-only from Genesis and ASH into `Assets/_Project/Art/Placeholder/{Genesis,ASH}` (29 PNGs, ~2.2 MB; sources in `SOURCES.md`). `PlaceholderArtImporter` imports them as sprites. Setup now wires icons into the 7 starter items (menu `Xenoarch/Setup/4`), smoke check verifies it.
- FPS_Starter_Pack: art/animation only (no controller). Materials already URP/Lit. Animator controllers are demo playlists without parameters, so Xenoarch will build its own arms controller from the pack's clips (Hands_Empty, Hands_PC).

## 2026-11-25 (1:30 PM ET): UITK, first-person player, Creepy Cat import, planet pack
- UI Toolkit placeholder UI: `Resources/UI` (PanelSettings, theme, Hud/MainMenu/Fade UXML, Xenoarch.uss with Genesis frames/glyphs). HUD, main menu and scene fade no longer use IMGUI or uGUI.
- First-person player: `FirstPersonController` (CharacterController + Input System, implements IPlayerRig; gravity per scene: ship/hub 9.81, Moon 1.62, Mars 3.71) + `FirstPersonArms` driving `XA_FP_Arms.controller` built from FPS_Starter_Pack clips (Hands_Empty idle/walk/run/jump/take; Hands_PC up/watch-idle/down for devices). Clips copied as looping .anim; vendor files untouched. `XA_Player` prefab replaced the capsule in Ship_Starter, TradeHub_Sol, Moon_Excavation_01, Mars_Excavation_01. Arms clip bindings resolve 55/61 paths (the 6 missing are IK helper bones, harmless).
- Smoke check: 45 pass / 0 fail (1:22 PM ET). Offline csc compile of all 4 Xenoarch assemblies: 0 errors.
- Creepy Cat 3D Scifi Kit Vol 3: full `_Creepy_Cat` folder copied from Genesis with .meta files (3,613 files, 3.71 GB; source untouched). `VendorMaterialConverter` converted 138 materials to URP (65 HDRP Lit, 1 HDRP Unlit, 26 Standard/legacy, 1 glass -> URP Lit; 45 particle -> URP Particles/Unlit), 0 failed; HDRP material data stripped from 64 files. Not tracked in git (vendor, 3.7 GB).
  - Missing nested prefabs (Genesis-only additions, not part of the vendor kit): DM_ForceField_Plane (Genesis _Project/Prefabs/ForceFields), Stairs01 (_DLNK Space Base Pack), Smoke Colored (Genesis _Project/Prefabs/VFX). Affects 6 example prefabs only: P_Glass_Tunnel_Part_01, P_Base_Biobase_A, P_Base_ComStation_A, P_Base_RadarStation_A, P_Room_Space_Station_01, P_Biodome_01.
  - Missing textures in 16 materials (see Logs/xenoarch_material_conversion.txt).
  - Kit scripts compile; several use legacy `Input.*` (MouseLook, Rover*, *_Example_Keys, LaserShoot, TurretLookAt, Crate_Open_Close) and will throw if run with Input System only. Do not use those demo controllers.
- PlanetPack02 (Genesis): **not imported**. Built-in render pipeline only (surface shaders + ForwardBase CG passes, render pink in URP), 572 MB of 8K textures, one trivial rotate script. If we want its planets for skyboxes, port one Shader Graph planet shader and downscale 1�2 texture sets.
