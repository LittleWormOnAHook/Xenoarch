# Xenoarch Changelog

## 2026-11-26 (evening) - Drill tuned for ice; Mars Dune Shrine moved into Mk I orbital coverage (t209u, UNTESTED in Play)

- **Drill:** removal 1.8 -> 3.5 (`DigTool_drill.asset` and the `DigTools.Drill` fallback). In ice/rock (hardness 3) the drill now digs about 1.5-1.9x deeper per second than the shovel (aimed column: 1.16 vs 0.62 m/s centred). In soft soil it is about 1.25x deeper per second but still narrower (0.25 vs 0.45 m radius). The shovel and the other tools are unchanged. The smoke check now compares metres per second in the aimed column and requires at least 1.5x.
- **Orbital range reverted:** Mk I is back to 400 m (Ship_ConstellationMk1, ShipDefinition default, "Orbital scanner range I: 400 to 700 m").
- **Dune Shrine moved instead** (approved Mars scene edit): from (358, 358) to (285, 326.5), 80 m inward. It is now 378 m from the re-baked Mars map centre (-26, 111.5), so the first orbital pass covers it. It is 376 m from base, 563 m from the Canyon Relay and 688 m from the Buried Spire.
  - Its landing zone moved with it: now (263.9, 310.8), 26 m from the site, with the shuttle re-grounded and the arrival spawn reset.
  - Its 4 scene journals (dig_flags, dig_hardness, scan_depth_probe, maps) were re-placed by the Place Journals rule.
  - The surface map and landing hints were re-baked. The Mars map is now 936 m square, x -494, z -356.5. Nothing else in the scene moved, and no other objects were near the old spot.
  - Caches, resource nodes and dig sites are placed at runtime from these positions, so there was nothing to re-sync in Digger.
- **New tool:** `XenoarchSiteMove` (trigger `Temp/xenoarch_sitemove.trigger`: "plan|apply siteId ringMin ringMax"; log `Logs/xenoarch_sitemove.txt`) moves one site using the Spread Sites rules. It refuses to run while the scene is open with unsaved changes or the Editor is in Play mode.
- Smoke 171/0, M1 check 82/0, galaxy snapshot compare identical.
## 2026-11-26 (evening) - Smoke check: 5 failures fixed (UNTESTED in Play)

- **Dig bug (real):** since ZEN-8 (943563e) set shovel removal to 2.0, one shovel or drill stroke could clear the soil above a still-hidden find and then remove its whole cell in the same stroke. The find was fully exposed with no damage (only the 15% jar), and the next stroke dug underneath it, so crude tools almost never damaged single-cell finds and the "jarred something" warning came too late. `ExcavationGrid.StrokeAt` now stops a crude stroke on top of a hidden find after it has cut the soil above, and caps a crude bite into a find cell at 0.5 (`CrudeFindBite`). The precision tool and brush are unchanged. This fixed the "surface dust" and "jar" checks.
- **Orbital coverage (real):** ZEN-14 (2c664e1) spread the Mars sites to the map edges, so all three were outside the Mk I 400 m orbital scan radius (dune shrine 450 m, buried spire 418 m, canyon relay 586 m from the Mars map centre). A Mars orbital scan at Mk I revealed nothing. The Mk I radius is now 460 m (`Ship_ConstellationMk1`, `ShipDefinition` default, Orbital Scanner Range I text "460 to 700 m"). The dune shrine is covered; the buried spire still needs 3 passes, so it stays hidden at Mk I. The canyon relay (story, gated) still needs Range I or an on-foot scan to resolve. Moon sites were already inside.
- **Stale checks updated:** "precision tool never damages finds" now compares the condition before and after the precision strokes (the earlier shovel jar had been counted against it). "Style bands drive hardness" now compares the aimed column's depth per stroke (the wider shovel's total over all columns isn't a speed measure).
- Smoke 171 / 0, M1 check 82 / 0.
- Tuning note: in ice the drill now digs only about 10% deeper per stroke than the shovel (0.099 vs 0.089 m), and the shovel strokes faster (5.6/s vs 4/s). This is a side effect of t108u (linear hardness falloff) and ZEN-8.
## 2026-11-26 (evening) - Phase 2 M1: foundations and safety (UNTESTED in Play)

- **Progression classes** (data, separate from rarity): `ArtifactDefinition.progression` = Ordinary / ResearchRequired / StoryCritical, with `releaseWhen` facts and a `progressionNote`. The tablet is StoryCritical until `civ.identified:civ.vael` and 35% translation. The Mars navigation core is StoryCritical until `tech.installed` (physically installed). The sensor lattice is ResearchRequired (it can be sold after a warning). Everything else is Ordinary.
- **One gate:** `ArtifactEligibility.Check(instance, action)` returns Allowed, Warning or Blocked, with a reason. The services enforce it: `ArtifactService.TrySell/CanInstall/CanStart`, `ShipHold.Transfer`, and the bulk `InventoryModel.Remove` guard used by crafting and refining. The vendor lists blocked items as "not for sale" and asks for a second click on warned items.
- **Star map:** placed pieces stay secured until the puzzle is solved. A piece cannot be sold if that would leave fewer than 8 different owned piece types; duplicates and surplus pieces can be sold.
- **Recovery** (`ArtifactRecoveryService`, save key `artifacts.recovery`): runs on load, every 5 s and when the lab terminal opens. It returns missing protected items with the same instance id (backpack, then crate, then held at the research lab for collection). It buys back a story item that was sold by an unguarded path, issues at most one `recovered:<id>` replacement per definition if the record itself is lost, and cancels lab jobs whose item is gone. It is idempotent and keeps a ledger. Knowledge facts are never removed.
- **New Game** now deletes every development save: autosave, quick save and slots 1-6, including their .bak/.tmp files. Before this, New Game kept saves. There is no migration or recovery for old saves, by decision.
- **Procedural pool frozen:** `ProceduralArtifactPools` v1 = [artifact.unknown_lump], with explicit order, plus a weighted v2 hook. Added independent RNG streams `ProceduralGalaxy.Stream(id, stream, seed)`. The tools are Xenoarch/Tools/Galaxy Snapshot, Galaxy Snapshot Compare and Galaxy Snapshot Record Baseline (`Temp/xenoarch_galaxy.trigger`). The baseline is `Docs/galaxy_snapshot_baseline.txt`, and the compare after M1 was a PASS (identical).
- **Check:** Xenoarch/Validate/Run Phase 2 M1 Check (`Temp/xenoarch_m1check.trigger` -> `Logs/xenoarch_m1_check.txt`): 82 pass / 0 fail. Chain_MoonMars was not changed and no mandatory steps were added. SaveService.CurrentVersion is unchanged (1).
## 2026-11-26 (afternoon) - Held first-person tool models (ZEN-23, UNTESTED)

- Added held models for the six tools Anthony picked from the mockups: wand depth probe, pistol-grip scanner, entrenching shovel, dual-ended precision tool, stubby brush and heavy rock drill. They are simple generated meshes under `Assets/Xenoarch/Tools/Held/`.
- `HeldToolVisuals` (added on the player by `PlayerToolsBootstrap`) parents the matching prefab to `HandSlot_R` when that tool is equipped from the hotbar, and hides it on holster or switch. The arm is raised with `FirstPersonArms.SetHeld`. The depth probe now also calls `SetHeld` (the dig tools and scanner already did).
- The probe and drill models only appear when those items are owned, because their equip paths already refuse if the inventory does not have them.
- Rebuild: Tools/Xenoarch/Player/Build Held Tool Prefabs (`Temp/xenoarch_heldtools.trigger` -> `Logs/xenoarch_heldtools.txt`). Catalog: `Resources/Tools/HeldToolCatalog`.
- No scene was edited or saved. Compiles; not seen in Play yet. Grip pose is a starting alignment on `HandSlot_R` and may need a small tweak after looking at it in game.

## 2026-11-26 (morning) - UI icons: one style (t155u, approved mockups, UNTESTED)

- Applied the mockups Anthony approved (`Docs/Images/UI_Icons/01-04`, rules in `Docs/XENOARCH_UI_Icons.md`). Compiles; not seen in Play yet.
- **Hotbar strip, inventory (hotbar row, backpack, details), artifact puzzle slots and drag ghost:**
  - Every item icon is a white silhouette at one size and padding (78% of the icon box), tinted by category: tools bone white, artifacts orange, parts teal, materials their light colour.
  - The handheld scanner is now radar-sweep, the powered drill is drill, and the test fragment is rune-stone. All artifacts get rune-stone, except the sensor lattice, which gets alien-stare (Lorc, game-icons.net, CC BY 3.0, credited in `Art/Icons/GameIcons/SOURCES.md`).
  - Brush, shovel, precision tool and sample container use padded copies in `Art/Icons/Items`.
- **Minimap, Maps window, orbital scanner and shuttle maps:** site, cache, base and landing markers are now Kenney target, star, home and arrowDown glyphs with a dark halo, in the same colours. The uncertainty circle and the player and shuttle arrows are unchanged, and a selected marker gets a ring.
- **Pause menu:** Kenney glyphs on Resume, Save, Load, Settings, Main menu and Quit.
- **Journal (J):** check / arrow / lock glyphs instead of `[x]` / `[ ]`.
- Shared USS: `.xa-icon`, `.xa-item-icon--*`, `.xa-map-icon--*`, `.xa-journal-icon--*` and `--xa-icon-*` / `--xa-map-*` variables. Code helper: `XenoarchIcons`.

## 2026-11-26 (morning) - Decorative rocks on planets (t151u, UNTESTED)

- Poly Haven CC0 rocks from `Assets/Xenoarch_Free` are now prefabs in `Resources/World/Rocks` (moon_rock_01/03/05/07 and boulder_01). They use URP Lit materials in `Art/Rocks`, convex colliders, a bottom-centre pivot normalised to 1 m, and a cull-only LODGroup.
- `RockScatterDirector` places them at runtime on every surface body: Moon 180, Mars 160, and future planets 140 tinted from their map colour. Placement is seeded and repeatable. Rocks are mostly small with some clusters, sunk 20% into the ground, kept to slopes of 30-32 degrees or less, and tinted per planet (Mars red-orange, Moon grey).
- Rocks keep clear of dig sites, landing zones, the base zone (30 m), the walking corridors from the base to each site zone, valuable caches, resource nodes, player spawns and any existing collider. No scene was edited or saved, and nothing placed was moved.
- Boulder_01 is 124k triangles, so it is rare (weight 0.1). Rocks cull below 2% of screen height.
- Data: `Resources/World/RockScatterProfile`. Rebuild: Tools/Xenoarch/World/Build Rock Prefabs + Scatter Profile (`Temp/xenoarch_rocks.trigger`).
- The ambientCG terrain textures stay unused for now. The UI icon swap is on hold until Anthony picks from the mockups.

## 2026-11-26 (morning) - Xenoarch_Free asset pack moved and added to git

- Anthony moved the CC0 pack to `Assets/Xenoarch_Free/` and deleted the old `Assets/!GET_MORE_ASSETS_FOR_FREE` promo folder. Planet Forge is cleared for use (Anthony, t147u).
- The pack (about 235 MB, 1814 files with .meta; the largest file is under 10 MB) is now committed. The stale .gitignore lines for the promo folder are removed.
- `XenoarchFreeAssetsImporter` now targets `Assets/Xenoarch_Free/`. Both SOURCES files point at the new path.

## 2026-11-26 (early) - Free CC0 asset pack (Xenoarch_Free) + import defaults

- Added about 232 MB / 883 files of CC0 assets in `Assets/!GET_MORE_ASSETS_FOR_FREE/Xenoarch_Free/`. That folder is **gitignored** (repo .gitignore line 74, and excluded from commits by standing rule), so the files are on Anthony's disk only. The source and licence list is committed as `Docs/XENOARCH_Free_Assets_SOURCES.md`. Contents:
  - ambientCG 2K PBR sets: Rock064 / 058 / 035, Ground054 / 080, Gravel043, Ice002 / 004, Snow006, Lava004.
  - Poly Haven moon_rock_01 / 03 / 05 / 07 and boulder_01 (FBX, 1K maps).
  - Kenney Space Kit: 153 FBX.
  - Kenney Sci-fi Sounds (73 OGG) and Interface Sounds (100 OGG).
  - Kenney UI Pack Sci-fi (PNG + fonts).
  - Kenney Game Icons (white 2x).
- `XenoarchFreeAssetsImporter` (editor) sets first-import defaults for that folder only:
  - normal maps as Normal Map; roughness / AO / height linear; PBR max 2048 with mipmaps
  - UI PNGs as sprites without mipmaps
  - audio as Vorbis (UI clips decompress on load, engine loops compressed in memory)
  - models without cameras or lights
- The original promo files (`DEV FREEDOM.txt`, `DevFreedom.club ... .url` and their metas) are untouched.

## 2026-11-26 (early) - t140u: Mars buried chamber; dev testing loadout off (UNTESTED)

Nothing here has been run in Play Mode or tested. Compiles in the open Editor.

- `devTestingLoadout` is now OFF in XenoarchConfig (commit 1e5877d): new games start with the normal kit.
- **Buried chamber under the Canyon Relay (Mars)**, built the way Anthony described:
  - Dig the Canyon Relay site down to its floor (3 m) at grid cell (6, 6), near one corner. That uncovers a sealed Vael door with a glowing ring. Hold E (1.2 s) to open it. The first time, it publishes `chamber.opened:chamber.mars.canyon_relay`, gives +10 notoriety and shows a message.
  - After a short fade you arrive in a stone vestibule. A 10 m tunnel leads to a 7 x 7 m room whose floor is 2 m of diggable Mars soil, with 3.4 m of headroom above it.
  - The room holds four Vael finds: glyph tablet, effigy, offering bowl and bead string. Several are Proxima star-map fragment types, so they give another chance at missing pieces.
  - Every dig tool works in the room, and the bed saves like any dig site (`chamber.mars.canyon_relay.floor`). Glyph strips and lights light the chamber. The glowing panel at the back of the vestibule takes you back up onto the door in the pit.
- Data-driven: `ChamberDefinition` (Resources/Excavation/Chambers: host site, entrance cell, layout sizes, finds, colours) and `ChamberDirector`, which builds everything at runtime. **No scene was edited or saved.**
  - The interior is built below the terrain's own volume, so terrain and Digger never touch it.
  - The soil bed is a trench-mesh dig grid, not Digger voxels.
- An optional chain node, "Open the buried chamber under the Canyon Relay", follows the Mars scan.
- Dig sites now also check height when matching a hit point (`ExcavationSite.At`), so stacked sites stay separate.
- Known limits:
  - Chamber yaw should stay at 0/90/180/270, because dig grids are axis-aligned.
  - Moving in and out is a fade, not a walk-through.

## 2026-11-26 (early) - Phase 1D batch 5: notoriety unlocks; Phase 1D code complete (UNTESTED)

Nothing here has been run in Play Mode or tested. Compiles in the open Editor.

- Every rank publishes the discovery fact `notoriety.rank` (value = rank), so chain nodes and gates can require a rank.
- `XenoarchConfig.economy.rankUnlocks`: per rank, an optional fact, an optional Nav star system (`unlockSystemId`) and a message shown with the rank-up toast. Applied whenever the rank is reached or a save loads.
- Defaults (placeholders):
  - Rank 1 and rank 2 announce the Equipment Supplier's rank-gated stock and upgrades.
  - Rank 3 notes the better buyer prices.
  - **No star system is unlocked by rank.** Which system (if any) a rank or a chain node unlocks, such as Barnard's Star or Epsilon Eridani, is still Anthony's call: set `unlockSystemId` on a rank or on a chain reward.
- Phase 1D (credits, vendors, Trade Hub, equipment and upgrade purchases, fuel, notoriety and unlocks, alien tech, discovery journal UI) is now all in code. None of it has been played. Prices and points are placeholders for the cost pass. Phase 1E (integration) has not been started.

## 2026-11-26 (early) - Phase 1D batch 4: discovery journal screen (J) (UNTESTED)

Nothing here has been run in Play Mode or tested. Compiles in the open Editor.

- New **J** window (pauses like the maps window; J or Esc closes). It has four tabs:
  - **Discoveries:** each discovery chain with its steps (done, open, or "???" while prerequisites are missing) and the full discovery log, newest first.
  - **Finds:** every artifact ever recovered (name, rarity once identified, condition, carried / stored / sold / installed, site, estimated value), identified civilizations with language knowledge, and installed tech.
  - **Notes:** collected datapad journals (new ones flagged); clicking one opens the note reader with prev / next.
  - **Standing:** credits, notoriety score and rank ladder, and the last 25 deals.
- Read-only. Nothing in saves changes.

## 2026-11-26 (early) - Phase 1D batch 3: recoverable alien tech, research and install (UNTESTED)

Nothing here has been run in Play Mode or tested. Compiles in the open Editor.

- The Vael Sensor Lattice (Mars, Canyon Relay dig site; already placed there) is now a functional device. After **Technology analysis** at the ship's research lab, its page shows **Install in the handheld scanner**. Installing uses the artifact up (marked Installed, removed from the backpack) and adds +1 handheld scanner tier: more range, accuracy and max depth. On a fully upgraded scanner, extra tiers add 15% range each.
- Data-driven: `ArtifactDefinition.installTarget` (HandheldScanner / OrbitalScanner) and `installBonus`; fact `tech.installed:<artifact id>` for chains; +20 notoriety (`economy.techInstalled`). The lab's LAB column lists installed tech.
- Moon-Mars chain: "Analyse the sensor module" no longer grants +1 ship scanner level by itself (it only logs that the lattice can be fitted). The new optional node "Install the sensor module" logs the install. Saves that already analysed the lattice keep the old +1 ship scanner level.
- Data changes are made by Tools > Xenoarch > Setup > Economy (rerunnable).

## 2026-11-26 (early) - Phase 1D batch 2: upgrades for credits; roadmap/checklist updated (UNTESTED)

Nothing here has been run in Play Mode or tested. Compiles in the open Editor.

- The Equipment Supplier also sells the workshop upgrades for credits: ship storage (800/1600/3200), handheld scanner range (600/1200/2400, rank 1), orbital scan radius (900/1800/3600, rank 1), depth probe (700/1400, rank 2). Same upgrade levels as crafting, so either route works and the max level is shared. Prices are placeholders.
- Roadmap: 1D marked in progress. Phase 1 checklist: criteria 13, 14 and 19 marked in code / not yet played; new Phase 1D section.

## 2026-11-26 (early) - Phase 1D batch 1: credits, notoriety and Trade Hub vendors (UNTESTED)

Nothing here has been run in Play Mode or tested. Compiles in the open Editor.

- Credits: new `EconomyService` (save key `player.wallet`: credits, a transaction log of the last 200 deals, one-off purchases). New games and saves from before 1D start with `XenoarchConfig.economy.startingCredits` (500). `JsonStateSection` got an optional new-game factory for this (existing sections unchanged).
- Notoriety (`progression.notoriety`): ranks Unknown Explorer -> Independent Excavator (25) -> Recognized Archaeologist (75) -> Renowned Explorer (200) -> Galactic Authority (450) -> Legendary Discoverer (1000). Earned once per deed: recovering an artifact (2-40 by rarity), pinpointing a site (5), identifying a civilization (15), each translation pass (3), other research ops (1), first collection of a cache (2), selling an artifact (1). Rank-up shows a toast. Credits and rank are on the inventory status line. All values are placeholders in `XenoarchConfig.economy`.
- Vendors: `VendorDefinition` assets in Resources/Vendors (created by Tools > Xenoarch > Setup > Economy). On loading TradeHub_Sol, `VendorDirector` replaces the three "offline" placeholder stations with working vendor terminals (runtime only; the scene file is unchanged):
  - Artifact Buyer: buys artifacts at 80% of their appraised value (condition, research and rarity count) and valuables at 80% of base value, +3% per notoriety rank.
  - Equipment Supplier: sells sample containers, reagents, fuel cells, components, the precision tool, depth probe (rank 1, once) and powered drill (rank 2, once); buys resources at 50%.
  - Refuelling Service: 4 cr per unit of ship fuel (buy 10 or fill the tank).
- Sold artifacts are removed from the backpack and marked Sold. Bought items go to the backpack (refused if it is full).
- Prices and rank gates are placeholders for the recipe/cost pass.

## 2026-11-26 (night) - t134 (ZEN-17): orbit view varies per arrival and moves in real time (UNTESTED)

Nothing here has been run in Play Mode or tested. Compiles in the open Editor.

- Cause: the orbit view placed the planet at the fixed `windowOffsetDeg` / size of its OrbitViewDefinition with a constant spin, and the ship sun at a fixed offset, so every arrival looked identical.
- Each arrival (FTL or in-system travel, `ExplorationService.CommitTravel`) stores a random `Ship.arrivalSeed` and `arrivalPlaySeconds` (saved; older saves derive the seed from the body id). The seed picks a small yaw/pitch offset from the authored framing, a size change, a random visible face, a tilt change and a sun angle change (different day/night line).
- While parked: the planet spins from the body's new `rotationPeriodHours` (Moon 655.7 h, Mars 24.6, Earth 23.9, Jupiter 9.9) sped up x600 and clamped to 0.01-0.4 deg/s; the planet and the sun drift slowly (sine, 15 min cycle); the ship sun light, disc and flare follow. Driven by play time since arrival, so a reload looks the same.
- Framing: the offset never exceeds the authored offset + 8 deg (max 45 deg) from the helm window direction. Cockpit objects are not touched (runtime objects only). Tunables: `XenoarchConfig.orbitViewMotion`.
- Not done: moons are not drawn in the orbit view yet (only the orbited planet and the sun), so there is no moon drift.

## 2026-11-26 (night) - t132-t133 (ZEN-16): Depth Probe - third scanner, upgradable to 3 tracked objects (UNTESTED)

Nothing here has been run in Play Mode or tested. Compiles in the open Editor.

- Item `item.tool.depth_probe` (Tool). Default hotbar key 6 (when the hotbar was never customised); otherwise drag it onto any slot 2-8. Equipping it holsters the dig tool / scanner and vice versa.
- Use: aim at the dug surface (up to 5 m), LMB pulse or hold LMB for live readings (every 0.25 s; a reading stays 6 s). For the nearest buried objects (unrecovered artifacts by the top of their shape, uncollected cache loot) below that point it shows depth and sideways offset (ahead / left / behind-right...), never the object type. A pulse ring and a coloured ring over each tracked object mark the surface. Under 12 cm: "VERY CLOSE - switch to the brush (4) or precision tool (3)", rings turn red.
- Tiers (data: `Resources/Excavation/DepthProbe.asset`, `DepthProbeDefinition`): Mk I 1 object, bands only (<10 cm, 10-30, 30-60, deep), 1.5 m across / 1.2 m down; Mk II 2 objects, metres +/-8 cm, 2.5 m / 2 m; Mk III 3 objects, +/-3 cm, 3.5 m / 3 m. Error is stable per object.
- Crafting (ship): Depth Probe (Tools: 1 alloy plate, 2 wiring, 1 circuit), Mk II (Upgrades: 2 circuit, 1 gold ingot), Mk III (1 sensor array, 2 circuit, 1 platinum ingot). Upgrade id `scanner.depth_probe`, saved as `Ship.depthProbeLevel` (older saves = Mk I). In the DEV testing loadout.
- Setup: `Tools/Xenoarch/Setup/Depth Probe (item, tiers, recipes)` / `Temp/xenoarch_depthprobe.trigger` creates the item, definition and recipes and appends them to the registry / config; Batch E crafting setup calls it too.
- Journal entry "How Deep Is It?" (FirstDigSite) explains it and the upgrades.

## 2026-11-26 (night) - t131 (ZEN-15): survey journals - datapad pickups, note reader, inventory Journals (UNTESTED)

Nothing here has been run in Play Mode or tested. Compiles in the open Editor.

- Data: `JournalEntry` assets in `Resources/Journals` (title, author, date line, category, body, placement, optional body id / site index, order). 14 entries that teach real mechanics only (shuttle, saves, helmet, shovel damage vs precision/brush, scanner pulse, backpack/hotbar/crates, refined flags, hardness/drill, maps, condition/lab, star map pieces, caches, Moon and Mars gravity).
- Pickup: `JournalPickup` datapad (code-built slate with a glowing teal screen and a soft light), E "Read journal: <title>". Picking it up adds it to the Journals list and opens the reader; a collected entry hides its copies everywhere.
- Reader: `JournalReader` (UITK datapad: header, title, author | date, scrolling body). Frees the cursor and pauses; E / Esc close; Q / R or arrows (and Prev / Next) page through the collected list. Opening marks an entry read.
- Inventory: JOURNALS section with count, unread count and NEW tags; click to read.
- Save: new key `journals.collected` (id, read, time). Older saves load with none collected.
- Placement: `Tools/Xenoarch/Journals/Place Journals` (trigger `Temp/xenoarch_journals.trigger`, log `Logs/xenoarch_journals.txt`) puts `XA_JournalPickup` prefab instances under a `Journals` root in every landing scene from the galaxy data: BaseLanding (ring 11 m around the base zone), FirstDigSite (just outside the nearest site's fence), SiteLanding (11 m from the Nth site's zone). Additive and rerunnable; skips scenes with unsaved changes. The site layout menu runs it too, so future planets get journals. Ship entries spawn at runtime in front of the default ship spawn (Ship_Starter is not edited); NearCache entries spawn at runtime near the cache closest to the base zone; any entry missing from a scene is spawned at runtime as a fallback.
- Add more: create a JournalEntry asset in `Resources/Journals`, set placement (and bodyId / siteIndex if needed) and order, then run Place Journals (or rely on the runtime fallback).

## 2026-11-26 (night) - t135-t136 (ZEN-13): M map hid the minimap, hotbar shift, no inventory on the Moon; minimap to the corner (UNTESTED)

Nothing here has been run in Play Mode or tested. Compiles in the open Editor.

- Cause: `MinimapHud` put its UIDocument on the XenoarchRuntime object itself. Every other runtime UI (inventory, M/N maps, pause menu, helmet HUD, journal reader) builds its UIDocument on a child of that object, and Unity nests a child object's UIDocument inside the parent object's UIDocument. So all of them lived inside the minimap's panel, and the minimap cleared its panel (`root.Clear()`) on every scene load. After landing on the Moon, the inventory and the maps window still "opened" (cursor freed, input blocked, minimap and helmet hidden, so the hotbar dropped to the no-helmet position) but had nothing to draw, so nothing appeared.
- Fix: the minimap now has its own child object (`[Minimap HUD]`), so no UI is nested in it and a scene load only clears the minimap. Closing the map restores the HUD exactly (minimap, helmet pods and hotbar inset come back as before).
- Helmet overlay (t139): same cause. Its UIDocument was nested in the minimap's panel too, so the overlay was wiped on the first scene load and never rebuilt; H only toggled an invisible element. It is now top-level again and also rebuilds itself if its element is ever detached. It shows outdoors where the air is not breathable (Moon, Mars), H toggles it, and it never takes input (picking ignored).
- Minimap: 20 px from the top and right edges (was 100 px). With the helmet visor up it sits just under the top-right ENVIRONMENT/CO2 pod. The scanner panel still stacks under it.

## 2026-11-26 (night) - t126-t127 (ZEN-14): spread dig sites and landing zones, more caches and resource nodes, dig yield x0.4 (UNTESTED)

Nothing here has been run in Play Mode or tested. Everything compiles in the open Editor with no `_Project` CS errors.

- New Editor menu **Tools/Xenoarch/Landing/Spread Sites and Landing Zones** (`XenoarchSiteLayout`, trigger `Temp/xenoarch_sitelayout.trigger`, log `Logs/xenoarch_sitelayout.txt`). For each Mars and Moon dig site it picks flat, open ground well apart from the other sites and from the base zone, inside the terrain edges. It moves the site definition and the site's LandingZone marker, with its LandingZone_ child (the shuttle and the arrival spawn), then re-grounds the shuttle and re-bakes the surface map and landing hints. The base zone is not moved. The rules are data in `XenoarchConfig.siteLayout` (spacing 280 m, at most 520 m from base, 180 m edge margin, max slope 9 deg; relaxed step by step if nothing fits). Idempotent: a scene that is already spread is left alone.
- Moon is laid out and saved: Deep Echo is 326 m from base, Ridge Vault 260 m, Tranquility Cache 323 m, and the sites are 250-523 m apart. Each landing zone is about 27 m from its site. Surface map re-baked to 684 m.
- Mars is laid out and saved (after Anthony saved the scene): Buried Spire is 505 m from base, Canyon Relay 491 m, Dune Shrine 453 m, and the sites are 592-997 m apart. Each landing zone is 26-31 m from its site. Surface map re-baked to 952 m. The Mars scene marks itself dirty again right after saving, so the map bake now only reads the scene (`WithScene(readOnly)`) and no longer skips an open scene with unsaved changes.
- Shuttle menu: every landing zone is listed and can be picked, including zones near sites that have not been scanned yet ("Landing zone N (unsurveyed)"), and it is shown on the map. Scanned sites keep their old entries.
- Caches: 7 spread over the whole surface map plus 1 around each landing zone (30-110 m ring), so 11 per planet with 4 zones (`valuableSites.perBody` / `perLandingZone` / `zoneRing`). Loot rules are unchanged. Caches already found in a save keep their ids, but ids map to new positions.
- Resource nodes: each material spawns `nodesPerScene x resourceNodeMultiplier` (3) nodes. 35% are placed around landing zones (14-120 m) and the rest across the surface map, at least 8 m apart, off steep ground (32 deg) and clear of sites and zones.
- Dig yield: `XenoarchConfig.digYieldMultiplier` = 0.4 (60% less) scales the strata volume that is added up before materials are given out, so fractions carry over between strokes. It applies only to materials found while digging (StrataYield.Dig via DigTool), including digging at cache pits. Cache loot, artifacts and resource node pickups are unchanged.
- Standard for future planets and systems (t130): the layout menu and the map bake now find every body with a landing scene from the galaxy data, instead of a fixed Mars/Moon list. They create missing base and site landing zone markers and build their shuttles. Documented in XENOARCH_Galaxy_Architecture.md, section "Surface layout standard". Mars was re-saved by Anthony at 3:35 AM; the layout on disk matches commit 2c664e1.
## 2026-11-26 (night) - t115-t125: shader warm-up, preplaced landing zones, dig lumps, aim readout, pit lighting, minimap (UNTESTED)

Nothing here has been run in Play Mode or tested. Everything compiles in the open Editor with no `_Project` CS errors.

### Shader warm-up (462b93d)
- Cause of the blue (cyan) flashes: the Editor's async compile was only requested for each material's own keywords; the variant actually drawn adds URP's scene keywords (shadows, fog, lights), so dig-site and spawned materials still compiled on first sight.
- The loading screen now draws every tracked material once on tiny hidden quads in front of the real camera (scene lighting) and waits for those compiles (up to 25 s). Includes scene, Resources/prefab/Data dependencies (shuttle, caches, pickups, VFX), code-made materials (site markers, borders, flags, soil, artifacts, nodes, FTL, sun disc, dig ring decal).
- Artifact and resource-node materials are shared per definition instead of one per spawn.
- The build collection adds the URP global-keyword variants. Rebuild: `Tools/Xenoarch/Rendering/Rebuild Shader Warmup Cache` (or `Temp/xenoarch_shaderwarm.trigger`). Preloaded Shaders not used (the warm pass covers it without slowing every launch).
- Dev: `XenoarchConfig.logShaderWarm` logs `[ShaderWarm]` lines for shaders compiling outside the loading screen.

### Landing zones (f121e4d)
- Log cause: the runtime-landed shuttle was placed at y -256.8 with the player at -23.6 (233 m underground) by the runtime terrain-height sample during load; gear offset (-1.54 m) and edit-time heights were correct. Also the prefab's bay script would have given the "fly to Mars" menu instead of "Board shuttle: return to ship".
- `Tools/Xenoarch/Landing/Build Landing Zones` adds under each LandingZone marker a `LandingZone_<id>` with a preplaced Mark2Shuttle (grounded on its gear) and an `XA_Player Spawn (shuttle)`; `LandingZoneSet` turns on only the chosen zone. Additive and idempotent (never re-positions zones). Built in Mars and Moon (4 zones each). Runtime spawning is only a logged fallback (now grounded from real renderer bounds). Dev F7 summons the shuttle.
- Mars: Dune Shrine, Canyon Relay and Base markers are ~3 m apart, so their three shuttles stack in the Editor (one shows in play). Not duplicates.

### Digging, HUD, lighting (this commit)
- Lumps/peaks in pits: the grid already counted that ground as dug, so aims more than ~3 cells above a column's floor were skipped and no tool did anything. Aims above the dug floor now trim the leftover where aimed (all tools; harder bands take smaller bites; never below the grid floor).
- Dig HUD: `Aiming:` / `Digging:` with the band at the actual aim depth, hardness, and tool OK / slow / needs a tool rated N+, plus "leftover lump" when levelling.
- Pit shadows: cause is the sun's directional shadow (full strength) into the pit lit only by flat ambient (Moon ambient was 0.03). SSAO is mild (0.4), not the cause. Now: `EnvironmentProfile.sunShadowStrength` (Mars 0.7, Moon 0.8), lighter ambient (Mars 0.55/0.40/0.31, Moon 0.10), ambient probe refreshed, and a shadowless pit fill light per dug site (`XenoarchConfig.pitFillIntensity`, 0 = off). Surface shadows stay.
- Minimap top-right with 100 px margins (top and right); the scanner stacks under it; with the helmet visor up the column starts below the pods.

## 2026-11-26 (later) - t108-t111: digging, shuttle, maps, HUD, drag & drop, post, helmet, Sol terrain (UNTESTED)

Nothing here has been run in Play Mode or tested. Everything compiles in the open Editor with no `_Project` CS errors.

### Digging (88236ff)
- Root cause of shallow/spiky holes: Digger Master voxel height 1 m against ~4.6 cm horizontal voxels. `DiggerExcavationSurface` now sets a 0.1 m voxel height at runtime before Digger initialises (scenes untouched; there was no saved voxel data) and enforces a minimum carve radius. Start logs `[Xenoarch] Digger voxels: ...`.
- Hard bands: the shovel and precision tool cut every Sol layer. Hardness above the tool rating now slows digging linearly (was squared) with a guaranteed minimum bite, so progress is always visible; the drill is faster. `SoilLayer.minToolRating` (0 = any tool) lets future sites require better tools; the HUD says which tool is needed.
- DEV/TESTING: `XenoarchConfig.devTestingLoadout` (on) gives every dig tool and the scanner on New Game, Continue and Load. Untick for release.

### Shuttle (861260e)
- Landing is exception-safe (a failure still finishes the load), logs `[Xenoarch] Shuttle placed at ...` and a watchdog re-checks it 3x after loading (re-lands, re-enables, re-grounds or moves it near the player). The original cause was not confirmed (no play log); the new log line will show it.

### Nav, maps, HUD (c98bdd5)
- Nav terminal: no "Land" on planets; landing is from the shuttle only. Stations still dock/undock.
- M opens the big map (Nav-terminal size), cursor shown, game paused (timeScale 0) and restored on close. Tabs: Surface (M) and Dig Sites (N) with per-site dig maps and spot codes.
- `HudLayout`: minimap top-right, scanner panel stacked under it with a gap (no overlap at any resolution).

### Inventory drag & drop (5a63d23)
- New saved hotbar (`player.hotbar`, 8 slots, keys 1-8; old saves get the old default 1 scanner, 2 shovel, 3 precision, 4 brush, 5 drill). Drag backpack items onto hotbar slots 2-8, drag hotbar slots to swap or drag them off to clear. The HUD strip shows the hotbar.
- Drag within the backpack swaps slots. Storage crates: drag stacks between the backpack and crate columns (clicking still works); in the inventory window, drop a backpack item on a crate line while aboard.

### Post-processing (6d3fb70, e2cd06c)
- Like ASH's runtime post stack: a global base volume (ACES tonemapping, gentle bloom, colour adjustments, subtle vignette) in scenes without their own global volume (surfaces, stations); the ship keeps its authored volume. Editable profile `Data/Rendering/VP_Xenoarch_Base.asset` (created once by the editor, assigned to `XenoarchConfig.basePostProfile`; `basePostEnabled` toggle). Fog stays per body in the environment profiles: Mars light dust haze (exp2 0.004), Moon none.

### Helmet visor (6e1ca88)
- UITK overlay using the supplied reference art (`Resources/UI/Helmet_Visor.png`, baked bars removed; original in `Art/UI/Helmet`). Live pods: OXYGEN, NITROGEN (top-left), ENVIRONMENT (temperature, gravity, atmosphere) and CO2 (top-right), SUIT INTEGRITY (bottom). On outdoors where the air is not breathable, off aboard the ship and in stations, `H` toggles. Minimap/scanner move below the pods and the hotbar lifts above the suit pod while it is up.
- Readings are display-only for now: O2/N2 drain slowly outside and refill aboard, CO2 rises with sprinting, suit integrity stays 100% (no damage system yet). Not saved.

### Sol terrain surfaces (2308231)
- CC0 4K PBR sets (ambientCG / TextureCan, copied from Genesis' `Terrain_HiRes_4K`), regraded to Mars rust and Moon grey at 2048 px with URP masks (AO, height, smoothness): `Art/Terrain/SolPBR`. Licences in `Data/Terrain/SOURCES.md`.
- `Tools/Xenoarch/Terrain/Apply Sol PBR Surface Layers`: slot 0 regolith, rock on slopes (20-34 deg), Mars cracked-crust patches, PBR dig-wall soil, then Digger's own Sync & Refresh. First application only; re-running keeps your painting. TerrainLayer assets in `Data/Terrain/Layers` are created once and never overwritten.
- Editing: the terrains are normal Unity Terrains with TerrainData and TerrainLayer assets in `Data/Terrain`; no runtime or setup code writes them (the Phase 1C setup only builds a terrain when none exists). After changing layers, textures or heights, run Digger's sync: select the Digger Master and press "Sync with terrain(s) & Refresh", or `Tools/Xenoarch/Terrain/Resync Digger (open scenes)`, then save the scene.
- Applied this pass: Mars [0 TL_Mars_Regolith, 1 Mars_Soil (dig walls, unchanged index), 2 TL_Mars_Rock, 3 TL_Mars_CrackedCrust]; Moon [0 TL_Moon_Regolith, 1 Moon_Soil, 2 TL_Moon_Rock]. Digger synced (its sync re-imports its URP shaders and logs "Shader Unsupported ... All subshaders removed" import warnings for Digger's own shaders).
- Note: the new texture import rule (first import only, SolPBR folder) made Unity re-import project textures once after the deploy (several minutes).

## 2026-11-26 - Loading/shuttle fixes, space visuals, FTL tunnel (UNTESTED)

Nothing here has been run in Play Mode or tested. It compiles cleanly in the Editor, with no shader import errors.

### Bug batch (commit c6e3078)
- Loading screen starts at 0% and fills in stage order (load 30% / materials 45% / gates 17% / settle 8%). "Compiling shaders" text removed.
- Shader wait: keeps waiting while progress advances (20 s stall limit, 180 s hard cap). Passes that never compile are skipped and remembered. The give-up log now states the reason.
- Distinct titles: New expedition, Loading saved game, Main menu, In-system transit, Landing/Docking, Aboard the ship. "Returning to the ship" is used only for the shuttle ascent.
- Landed shuttle is re-placed on flat ground within 40 m of the player after load, continue or autosave restore.
- Removed the red/crimson bottom-bar gradient (`.xa-bottom` in Xenoarch.uss).

### Space visuals
- `StarPhysics`: per-star radius and luminosity. Sun angular size is 2·atan(R/d); brightness scales with sqrt(flux) using the body's distance in AU.
- `SunDirector`: a `[Sun]` disc plus a URP Lens Flare (SRP) in every scene. Surface lights are scaled by flux; the ship gets a `[Sun Light]`. Hidden during FTL.
- Skies: `Sky_SpaceMilkyWay` (NASA SVS starmap, config default, vacuum/icy envs), `Sky_MoonSurface` (black sky with stars) and `Sky_MarsSurface` (thin dusty day sky). Old 6-sided skies have exposure lowered to 0.62. Ship ambient is scaled ×0.7 (`XenoarchConfig.shipAmbientScale`).
- `OrbitViewDirector` and `OrbitViewDefinition`: a real planet sphere plus an atmosphere shell outside the helm window. Earth, Moon and Mars use Solar System Scope CC BY 4.0 textures; Jupiter and the exoplanet arrivals use PlanetPack02. The `XenoarchConfig.preferOrbitViews` toggle can be switched off to restore the kit orbit skies.
- PlanetPack02 is copied from Genesis to `Assets/_Vendor/PlanetPack02` (gitignored). Our materials use URP shaders.
- Licences are listed in `Art/Space/SOURCES.md` and `Art/Skyboxes/Textures/SOURCES.md`.

### FTL
- The tunnel has two layers: an outer gold vortex and an inner cyan/violet layer spinning faster. It adds a cyan-white core with a violet ring and about 9× more white star streaks; bloom is stronger. Velocity curve modes are unchanged.
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
