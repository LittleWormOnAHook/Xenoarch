# XENOARCH — Game Design (Phase 1 baseline)

**Developer:** Dark Matter Interactive · **Engine:** Unity 6 URP · **Framework:** Living World Engine
**Genre:** single-player sci-fi archaeology, exploration, research and economic simulation · **View:** first person
**Tagline:** Discover the past. Unlock the future. — *Every artifact tells a story. Every story leads somewhere.*

_Status legend: Planned · Implemented · Tested · Incomplete. As of Phase 1A everything below is **Planned** unless noted._

## Premise
Several centuries from now humanity has FTL travel and settlements across Sol and other systems. Ruins, artifacts, documents and technology of extinct civilizations lie undiscovered across the galaxy. The player is an independent xenoarchaeologist with a modest ship, basic tools, few credits and no reputation. No mandatory combat, no linear campaign, no character levels: progress comes from discoveries, knowledge, equipment and notoriety.

## Core loop
Select destination → travel → orbital scan → identify anomalies → choose landing site → surface survey → locate buried finds → excavate and recover → return to ship → clean/identify/translate/research → sell, keep or use → earn credits, knowledge, notoriety → upgrade equipment and ship → unlock destinations → repeat.

## Pillars
1. **Discovery is information.** Scans and research reveal progressively; nothing is identified instantly.
2. **Care matters.** Tool choice near fragile finds changes condition and value.
3. **Knowledge persists.** Translations and discoveries stay in the journal even after an artifact is sold.
4. **Interconnected stories.** Discovery chains link artifacts, coordinates and sites across worlds, non-linearly.
5. **Manageable scope.** Placeholder-first, data-driven content, solo-dev friendly.

## Phase 1 content
- Sol system data: Sun, Mercury, Venus, Earth, Moon, Mars, Asteroid Belt, Jupiter, Io, Europa, Ganymede, Callisto, Saturn, Titan, Enceladus, Uranus, Neptune, Pluto (navigational entries; only Moon and Mars playable). — Planned (1B)
- One orbital trade hub (TradeHub_Sol). — Scene placeholder Implemented (1A); vendors Planned (1D)
- One fictional civilization; ~30 placeholder artifact definitions; one Moon → Mars discovery chain ending in extrasolar coordinates; one recoverable technology (alien sensor module improving the handheld scanner). — Planned (1C/1D)
- Starter ship interior with cockpit/navigation, orbital scanner, research lab, artifact storage, equipment storage, engineering, airlock. — Greybox placeholders Implemented (1A), functions Planned (1B–1D)

## Player
First-person, controller supplied by a **vendor FPC package (pending)** behind the `IPlayerRig` adapter. Phase 1A scenes use a static capsule placeholder.

## Starting kit (Phase 1A data, Implemented)
Handheld Surface Scanner Mk I, Sonic Excavator (its Fine focus is the former Precision Excavation Tool), 5 Sample Containers. The Ultrasonic Brush is a pickup in the ship (used to test world persistence). The Sonic Burster (the former Basic Powered Drill) exists as a definition for purchase later.

## Environment
Environment profiles (atmosphere, temperature band, radiation, visibility, gravity) gate equipment requirements; no survival micromanagement. Configurable gravity per location. — Planned (1B)

## UI direction
Dark graphite/muted metal backgrounds, white/pale-blue text, subtle amber highlights, instrument-like graphics, minimal clutter. Phase 1A uses IMGUI placeholders only.

## Out of scope for Phase 1
Procedural galaxy, seamless planets, flight physics, multiplayer, combat, large NPC simulation, crew, dynamic economies, station building, cinematics, final art, full terrain deformation.
