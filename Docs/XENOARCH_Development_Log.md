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
- PlanetPack02 (Genesis): **not imported**. Built-in render pipeline only (surface shaders + ForwardBase CG passes, render pink in URP), 572 MB of 8K textures, one trivial rotate script. If we want its planets for skyboxes, port one Shader Graph planet shader and downscale 1-2 texture sets.

## 2026-11-25 (1:50 PM ET): Kade body, hands lowered/hand slots, ship prefab survey

### Player (XA_Player prefab only; no scenes opened or saved)
- **Kade full body** (`Body/Kade` under XA_Player). Copied read-only from Genesis `TripoModels/rugged_male_character_3d_model` (the same mesh and avatar `Player_Kade_v2` uses; Mixamo humanoid) into `Assets/_Project/Art/Characters/Kade`. No Invector components or scripts. The hair (.glb) and the Rebreather mask were not copied. The material was converted to URP/Lit (1/1 OK; report in `Logs/xenoarch_material_conversion_kade.txt`). The Genesis AO map was wired as occlusion. The unused HDRP height map is reported missing, which is harmless.
- **Head/neck split**: at build time the mesh is split by skin weights into `Kade_Body_NoHead` (83,290 tris, rendered and casting shadows) and `Kade_HeadNeck` (10,088 tris, shadows only). The local camera never sees inside the head, and the shadow stays complete. `PlayerBody.SetFirstPersonView(false)` shows the head again for a future photo or cutscene camera.
- The body is scaled to 0.913 so the head bone sits about 0.1 m under the eye camera. It is offset 0.14 m back, so looking down shows the chest and legs.
- **Locomotion**: `XA_Body_Locomotion.controller` uses humanoid clips from Kevin Iglesias *Basic Motions* (Asset Store licence, no scripts), copied from Genesis into `Art/Characters/Animations/BasicMotions`. Its 2D freeform blend (MoveX/MoveZ in m/s) has 20 motions: idle, an 8-way walk at 1.5 m/s, an 8-way run at walk speed 3.2, and 5 sprint directions at 5.8. Jump start, mid-air, fall and land are separate states. A grounded grace period stops CharacterController flicker. There is no root motion.
- **FP arms**: with empty hands, each arm swings down out of view. The renderers are then disabled and the Animator is culled, so you see the body's own arms at your sides. An arm raises only while that hand holds something (`FirstPersonArms.SetHeld(Hand.Left/Right, bool)`), or briefly on Interact.
  - The arms controller now has `LeftHeld`, `RightHeld` and `ToolHeld` parameters.
  - It also has `LeftHand` and `RightHand` override layers, masked to each arm. Each layer's weight follows its hand. The placeholder pose is PC watch idle, which tools can swap with an AnimatorOverrideController.
  - `HandSlot_L` and `HandSlot_R` sit under `hand_l` and `hand_r` for tool models.
- Menu: **Xenoarch/Setup/6. Build Player Body (Kade) + Hand Slots (prefab only)**. It edits the existing prefab in place, so the per-scene gravity overrides survive. Trigger file: `Temp/xenoarch_body.trigger`.
- Smoke check: **53 pass / 0 fail**, including 8 new body/hands checks. The compile was clean (Tundra build success).

### Ship prefab survey (read-only), see Reuse Audit §6
- `P_Spaceship_Constellation_Mark_1_{Fake,FlyMode,StandMode}`: FlyMode and StandMode are the same ship. The only difference is the landing gear: up in FlyMode, down in StandMode. Fake is the exterior shell, with no interior lights and fewer interior pieces.
- The doors (`DoorDoubleSlide`) are trigger-based, with no Input API. Station-to-room mapping for Phase 1B is in the audit.
- Post-processing profiles are PPv1/PPv2, not URP. They need recreating as URP Volume profiles.

## 2026-11-25 (afternoon, ET): Phase 1B systems, skyboxes, travel plan
- Phase 1B code is in and compiles. Smoke check 77 pass / 0 fail. Added:
  - Sol data: 18 bodies plus TradeHub Sol, with environment profiles and gravity
  - starter ship data, saved in the `ship.state` and `world.discovery` sections
  - Navigation and orbital scanner terminals (UITK)
  - handheld surface scanner (1 to equip, LMB/RT to pulse) and site markers
  - XenoarchDoor wrapper for the Creepy Cat doors, and the ship airlock
  - environment applier (gravity, ambient/fog, URP Volume, skybox)
- Ship_Starter: Anthony aligned the stations to the Mark 1 StandMode ship (committed 53ca720). The additive pass (Setup/8) only swaps placeholder components on his objects. It never moves anything and runs only on a saved, clean scene. It is pending while he keeps editing the scene.
- Space skyboxes: the kit's .mat files reference a shader that isn't in this project, so new Skybox/6 Sided materials were built from the kit's face textures in `Art/Skyboxes`. The sky is applied at runtime per orbit (Earth/TradeHub, Moon, Mars, Europa/Enceladus; deep space by default), so the scene file isn't edited.
- Travel plan (map, cockpit, FTL through the front window) is documented in the Roadmap. Not built yet.
- 15:04–15:10 ET: ran the Ship_Starter pass (Setup/8).
  - Doors did not open because the kit marks everything Static, so the leaves were static-batched. Static is cleared on the leaves in this scene only, and XenoarchDoor is added.
  - The navigation terminal is swapped onto Anthony's holo-table (`P_Holo_Table_04/.../Holo_Globe_01`). Every placeholder copy is now swapped.
  - Smoke check: 83/0. Anthony checked doors, nav and skybox in Play.

## 2026-11-25 (4:00-5:05 PM ET): 1B polish, cockpit seats/console, sky rule, auto-compile
- Polish pass (Setup/9) ran on the saved Ship_Starter at 16:24: 8 floating labels removed (plus TradeHub 4, Moon 2, Mars 2), console `CockpitHelm` on Anthony's `P_Cockpit_Tech_02`, `CockpitSeat` on both `P_Chair_07` (StandMode ship; components only, nothing moved, vendor prefabs untouched), ship Volume -> `VP_CC_Profile_A_2018`. Smoke 92/0. Commit bd13ca6.
- Sky rule (Anthony): the ship scene's own skybox is open space; Earth/Moon/Mars swap in EarthOrbit/MoonOrbit/MarsOrbit; every other body uses the scene sky. Derived from the saved ship location, so it survives save/load. The ship builder no longer overwrites the scene skybox. New game starts in Earth orbit (Anthony OK).
- Seats: no sit animation in the Kade set, so the body is hidden while seated. Seated eye height raised 1.15 -> 1.3 m (`CockpitSeat.eyeHeight`). Saves taken while seated store the standing spot.
- Console commands (Engage drive, Nav map, Orbital scanner) now play a procedural right-arm reach to the console (about 0.35 s out, press, back) and fire on the press (`FirstPersonArms.Reach`, aim point from `CockpitHelm.PressPointFor` or an optional `pressPoint`).
- Compile tooling: the earlier "Unity is in Play mode" reports were wrong. Unity was idle and already in the foreground, so it never got the focus event that triggers auto-refresh. `XenoarchAutoCompile` now polls `Temp/xenoarch_compile.trigger` (refresh + compile, works in the background), logs each assembly result to `Logs/xenoarch_compile.txt`, writes a play/compile heartbeat to `Logs/xenoarch_editor_state.txt`, and supports `Temp/xenoarch_stopplay.trigger` (used only with Anthony's OK). `Tools/unity_compile.ps1` drives it and toggles focus if needed. 17:03 and 17:05 compiles: all assemblies OK, no errors. Smoke 92/0 at 17:04.

## 2026-11-25 (5:10-5:25 PM ET): Phase 1C batch 1 - dig, recover, research, Moon-to-Mars chain
- Runtime: `Excavation/ExcavationGrid` (pure C# cell grid, tool rules, exposure, damage, RLE save), `ExcavationService` (`excavation.sites`), `ExcavationSite` (trench mesh + collider, buried finds, Digger mirror via `IExcavationSurface`), `ExcavationDirector` (builds sites per scene, gives the player `DigTool`), `BuriedArtifact` (hold E to recover), `DigTool` (keys 2-5, LMB), `Artifacts/*` (`ArtifactDefinition : ItemDefinition`, `CivilizationDefinition`, `ArtifactService` with instances, civ knowledge and the lab queue, `ResearchRules`), `Discovery/*` (`DiscoveryChainDefinition`, `DiscoveryService` evaluator + journal, `Facts` keys), `Ship/ResearchTerminal`, `UI/Toast`. GameSession registers the new save sections; ExplorationService gained `SiteRevealed` and fact gating (`ArchaeologicalSiteDefinition.gateFact`).
- Setup/10 (`XenoarchPhase1CSetup`, trigger `Temp/xenoarch_1c.trigger`) ran 17:22 ET: 30 artifacts, civ.vael, chain.moon_mars, 22 site placements, Moon/Mars terrain with site holes (primitive ground + area marker deactivated), Research Laboratory placeholder -> ResearchTerminal (position kept).
- Decisions taken for the slice (open to change): cell size 0.25 m; Moon/Mars switched to Unity Terrain now (doc question 2); civilization working name "the Vael"; lab durations 6-18 s and translation cap 60% for testing.
- Digger PRO is not driving the visuals yet: no scene has Digger set up. The trench mesh is our own; strokes are forwarded to Digger automatically once a scene has `DiggerExcavationSurface`.
- Verification: compile clean 17:21 ET (all Xenoarch assemblies OK, no `error CS`), smoke 114 pass / 0 fail (24 new 1C checks: content, dig rules, research, full chain, save/load). Not played by hand.


## 2026-11-25 (6:05-6:25 PM ET): Phase 1C batch 2a/2b - Digger terrains, site styles, sized finds, scan border, lab scroll, airlock return
- Terrain: Setup/10 now builds `Data/Terrain/<scene>_Terrain.asset` from the Creepy Cat sample terrains (vendor files read only), heightmap 2049, centred on the origin at height 0. The batch-1 flat terrain and its data were deleted; our placeholders (lander, beacon, spawn, board-lander, test ring) were snapped onto the new ground. Ran 18:15 ET.
- Digger PRO: set up through its own "Tools/Digger/Setup for runtime" (shaders imported, `Assets/DiggerData/Scenes/<scene>`), `DiggerMaster.ResolutionMult` 4, plus `DiggerExcavationSurface` (buffer 512). The grid samples the terrain per column (`ExcavationGrid.surface`), carves each cut cell (sphere about one voxel, texture per soil band) and replays the saved grid on load instead of using Digger's own persistence. Digger reverts Play-mode edits when Play stops.
- Data: `DigSiteStyle` (soil bands with label, hardness, colour, Digger texture; border colour). Four styles created and assigned to the six sites. `SoilMaterial` gained Ice and Rock.
- Finds: footprint/height derived from the artifact size (max 3x3x2 cells); six chain/big artifacts enlarged. New damage rule (crude tool + already visible).
- Scan: readings clear after 5 s; the beacon shows for 5 s on pinpoint; persistent square `LineRenderer` border around pinpointed sites.
- Lab: `TerminalWindow.ScrollCol` (vertical ScrollView per column, scroll position kept across refreshes); used by the research lab.
- Return to ship: `SceneTravelInteractable` targeting Ship_Starter from another scene now goes through `ShipTravel.ReturnToShip()` (airlock spawn).
- Fix: the 1B galaxy ScriptableObjects shared one file (`GalaxyDefinitions.cs`), so their assets had no script reference (`m_Script: {fileID: 0}`). They only worked while the objects stayed in memory, and they came back empty after this batch's reload. Split into one file per class, re-linked 30 assets, and moved the old script GUID to `EnvironmentProfile.cs` (9 environment assets).
- Verification: compile clean 18:23 ET (Runtime/Editor/Digger assemblies OK, no `error CS`), smoke 121 pass / 0 fail at 18:23 ET (new: sized footprints, styles, visible-only damage, precision safe, partial exposure, ice band, per-column surface). Ship_Starter had unsaved changes, so that pass was skipped (nothing in it changed). Not played by hand.
