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

## 6. Creepy Cat 3D Scifi Kit Vol 3 (copied to `Assets/_Creepy_Cat`), reusable content

### 6.1 Constellation ships (`Prefabs/Props/_Update 1.11-Spaceship Constellation_R1`)
These were read only; nothing was modified.

| Prefab | GameObjects | Lights | Colliders | Scripts | Use in Xenoarch |
|---|---|---|---|---|---|
| `..._Mark_1_Fake` | 1,173 | 3 | 373 | Turn_Move ×4, ObjectFacingCamera ×3, DoorDoubleSlide ×1 | **Exterior** for space travel, docking and orbit shots |
| `..._Mark_1_FlyMode` | 1,623 | 36 | 566 | + DoorDoubleSlide ×4 | Walkable interior, gear **up** (in flight) |
| `..._Mark_1_StandMode` | 1,623 | 36 | 566 | + DoorDoubleSlide ×4 | Walkable interior, gear **down** (landed). **Recommended for Ship_Starter** |

- **Fly vs Stand**: the hierarchy is identical. Only `Gear_01/02/03` differ (y 5.94 vs 3.35). Pick by context, or swap the gear transforms at runtime instead of keeping two prefabs.
- **Orientation**: the bow faces **−Z** (cockpit at z ≈ −33.6, reactors at z ≈ +31.8). Decks: lower/cargo y ≈ −0.6 (`Interior_Bottom_01`), main y ≈ 5.4, upper/roof y ≈ 9.4.
- **Lighting cost**: Stand and Fly have 36 realtime lights, 26 of them spotlights. The Editor log already shows "Reduced additional punctual light shadows…" atlas warnings. Before 1B, cut shadowed lights to a few, bake the rest, or raise the URP shadow atlas.
- **Root groups**: `_Interior` (422 objects: floors, walls, corridors, roofs, the aft room and 4 sliding doors), `_Props` (1,176 objects: cockpit, holo table, consoles, crates, servers), `_Reactors` (4 afterburner particles), and `Spaceship_Constellation_Body_1` (hull, glass, gear, cockpit platform).

**Doors and the Input API.** Interior doors are `P_Wall_Int_Door_Simple_02` ×4 at (−3,5.4,−18.5), (7,5.4,−10.5), (−5,5.4,−10.5) and (−3,5.4,7.5). They use `Common Scripts/DoorDoubleSlide.cs`:
- It opens and closes in `OnTriggerEnter/Exit` on a trigger BoxCollider (3.85 × 3.46 × 2.94 m), sliding 1.6 m along Z.
- It does **not** use the old Input API. Our CharacterController fires the triggers, so the doors work with XA_Player as they are.
- Caveats: any collider opens them (thrown items, NPCs later); there is no lock or power state; it plays only the first AudioSource; `Pneumatic_Door_01` (lower deck) has no script.
- For 1B, wrap or replace the door logic with a Xenoarch door component (player layer filter, locked/powered state, save state). Keep the meshes.

**Other ship scripts**: `Turn_Move` spins antennas/radar and `ObjectFacingCamera` handles billboards (it uses `Camera.main`; our player camera is tagged MainCamera). Neither uses input.

**Old Input API elsewhere in the kit.** The project runs **Input System only** (`activeInputHandler: 1`), so these scripts would throw if used: `MouseLook`, `PhysicAddForce`, `RoverControler_A`, `RoverPilotHead`, `RoverWheelDrive`, `Uween/CreepyCatTest`, `Infinity_Example_Keys_01`, `Infinity_Joystick_01`, `Container_Example_Keys_01`, `Crate_Open_Close`, `Walkyrie_Example_Keys_01`, `LaserShoot`, `TurretLookAt`. None of them are on the Constellation prefabs. Do not use them as they are; port them if needed.

### 6.2 Station placeholder → ship room/equipment mapping (StandMode local coordinates; for re-parenting in Phase 1B)

| Ship_Starter placeholder (current) | Constellation location / object | Notes |
|---|---|---|
| `Cockpit_Console` + **Navigation Terminal** (Phase 1B) | `_Props/P_Cockpit_Tech_02` (−0.1, 5.6, −33.6): steering wheels, button panels, handle; pilot chairs `P_Chair_07` ×2 at z −31 | Interactable on the console; also the travel menu (replaces the debug TradeHub/Mars travel pillars) |
| **Orbital Scanning Terminal** (Phase 1B) | `_Props/P_Holo_Table_04` (0, 5.6, −26.7) on `P_Platerform_Base_01` behind the cockpit | The holo table fits planet/orbit scan UI. Side consoles `P_Cockpit_Tech_03_Rev_B` (±6, 5.6, −22.3) are spare slots (comms, ship status) |
| **Research Laboratory** (Phase 1C) | Mid-ship: `P_Wall_Computer_01` (−3, 5.4, −16.5), `P_Display_Level_01` (−4.9, 6.2, −4.5), `P_Console_Small_01` (1.3, 5.4, 2.1), around `P_Thing_18` (0, 4.4, 4.5) | No dedicated lab room. Dress it with kit props (`P_Desktop_Complex_01`, `P_Tube_Pod_01/02`, `P_Health_Pod_01`) |
| **Engineering / Upgrade Terminal** (Phase 1D) | Aft room `_Interior/P_Interior_Room_01` (3, 5.4, 21.5): `P_Reactor_ColdFusion_01`, `P_Computer_Server_02/04`, `P_Desktop_Part_01` | Natural engine room |
| `ArtifactStorage_Racks` | Lower/cargo deck `Interior_Bottom_01` (y −0.6): `P_Crate_Simple_01/02` stack at (4.4, −0.7, −2) and (−1.5..0.7, −0.6, 8.7); `P_Console_Small_01` (−4, −0.6, −4.2) as a cargo manifest console | Artifact storage container + UI |
| `EquipmentStorage_Lockers` | Main deck `P_Char_Crate_01` (2, 5.4, −13.4) and `P_Crate_Simple_02` (5.8, 5.4, −2.7) | Equipment locker/crate interactable |
| **Airlock** (debug travel to Moon) | Lower deck `Interior_Bottom_01/Pneumatic_Door_01` (static mesh, no script) | Becomes the exit/landing-ramp interactable; animate it with our own door component |
| Ultrasonic Brush pickup | Any crate/table on the main deck (e.g. `P_Char_Crate_01`) | Keep it a `WorldObjectId` pickup |
| Player spawn | Main deck corridor near the bridge, e.g. (0, 5.4, −10) facing −Z | Set when the ship goes into Ship_Starter |

### 6.3 FX and particles (`Prefabs/Fx`, 24 prefabs; particle materials already converted to URP Particles/Unlit)
- **Ship and space**: `Particle_Afterburner` (+ `_Small` and `_Big Variant`), `Particle_Dust_Space`, `Particle_StarsZone_01`, `Particle_Glow Zone_01`, `Particle_Attractor_01`, `Sprite_Sun`, `Sprite_Glow`, `Halfball_Glow`, `SpriteSheet_Lightning_01`.
- **Planets/excavation**: `Particle_Dust_Storm` (Mars), `Particle_Cloud_Atmos_A/B/C`, `Particle_Smoke`, `Particle_Spark` (tool/drill contact), `Explosion_01`.
- **Laser**: `LaserShoot_A/B`, `LaserImpact_A/B`, `LaserController_A/B`. The impact visuals are useful for a cutter tool. `LaserShoot` and `TurretLookAt` use the old Input API, so use the visuals only.
- 16 kit materials have missing textures (see `Logs/xenoarch_material_conversion_creepycat.txt`). Check each FX before use.

### 6.4 Other props worth using (`Prefabs/Props`, 508 prefabs; `Builds` 132)
- **Machines & Computers (30)**: `P_Drilling_Machine_01..03` (excavation sites), `P_Computer_Server_01..04`, `P_Desktop_*`, `P_Console_*`, `P_Health_Pod_01`, `P_Oxygen_Tank_01`, `P_Tube_Pod_01/02`, `P_Big_Radar_01`, `P_Radar_Pylon_01`, `P_Electric_Tower_01`, `P_TV_Big_Screen_01`, `P_Stargate_01..04` (galaxy-travel set dressing).
- **Panels (46)** and **Numbers & Words (41)**: signage and panels for the ship and TradeHub.
- **Physics (7)**: crates and ropes (rope scripts not reviewed).
- **Ships/stations**: Akorn, Walkyrie, Infinity R1–R3, Falabella & Magellan, Spaceships Kit 1.0, Station Ptolemy. Candidates for TradeHub docking traffic and later player ships.
- **Terrain**: `_Update 1.14-New Moon Terrain` and `1.15-New Mars Terrain` (Moon/Mars surface reference meshes and terrain data). Compare with Digger-sculpted terrain before use.
- `Things (For Alexy)` (20) and `Others` (2) are misc shapes, crystals and a free-look camera (the camera script was not reviewed).

### 6.5 Post-processing profiles (`_3D Scifi Kit Vol 3/Post_Process`): **not URP**
| Asset | Type | Effects |
|---|---|---|
| `Post-Processing Profile_A_2017.asset`, `..._B_2017.asset` | **Post Processing Stack v1** (`PostProcessingProfile`) | fog, antialiasing, ambient occlusion, SSR, bloom |
| `Post-processing Profile_A_2018.asset` | **Post Processing Stack v2** (`PostProcessProfile`) | ambient occlusion, bloom |

- Neither package is installed, and URP ignores both, so the assets are inert (missing script). They also appear only in the kit's example scenes.
- **Recreate as URP Volume profiles** when lighting each scene:
  - Global Volume: Bloom, Tonemapping (ACES), Color Adjustments, Vignette.
  - Ambient occlusion → URP **SSAO renderer feature**.
  - SSR → none in URP (use reflection probes).
  - Fog → `RenderSettings` fog or a volumetric fog asset.
- This was not converted now: it isn't a one-to-one asset conversion, and the values should be tuned per scene. Use the 2018 profile's bloom and AO values as the starting point.
