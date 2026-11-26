# XENOARCH — Development Roadmap

_Status legend: Planned · Implemented · Tested · Incomplete._

| Phase | Scope | Status |
|---|---|---|
| **1A Foundation** | Reuse audit, architecture, docs, folder structure, asmdefs, bootstrap + scene management with fades, controller adapter (vendor FPC pending), interaction, stable-id item definitions, inventory, versioned save, 6 placeholder scenes, smoke check + EditMode tests, git | Implemented — **awaiting Editor compile/verification and Anthony's review** |
| 1B Exploration | Celestial body + star system definitions (Sol), environment profiles + gravity, navigation terminal UI, travel via ship state, ship interior stations wired, orbital scan + handheld surface scan, archaeological site definitions; vendor FPC integration | Planned (do not start until 1A review) |
| 1C Excavation & research | Layered-cell diggable volume (see Excavation Design), artifact definitions (~30) and instances, condition/damage, research + translation, civilization, discovery chain evaluator, Moon→Mars chain | Planned |
| 1D Economy & progression | Credits, vendors, trade hub, equipment + ship upgrade purchases, fuel, notoriety + unlocks, recoverable alien tech (sensor module), discovery journal UI | In progress (2026-11-26): credits, notoriety, Trade Hub vendors, refuelling and upgrade purchases in code, not yet played. Alien tech module and journal UI next |
| 1E Integration | Full loop playthrough (21 acceptance criteria), persistence validation, scene transition tests, compile clean, limitations doc | Planned |

## Recommended order inside 1B
1. Vendor FPC adapter (unblocks real walkthrough testing).
2. Body/system definitions + `galaxy.discovery` state + nav terminal (UI Toolkit).
3. `ship.state` (fuel) + travel rules.
4. Environment profile + gravity.
5. Orbital scan (data-driven anomalies, progressive reveal) → site definitions.
6. Handheld surface scan (detect buried items in a site volume; feeds 1C).

## Planned: map-driven travel with cockpit FTL sequence (Anthony, t29u). Plan only, not built
1. **Pick destination.** On the Navigation Terminal map, select a body and press Plot course. This stores a *plotted course* in ShipState (target id, fuel cost) without moving the ship yet. Phase 1B's Travel button becomes Plot course.
2. **Walk to the cockpit.** The cockpit pilot seat or console (`Cockpit_Console`) gets an Engage interactable. Prompt: "Engage drive: <destination> (fuel X)". Engaging locks player input and seats the player facing the front window.
3. **FTL through the front window.** A `ShipTravelSequence` component in Ship_Starter runs a timed sequence (about 4-8 s, scaled by distance, skippable):
   - spool: afterburner glow plus engine audio
   - jump: stretched star streaks and dust rushing past the window, with camera shake, Bloom/Lens Distortion/Chromatic Aberration ramped on a dedicated URP Volume, and an FOV kick
   - drop-out: flash, then the arrival sky
   Creepy Cat FX to reuse (already converted to URP Particles):
   - `Prefabs/Fx/Particle_Dust_Space` for the dust stream
   - `Particle_StarsZone_01` for the star field, with velocity-stretched renderer
   - `Particle_Afterburner(_Small/_Big Variant)` for the engine glow
   - `P_Stargate_Warp_01` as an optional tunnel mesh
   Placed outside the front window, parented to the ship.
4. **Arrive.** `ExplorationService.CommitTravel` runs at the jump midpoint (fuel spent, discovery updated). The sky swaps to the destination's `orbitSkybox` (`EnvironmentDirector.ApplySky` + `DynamicGI.UpdateEnvironment`), so no scene reload is needed. Input unlocks and the orbital scanner/airlock act on the new body.
5. **Sky per location (done in 1B).** `CelestialBodyDefinition.orbitSkybox` per body, `XenoarchConfig.defaultSpaceSkybox` for deep space, `EnvironmentProfile.skybox` for surfaces. Materials are in `Assets/_Project/Art/Skyboxes` (Skybox/6 Sided, built from Creepy Cat `_Skyboxes` faces): DeepSpace, Starfield, EarthOrbit, MoonOrbit, MarsOrbit, IcyOrbit.
6. **Later.** Interstellar jumps reuse the same sequence with a longer tunnel (FTL range check from ShipState). The exterior `Fake` ship is used for any third-person departure/arrival shots.

## Expanded scope (requested 2026-11-25), in delivery order
- **3A (done):** shovel depth/hold-to-dig, dig aim decal ring, loading screen + shader warm-up.
- **3B (done 2026-11-25, not yet play-tested):** airlock -> "Take Shuttle"; landed craft is a copy/instance of the Creepy Cat Mark 2 Shuttle (vendor prefab untouched; hangar later). Landing-zone choice; auto-pick of semi-flat ground near the dig site (slope threshold, footprint clearance, min distance from the grid); per-site `LandingZone` marker (gizmo, editable, saved in scene) overrides auto-pick. E at the shuttle fades back to the ship bay spawn. Scanner direction indicator to the nearest site.
- **3C (done 2026-11-25, not yet play-tested):** solar-system map rotate/drag/zoom; galaxy map unlocked by discovery-chain info; orbit-scan terrain view with dig sites as red triangles and a selectable landing site near each.
- **3D (done 2026-11-25, not yet play-tested):** per-planet minimap (orbit-scanned sites, random sites, shuttle); random valuable sites with data-driven loot tiers up to rare/super rare, handheld-scanner only; scan radius (orbital + handheld) as an upgradable stat.
- **Ship pass (done 2026-11-25, not yet play-tested):** shuttle bay lift (Down Position marker, either console, 3D audio), three role crates with hinged lids and upgradable slots, Refiner hookup, bay shuttle flight menu, landed shuttle with gear, return beside the bay shuttle, map info cards, longer landing hold.
- **3E (done 2026-11-25, not yet play-tested; Digger texture layers per band still to do):** layered voxel materials (Digger texture layers) yielding raw materials refined into rare metals; ship hold crate (sell/trade/craft); UITK grid inventory with stacking and transfer; basic crafting for ship/tool upgrades (ASH pattern, reimplemented); white openly-licensed icons logged in SOURCES.md.
