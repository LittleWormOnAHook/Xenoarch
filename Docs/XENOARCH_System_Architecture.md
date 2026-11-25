# XENOARCH — System Architecture

_Status legend: Planned · Implemented · Tested · Incomplete. Updated 2026-11-25 (Phase 1A)._

## 1. Principles
- Independent Unity 6 URP project, namespace root `DMI.Xenoarch`. Living World Engine components that become shared later move to their own namespace/asmdef (e.g. `DMI.LivingWorld.*`); nothing in Phase 1A qualifies yet.
- **Definitions vs state.** ScriptableObject definitions (static, authored, stable string ids) are never mutated at runtime. Player and world state are plain `[Serializable]` classes saved as JSON and reference definitions by id only.
- **Generated world ≠ discovery state.** Galaxy/site definitions (authored or seeded) are rebuilt from data + seed; what the player has found/changed lives in save sections.
- **Gameplay independent of presentation.** Models are plain C# (`InventoryModel`, `SaveService`, `WorldPersistence`, `GameSession`); MonoBehaviours are thin adapters; UI only reads.
- **No hidden singletons.** One composition root (`XenoarchRuntime`) owns the `GameSession`; systems receive the session through context (`InteractionContext.Session`) or constructor.
- Controller-agnostic: gameplay sees the player only through `IPlayerRig`.

## 2. Assemblies

| asmdef | Path | References | Status |
|---|---|---|---|
| `DMI.Xenoarch.Runtime` | `Assets/_Project/Scripts/Runtime` | Unity.InputSystem, UnityEngine.UI | Implemented |
| `DMI.Xenoarch.Editor` | `Assets/_Project/Scripts/Editor` (Editor only) | Runtime | Implemented |
| `DMI.Xenoarch.Tests.EditMode` | `Assets/_Project/Tests/EditMode` (Editor only, UNITY_INCLUDE_TESTS) | Runtime, NUnit, TestRunner | Implemented |

Later phases may split Runtime into `Galaxy`, `Excavation`, `Research`, `Economy` assemblies once they exist; not before.

## 3. Runtime modules (Phase 1A)

| Namespace | Types | Responsibility | Status |
|---|---|---|---|
| `Core` | `StableId`, `IHasStableId`, `DefinitionAsset`, `DefinitionRegistry<T>`, `XenoarchConfig`, `SceneNames`, `GameSession`, `PlayerLocationState`, `XenoarchRuntime` | Ids, definition base, config, session state, composition root, game flow (new/save/load/travel) | Implemented |
| `Items` | `ItemDefinition`, `ItemCategory`, `ItemRegistry`, `IItemLookup` | Static item data | Implemented |
| `Inventory` | `InventoryModel`, `ItemStack`, `InventorySaveData` | Slots, stacking, unique instances, capture/restore | Implemented |
| `Save` | `ISaveSection`, `SaveFile`, `SaveSectionRecord`, `ISaveMigration`, `SaveService`, `ISaveStorage`, `FileSaveStorage`, `MemorySaveStorage`, `JsonStateSection<T>` | Versioned JSON saves | Implemented |
| `World` | `WorldPersistence`, `WorldObjectId` | Stable ids for scene objects, consumed/recovered state | Implemented |
| `Scenes` | `SceneLoader`, `BootstrapEntry` | Fade transitions, spawn/pose placement | Implemented |
| `Player` | `IPlayerRig`, `PlayerRigLocator`, `PlayerSpawnPoint`, `PlaceholderPlayerRig` | Controller adapter seam; placeholder until vendor FPC | Implemented (placeholder) |
| `Interaction` | `IInteractable`, `InteractionContext`, `IInteractionInput`, `InputSystemInteractionInput`, `Interactor`, `ItemPickupInteractable`, `SceneTravelInteractable` (debug), `StationPlaceholderInteractable` | Look-at interaction, tap/hold | Implemented |
| `UI` | `UIState`, `InteractionPromptHud` (IMGUI placeholder), `MainMenuPlaceholder` (IMGUI) | Input gating, placeholder UI | Implemented (placeholder) |

## 4. Runtime flow
1. `XenoarchRuntime.AutoCreate` (BeforeSceneLoad) creates `[XenoarchRuntime]` (DontDestroyOnLoad): loads `Resources/XenoarchConfig`, builds `GameSession` (item registry, inventory, world persistence, save service with file storage at `persistentDataPath/Saves`) and a `SceneLoader`.
2. `Bootstrap` scene (build index 0) → `BootstrapEntry` → MainMenu.
3. New Game → `GameSession.StartNewGame()` (reset all sections, starting kit, location = Ship_Starter/default) → fade → load → place rig at spawn.
4. Save (F5 quick save in Phase 1A) → location pose captured from `IPlayerRig.Root` → `SaveService.Save` → `persistentDataPath/Saves/quick.json`.
5. Load (F9 / Continue) → read + migrate → **apply state first** → load scene → scene objects read restored state in `Start` (consumed pickups disable themselves) → rig placed at saved pose.
6. Playing any gameplay scene directly in the Editor starts an implicit new game so systems have state.

## 5. Vendor FPC integration (pending)
1. Import the vendor package under `Assets/_Vendor/<Package>` (git-ignored by default; see .gitignore).
2. Add a MonoBehaviour adapter (e.g. `VendorFpcRigAdapter : MonoBehaviour, IPlayerRig`) on the vendor player prefab: `Root` = player root, `ViewCamera` = vendor camera, `SetInputEnabled` → vendor's enable/disable input or movement API, `Teleport` → vendor-safe teleport (disable CharacterController/rigidbody interpolation, reset velocity).
3. Add `Interactor` (+ optionally a custom `IInteractionInput` reading the vendor's input actions) and `InteractionPromptHud` to the prefab.
4. Set `UIState.ManageCursor = false` if the vendor controller owns the cursor.
5. Replace `Player_Placeholder (vendor FPC pending)` in each scene with the prefab, delete `PlaceholderPlayerRig`.
6. Put the adapter in its own asmdef if the vendor ships asmdefs; Runtime must not reference the vendor.

## 6. Save format (v1)
```json
{ "version": 1, "gameVersion": "0.1", "slot": "quick", "savedUtcTicks": 0, "playSeconds": 0,
  "sections": [ { "key": "player.location", "json": "{...}" },
                { "key": "player.inventory", "json": "{\"capacity\":24,\"slots\":[...]}" },
                { "key": "world.persistence", "json": "{\"consumed\":[\"wo.ship_starter.pickup_brush\"]}" } ] }
```
- Each system owns its section. Missing section → that system keeps its new-game reset.
- `SaveService.CurrentVersion` bumps on incompatible changes; register an `ISaveMigration` from the old version. Newer-than-build saves are refused.
- Inventory restore drops unknown item ids and duplicate unique instance ids (never duplicates artifacts).
- Writes are atomic (`.tmp` + replace, previous kept as `.bak`).

## 7. Stable identifiers
- Definitions: authored lowercase dotted ids (`item.tool.brush`, later `body.sol.moon`, `site.moon.tycho_01`, `civ.aurelian`). Validated by `DefinitionRegistry.Validate()`; never renamed after saves exist.
- Scene objects with persistent state: `WorldObjectId` (`wo.<scene>.<name>` for authored, GUID default).
- Runtime instances (artifacts, recovered tech): GUID (`StableId.NewInstanceId()`), stored in inventory/state.
- Procedural content (later): id = deterministic function of (parent id, seed, index) so it is stable across sessions.

## 8. Known architectural limits (Phase 1A)
- Single save slot used by the UI ("quick"); multi-slot UI not built. Incomplete.
- `WorldObjectId` duplicates when objects are duplicated in the Editor; caught by the smoke check (scene text scan) only. Incomplete.
- Placeholder UI is IMGUI. Planned replacement: UI Toolkit.
