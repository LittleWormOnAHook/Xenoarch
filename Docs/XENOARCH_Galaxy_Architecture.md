# XENOARCH — Galaxy Architecture

_Status: **Planned** (Phase 1B). Phase 1A provides the foundations it builds on: `DefinitionAsset` (stable ids), `DefinitionRegistry<T>`, save sections, `WorldPersistence`._

## Hierarchy
Galaxy → Star systems → { Stars, Planets, Moons, Asteroid fields, Orbital stations, Anomalies, Surface landing zones → { Archaeological sites, Excavation locations, Underground structures, Artifacts/discoveries } }

## Definitions (ScriptableObjects, all `DefinitionAsset`)
| Type | Key fields |
|---|---|
| `GalaxyDefinition` | id, seed, list of star systems (authored) |
| `StarSystemDefinition` | id (`sys.sol`), name, galactic coordinates (Vector3, ly), stellar classification, bodies, stations, travel requirements (min FTL range, fuel), environmental notes, archaeological potential (0–1), generation seed, discovery default (known/unknown) |
| `CelestialBodyDefinition` | id (`body.sol.moon`), kind (star/planet/moon/belt/station), parent id, orbit placeholder (distance AU, period) for map layout only, environment profile id, gravity (g), scene name (if landable), landing zones, orbital anomaly table |
| `LandingZoneDefinition` | id, body id, display name, map position (lat/long), scene name, spawn id, sites |
| `ArchaeologicalSiteDefinition` | see Artifact & Research doc / Excavation doc |
| `EnvironmentProfile` | atmosphere, temperature band, radiation, visibility, gravity, required equipment tags |

Distances between systems are computed from coordinates (no stored matrix).

## State (save sections, separate from definitions)
- `galaxy.discovery`: known system ids, known body ids, scan levels per body (none/system/orbital), known coordinates (from research; may be partial: `{targetId, precision 0–1, sourceArtifactInstanceId}`).
- `ship.state`: current system/body/zone, fuel, installed modules (Economy doc).
- Visited flags and timestamps for the journal.

## Procedural generation (future)
`id = Hash(parentId, galaxySeed, index)`; content generated from (definition template, seed) so it is identical every session; only discovery/changes are saved. Handcrafted and generated systems share the same definition types; generated ones are created in memory at load from the seed.

## Navigation and travel (Phase 1B)
- Ship navigation terminal lists known destinations (system → body → landing zone), shows requirements (fuel, range) and locked/undiscovered entries.
- Travel = validate requirements → spend fuel → fade/loading via `XenoarchRuntime.Travel(scene, spawnId)` (Implemented in 1A, currently used by debug pads) → update `ship.state`.
- Ship scene is the hub: in-system travel keeps the player in `Ship_Starter`; landing loads the landing zone scene; the hub docks into `TradeHub_Sol`.

## Surface layout standard (every planet and system, from ZEN-14)
Every body with a landing scene follows the same layout. It comes from data and one Editor menu, with no per-scene code.

**Inputs (data only)**
- `CelestialBodyDefinition.sceneName`: the body's landing scene. A scene counts as a surface if it has a Terrain.
- `ArchaeologicalSiteDefinition` entries in the site registry with that `sceneName`: the dig sites.
- The scene's default `PlayerSpawnPoint`. It becomes the base landing zone when the scene has no base `LandingZone` yet.

**Menu: Tools/Xenoarch/Landing/Spread Sites and Landing Zones** (trigger `Temp/xenoarch_sitelayout.trigger`, log `Logs/xenoarch_sitelayout.txt`)
1. Finds every surface scene from the galaxy data (`XenoarchSiteLayout.SurfaceScenes`), so new planets and systems are picked up automatically.
2. Creates any missing markers: a base zone on the default spawn, and one `LandingZone` per dig site.
3. Spreads the sites using `XenoarchConfig.siteLayout`:
   - at least 280 m between sites and from the base zone;
   - at most 520 m from base;
   - at least 180 m from the terrain edges;
   - flat ground (slope 9 deg or less, 1.6 m relief within 14 m);
   - no scene colliders in the footprint.
   The rules are relaxed step by step only when nothing fits. The base zone never moves.
4. Places each site's landing zone about 22 m outside the site, facing it. The zone's shuttle and arrival spawn move with it and the shuttle is re-grounded.
5. Builds the shuttle and arrival spawn for new zones (`XenoarchLandingZonesBuilder`).
6. Re-bakes the surface map and landing hints. The map grows to cover every site and zone.

It is idempotent: a scene that is already spread is left alone. It skips scenes open with unsaved changes, except for the read-only map bake.

**Runtime (generic per body, no scene code)**
- Shuttle menu: lists the base zone and every landing zone. Zones whose site has not been scanned show as "Landing zone N (unsurveyed)". Choosing one lands at that zone (`LandingZoneSet`).
- Caches (`XenoarchConfig.valuableSites`): `perBody` (7) spread over the surface map, plus `perLandingZone` (1) in a `zoneRing` (30-110 m) around each zone. That gives about 11 per body with 4 zones. They are spaced 40 m apart and kept 18 m clear of sites and zones. Loot stays mostly minerals.
- Resource nodes: each raw material spawns `nodesPerScene x resourceNodeMultiplier` (3) nodes on the bodies listed in `MaterialDefinition.bodies`. `resourceNodesNearZones` (35%) of them go 14-120 m around landing zones and the rest across the surface map. They are 8 m apart, on slopes of 32 deg or less, and clear of sites and zones.
- Dig yield: `digYieldMultiplier` (0.4) scales the strata volume added up per stroke, so fractions carry over between strokes. It affects only materials found while digging (`StrataYield.Dig`), not cache loot, artifacts or node pickups.

**Adding a planet:** give the body its `sceneName`, add its site definitions, add the scene to Build Settings with a Terrain and a default spawn, then run the menu and save.