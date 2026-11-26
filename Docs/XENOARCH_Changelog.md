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
