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
