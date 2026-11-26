# Xenoarch Changelog

## 2026-11-25 — Code polish & gameplay refinement pass (UNTESTED)

Nothing in this pass has been run in Play Mode, smoke-tested or unit-tested. All changes compile cleanly in the Editor; everything else needs manual testing.

### Precision excavation (commit a2d2d58)
- **Scanner triangulation** (`Exploration/ScanTriangulation.cs`, new, session-only, never saved): each pulse records the scan position per site. Distinct positions (≥3 m apart; repeats count 0.2) plus the widest angle between them seen from the find (full credit at 75°) give a *fix quality*. Bearing wobble and the distance/depth error shrink by `1 - 0.85 × quality`; at quality ≥ 0.78 the site is pinpointed remotely (the ring turns the bar colour and a lock chirp plays). The HUD shows `±` distance/depth and a "Fix N%" advice line ("move sideways and pulse again").
- **Scanner upgrades matter more**: each handheld level adds +0.06 accuracy (max 0.95) and +0.5 m depth (`ScannerStats.HandheldAccuracy/HandheldMaxDepth`).
- **Careless digging**: a crude tool (e.g. shovel) cutting the cell directly above a hidden find jars it (0.15 × damage × fragility); two cells above gives a warning only. Emerging finds get a toast and a sound.
- **Feedback**: exposure progress bar with a READY/pry state; hard material names the tool you need (owned vs craftable); the tool key list marks unowned tools "(none)".
- **Deliberate recovery**: the pry prompt shows the expected condition loss; hold time is 1.2–2.5 s depending on exposure; the recovery toast includes a quality word.
- The smoke assertion g3 in `XenoarchPhase1CSetup` was updated for the jar rule but has not been run.

### Discovery architecture hooks (commit 4a24b22)
- `ArtifactDefinition.setId` / `fragmentOf` (optional). Recovering a member publishes `artifact.set:<setId>` / `artifact.fragments:<groupId>` = distinct pieces found, so chain nodes can require e.g. "3 of the set".
- `DiscoveryReward.unlockSystemId` publishes `nav.system.<id>`, which unlocks that system in the Nav dropdown. `revealSiteIds` reveals extra dig sites from decoded coordinates.
- New helpers: `Facts.SetProgress`, `Facts.Fragments`, `Facts.NavSystem`. No existing fact ids or save keys changed.
- **Not wired yet (design call for Anthony):** no chain node sets `unlockSystemId`, so Proxima Centauri, Barnard's Star and Epsilon Eridani stay locked until a node (or a debug fact) unlocks them.

### Navigation (commit 4a24b22)
- The core game has four Nav destinations: Sol, Proxima Centauri, Barnard's Star and Epsilon Eridani (`ExplorationService.NavSystems`).
- **DLC hook**: `ExplorationService.RegisterNavSystem(id, name)` or the `navDestination` flag on a `StarSystemDefinition` asset adds more destinations after the core four. `NavDestinations()` is used by the dropdown and the galaxy map.
- **Legacy systems**: Alpha Centauri, Sirius and Tau Ceti are **not deleted**; their assets, saves and facts are untouched. The galaxy map's known-systems list now shows only Nav destinations, plus chain-revealed signal sources. So Alpha Centauri and Sirius are hidden. Tau Ceti still appears as a "[Vael signal]" marker once `chain.moon_mars.extrasolar_coords` is decoded, and that fact still unlocks the galaxy map. Rows show `current / open / LOCKED / signal`.

### Crafting info (commit ef537d1)
- Recipe details list the requirements (backpack + ship crates), and each missing ingredient says where it comes from (the Refiner recipe that makes it, a craft recipe, or "dig it at a site").
- Upgrade recipes show level `from -> to (max)` and the real effect from the live tables (scan range/accuracy/depth, orbital radius, crate slots).

### Manual testing needed
1. Scanner: pulse from one spot, then from 2–3 spots spread sideways; check that the ± values shrink, the Fix % rises and the remote pin triggers; check no false pins on gate-locked sites.
2. Dig with the shovel straight above a buried find (expect a jar warning and condition loss) and with the trowel/brush (expect no jar).
3. Exposure bar, emerging-find toast, pry hold time, and the condition-loss prompt.
4. Run the smoke suite yourself (g3 assertion changed).
5. Nav dropdown: only 4 systems, extras locked; galaxy map list/markers; Tau Ceti marker after the extrasolar chain fact.
6. Inventory crafting panel: ingredient sources and upgrade effect lines.

### Next priorities
- Decide which chain nodes unlock Proxima Centauri, Barnard's Star and Epsilon Eridani (set `unlockSystemId` on those nodes' rewards), and whether Tau Ceti's coordinates should be re-pointed to one of the core four.
- Author artifact sets/fragments on existing ArtifactDefinitions and add chain nodes that require set progress.
- Review recipe costs for grind (no recipe assets were changed in this pass).

## 2026-11-25 (late): Discovery puzzle + Creepy Cat importer fix (UNTESTED)

All of this compiles cleanly. Nothing was played or tested.

### Vael star map puzzle (commit d32f67b)
- **Pieces:** the 29 Sol-system artifacts (11 `Artifact_moon_*`, 12 `Artifact_mars_*`, 6 `Artifact_vael_*`) now have `fragmentOf: proxima_star_map`. `Artifact_unknown_lump` is excluded. Since there are more than 8 definitions, the board needs **8 distinct artifact types**: no duplicates, and no artifact gives more than one piece. Loot tables are unchanged.
- **Inventory:** puzzle pieces show a small orange corner mark. Right-clicking one opens a menu with "Combine with other artifact", which is enabled once at least 2 pieces are owned (backpack or ship crates).
- **Reconstruction panel** (`UI/ArtifactPuzzleWindow.cs`):
  - The left side lists the owned pieces.
  - The right side is a square 3x3 board: 8 slots around a "MAP CORE" counter.
  - Click a piece and then a slot, or drag a piece onto a slot. Right-click a slot to remove its piece. Back or Esc returns to the inventory.
  - Artifacts stay in the inventory; the board only references them. A piece that is sold or lost drops off an unsolved board.
- **Reveal:** filling all 8 slots fades in a star map (Sol to Proxima bearing) and types out the coordinates RA 14h 29m 43s, Dec -62° 40' 46", 4.25 ly, with a chime and a toast. It then:
  - publishes `puzzle.solved:proxima_star_map`;
  - publishes `nav.system.proxima_centauri`, which unlocks the Nav dropdown entry;
  - adds a journal record (`DiscoveryService.Record`).
- **Save:** board state is saved under the new key `artifacts.puzzles`. Older saves load with an empty board.
- **DLC hook:** `ArtifactPuzzleService.Register(PuzzleSpec)` adds more puzzles.

### Creepy Cat FBX import warnings (commit f4a97a8)
- **Cause:** 416 FBX models under `Assets/_Creepy_Cat/_3D Scifi Kit Vol 3` (Meshes, Meshes/_Letters_Numbers, and one in Station Ptolemy) were set to the legacy `materialLocation: 0` (External). Unity 6 warns "MaterialLocation.External is obsolete" on every import.
- **Fix:** the Editor tool `XenoarchImporterFixes` (Tools/Xenoarch/Fix Creepy Cat FBX Material Location, or Temp/xenoarch_ccfix.trigger) did the following:
  - recorded each renderer slot's material;
  - switched the importers to `materialLocation: 1` (InPrefab);
  - added 923 external-object remaps that point every embedded material back to the same .mat asset it used before.
- **Result:** 0 conflicts and 0 slot mismatches on re-verification. 832 reimports produced no MaterialLocation warnings.
- **Not in git:** `Assets/_Creepy_Cat` is gitignored, so the changed .meta files live only on this machine. The tool is committed so the fix can be re-applied after a fresh vendor import. A backup of the original metas is at `C:\Users\Teabagger\cc_meta_backup`.

## 2026-11-26 - ZEN-8 dig fixes, ZEN-9 menus / refined scanner / soil textures (compiled only, NOT played)

- **Digging (943563e):** one full-strength Digger carve per stroke, sunk to the deepest new floor. Previously there was a partial-strength carve per cut cell, dozens per stroke, which flooded Digger's buffer. That caused lag, late holes and mostly-paint strokes. Carves left waiting are capped at 24, and at most 4 are retried per frame. The per-stroke list allocation is gone.
- **Tool tiers are data** (`Resources/Excavation/DigTools/DigTool_*.asset`, `DigToolDefinition`): chunk size, rate, rated hardness, artifact-damage risk, crude flag and slot order.
  - No tool has a depth cap; only bedrock stops digging.
  - Bands harder than a tool's rating are dug slowly (squared falloff) instead of being refused.
  - The shovel is about 3x faster (removal 2.0 at 5.6 strokes/s). The precision tool is about 1.5/s and the brush about 1.0/s.
  - Keys 2-9 follow the slot order.
- **HUD:** a persistent "HARD <band>: slow going ... <faster tool>" note, plus a bedrock note.
- **Fence:** marked major sites get stakes every 2.5 m with an orange rope and a large ground circle. These are rebuilt from the reveal level on load. Caches are excluded.
- **Deep sites:** standing on a site now always gives a weak reading. Before, a site deeper than the scanner's rating read "No signal", so it was never marked or fenced.
- **Menus (7c43576):** an ASH-flow main menu (Continue / New / Load / Settings / Quit) and a pause menu (Esc: Resume / Save / Load / Settings / Main menu / Quit) in UITK.
  - Slots: auto, quick, and slot1-6.
  - Autosave runs on a timer (default 10 min), on landing/launch and on discoveries. All three are set in Settings (PlayerPrefs).
- **Refined scanner (4b31153):** hold 1 for 0.6 s inside a fenced site; a tap still equips. Each pulse pinpoints the nearest unflagged buried find within 8 m and plants a red flag.
  - Codes are the body's first 2 letters + site order + spot order (Ma11).
  - Spot states: flagged, digging (flag pulled), unfinished (left the 3 m perimeter; flag replaced), done.
  - New save section `digsites.maps`; old saves start empty.
  - Maps window (N) has Surface and Dig Sites tabs.
- **Soil textures (44b9401):** CC0 ambientCG Ground099, recoloured, on Mars_Soil / Moon_Soil. That is Digger texture 1, the "red layer" on dug walls. A normal map was added; see Data/Terrain/SOURCES.md. The main surface layer 0 (Creepy Cat) is unchanged.
