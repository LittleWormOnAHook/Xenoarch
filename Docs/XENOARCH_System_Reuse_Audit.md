# XENOARCH — System Reuse Audit (Phase 1A)

_Created 2026-11-25 (ET). Sources were inspected **read-only**; nothing in Genesis or ASH was modified, opened in Unity, or touched with git._

Status legend used across all Xenoarch docs: **Planned** (designed, no code) · **Implemented** (code exists, compiles) · **Tested** (verified by a test that actually ran; the test is named) · **Incomplete** (partially done / known gaps).

## 1. Projects inspected

| Project | Path | Unity | Pipeline | Code layout |
|---|---|---|---|---|
| Xenoarch (target) | `A:\Xenoarch` | 6000.4.11f1 | URP 17.4.0 (template `urp-blank-17.1.0`) | New. Input System only (`activeInputHandler: 1`) |
| Dark Matter: Genesis | `A:\Dark Matter Genesis` | 6000.4.11f1 | **HDRP 17.4.0** (URP package also present) | `Assets/_Project/Scripts` (~950 .cs, 37 areas) + `Features` (~100 .cs), namespace `Project.*`, **no asmdefs** (all in Assembly-CSharp, heavily cross-referenced) |
| ASH | `A:\ASH` | 6000.4.11f1 | URP 17.4.0 | `Assets/_ASH/Scripts` (~300 .cs), namespace `ASH.*`, **no asmdefs**; already contains lite ports of Genesis systems (see `A:\ASH\Docs\GenesisReuseAudit.md`, `PrototypeStatus.md`) |

Key finding: ASH already did the hard "decouple from Genesis" work for most generic systems, and it is URP. **ASH is the preferred reuse source; Genesis is reference only.**

## 2. Per-system audit

Complexity: S (< 1 day), M (1–3 days), L (> 3 days) for a solo developer.

| System | Genesis | ASH | Xenoarch decision | Status in Xenoarch | Complexity |
|---|---|---|---|---|---|
| Player controller / locomotion | Invector vThirdPersonController 2.5.2 + ECM2 + 27 glue files (`PlayerController` 1,115 lines, `PioneerShooterMeleeInput` 3,324 lines) | Invector Basic Locomotion copy + `ASHInvectorInput`; legacy+new input (`activeInputHandler: 2`) | **Not reused. Vendor FPC pending** (Anthony is installing a first-person controller package). Xenoarch defines `IPlayerRig` adapter + a static capsule `PlaceholderPlayerRig`. Invector is third-person, third-party and needs legacy input → excluded. | Adapter: Implemented. Vendor FPC: Pending | S (adapter) |
| Interaction / highlighting | `IWorldUsable`, `WorldUseController` (1,658 lines, 17 namespaces coupled) | `IASHUsable`, `ASHInteractor` (clean, ~170 lines), `ASHHarvestOutline` | **Adapted** from ASH: `IInteractable`, `Interactor` (first-person reach, rig/input via interfaces). Outline highlight not ported (ASH outline is URP; port later if wanted). | Implemented | S |
| UI input gating | PlayerController cursor flags | `ASHUIState` token set | **Adapted**: `UIState` (+ optional cursor management for vendor FPC). | Implemented | S |
| Inventory | `InventorySystem` (1,326 lines; ammo/equipment/progression coupled) | `ASHInventory` (slots, two-pass add, split/merge; MonoBehaviour + UI) | **Adapted logic, new model**: `InventoryModel` (plain C#, item-id based, unique instances that cannot duplicate). | Implemented | S |
| Item definitions / registry | `ItemData`, `ItemRegistry` | `ASHItemDefinition` (survival fields), `ASHItemRegistry` (Resources singleton + scene fallbacks) | **Adapted pattern**: `DefinitionAsset` base with authored stable id + generic `DefinitionRegistry<T>`; `ItemDefinition`, `ItemRegistry`. Survival/clothing/grip fields dropped. | Implemented | S |
| Crafting / research | `RecipeDefinition`, `CraftingManager` (stations, progression) | `ASHRecipe`, `ASHCrafting` (CanCraft/TryCraft, timed) | **Pattern only** for Phase 1C research operations (timed operation on an item with requirements). No code copied yet. | Planned | M |
| Resource gathering | `ResourceNode` (hold-harvest) | `ASHResourceNode` | Hold-interaction support kept in `Interactor`; gathering itself not needed (archaeology ≠ survival). | Not needed | — |
| Objectives / quests | `Scripts/Quests` (13) | `ASHObjectiveTracker`, `ASHObjectiveChain(Runner)` | **Reference** for discovery chains; Xenoarch needs a non-linear prerequisite graph, so new implementation (Phase 1C). | Planned | M |
| Save / load | `GameSaveSystem` (42 KB, 20 namespaces, flat `GameSaveData` v23) | `ASHSaveSystem` + `IASHSaveable` sections (key/JSON/restore order), autosave, slots | **Adapted pattern**: `ISaveSection`, versioned `SaveFile`, `SaveService` (plain C#, migrations, atomic file writes). Restore happens **before** the scene loads (no frame-delay overwrite). | Implemented | M |
| Scene transitions | `LoadingOverlayController`, terrain content loader | `ASHLoadingScreen`, `ASHSceneFlow` | **Adapted pattern**: `SceneLoader` (fade, async load, spawn/pose placement), `XenoarchRuntime` game flow. | Implemented | S |
| UI framework | UI Toolkit HUD/journal (182 UI files, HDRP project), `Shift - Complete Sci-Fi UI` vendor pack | uGUI built in code (`ASHHud` 82 KB), `ASHUIKit` | Phase 1A uses **IMGUI placeholders** only. Recommend **UI Toolkit** for Phase 1B+ (data-heavy screens: nav map, journal, trade). Shift UI pack: check licence seat before use. | Placeholder | M |
| Audio | `Scripts/Audio` (21) | `ASHAudioManager`, `ASHAudioSettings`, `ASHSoundBank` | **Reuse candidate** (adapt namespace) when audio work starts. | Planned | S |
| Weather / environment | `DMIoSkyController` (HDRP/Io specific), Gaia, Enviro | `ASHWeatherProfile/Director/Outputs` (ash/rain/snow, Enviro 3 vendor) | **Concept only**: Xenoarch needs `EnvironmentProfile` (atmosphere, temperature band, gravity, visibility, hazards) as data, not live weather. | Planned (1B) | S |
| Temperature / exposure | `Survival/*` (exposure, radiation, thermal) | `ASHBodyTemperature`, `ASHExposure(+Settings)` | **Reference**: hazards become equipment requirements, not survival meters. | Planned | S |
| Surface deformation | `DMDrillController` (Genesis drill zones) | `ASHDeformationField` + `ASHDeformer`: player-following render-texture mask read by an accumulation shader (visual layer displacement, no collision, no volume) | **Not suitable for excavation volumes**; technique reusable for footprints/dust trails on regolith. See Excavation Design. | Evaluated | — |
| Building / placement | `DMBuildingPlacementController` (+7 partials), ghosts, snapping, power | `ASHPlacer`, `ASHPlacedSave` | Not needed in Phase 1. ASH `ASHPlacer` ghost pattern is a candidate for placing scanner beacons/excavation markers later. | Not needed | — |
| Vehicles | `Scripts/Vehicles` (15) | `ASHVehicle*` (14 files) + VehiclePhysics vendor | Out of scope (no rover in Phase 1). Future rover: reuse ASH design, check VehiclePhysics licence. | Not needed | L |
| Map / fog of war | `MapFogOfWar` | `ASHMapFog`, `ASHGpsMap` | **Reuse candidate** for surface survey map (Phase 1B/1C). | Planned | S |
| Journal | `JournalPanelUI` | `ASHJournalUI`, `ASHJournalLog` | Reference for discovery journal UI (Phase 1D). Data model is new (discovery database). | Planned | M |
| Economy / vendor | `Scripts/Vendor` (11) | `ASHVendor`, `ASHVendorMenu`, `ASHEconomySettings`, `ASHWallet` | **Pattern** for Phase 1D vendors (buy/sell with settings SO). | Planned | M |
| Sci-fi environments / equipment | `PolygonSciFiWorlds` (Synty), `SciFi_Industrial_LevelKit_2.0`, `Sci-Fi Collection`, `PlanetPack02`, `FORGE3D`, `HDRP SCI-FI Weapons` | — | Candidates for ship/hub art **only after** licence check and URP conversion (see §4). None imported in Phase 1A. | Not imported | M |

## 3. Third-party dependencies

| Package | Where | Licence note | Xenoarch |
|---|---|---|---|
| Invector Third Person Controller 2.5.2 | Genesis, ASH | Asset Store EULA (per-seat). Third-person; legacy Input Manager. | **Excluded** |
| ECM2 (Easy Character Movement 2) | Genesis | Asset Store EULA | Excluded (vendor FPC pending) |
| Malbers Animations, Gaia/Procedural Worlds, Enviro 3, EasyRoads3D, NatureManufacture, VehiclePhysics, AmplifyShaderEditor | Genesis/ASH | Asset Store EULA | Not needed in Phase 1 |
| Synty Polygon packs, Sci-Fi kits, Shift UI | Genesis | Asset Store EULA; confirm DMI holds the seat(s) | Deferred; licence check required |
| Cinemachine 3.1.7, Addressables 2.9.1, ProBuilder 6.1.2 | Genesis | Unity packages | Not added. ProBuilder is a good fit for ship/hub greyboxing later (optional). |

**Imported into Xenoarch in Phase 1A: none.** Only Unity template packages are present (URP 17.4.0, Input System 1.19.0, uGUI 2.0.0, Test Framework 1.6.0, AI Navigation, Timeline, Visual Scripting, Multiplayer Center, Collab Proxy, IDE packages). Candidates to remove later: `com.unity.multiplayer.center`, `com.unity.visualscripting`, `com.unity.collab-proxy` (unused; left as-is to avoid surprising the open Editor).

## 4. Render pipeline compatibility (HDRP → URP)

- Genesis `Assets/_Project`: 493 materials. Sample of the 400 most common: the top shaders are HDRP/Lit (GUID `6e4ae406…`, 99 mats) plus custom/Shader Graph or vendor shaders (`178af239…` 99, `9070473…` 70, Shader Graph `37652a24…` 51+, others). **None of these work in URP as-is.**
- Conversion route if Genesis art is reused: copy only the specific meshes/textures, create new URP/Lit materials (Base Map, Normal, Mask Map → URP Metallic/Smoothness + Occlusion require channel repacking: HDRP mask = R metallic, G AO, A smoothness; URP metallic map = R metallic, A smoothness, AO separate). Shader Graph assets must be retargeted to URP target and checked for HDRP-only nodes. HDRP Volume profiles, decals, Density Volumes, Diffusion Profiles, and Physically-Based Sky do not carry over.
- ASH assets are already URP; ASH custom shaders (`Shaders/Accumulation`, `Shaders/Rendering`) are URP but specific to ash/snow and were not imported.
- Xenoarch Phase 1A materials: 7 new placeholder materials on `Universal Render Pipeline/Lit` created by the setup script. **No HDRP content imported.**

## 5. Summary

- Reused directly: **none** (deliberately: every candidate had ASH/Genesis namespace or survival coupling).
- Adapted (rewritten, ASH as the source of the pattern/logic): interaction (`IASHUsable`/`ASHInteractor`), UI gating (`ASHUIState`), inventory add/remove rules (`ASHInventory`), item registry (`ASHItemRegistry`), save sections (`IASHSaveable`/`ASHSaveSystem`), scene flow (`ASHLoadingScreen`/`ASHSceneFlow`).
- New: stable-id definition base and generic registry, versioned save envelope + migrations + storage abstraction, world persistence (consumed ids), player rig adapter, runtime composition root, setup/scene builder, smoke check, EditMode tests.
- Vendor FPC pending: integrate via `IPlayerRig` (see System Architecture §5).
