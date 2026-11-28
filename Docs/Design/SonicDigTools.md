# Sonic Dig Tools — Design Plan

_Status: **Proposed** (plan only, no gameplay code). Written 2026-11-27 (America/New_York). For Anthony's approval before any implementation. Related: `Docs/XENOARCH_Excavation_Design.md`, `Docs/XENOARCH_Economy_And_Progression.md`, `Docs/XENOARCH_Artifact_And_Research_System.md`._

Concept mockups (not in-game, pending visual approval) live on the box at `/workspace/sonic_tools_mockups/`. They are not in this repo until you sign off.

---

## 1. Intent

Xenoarch is set in **2187**. Mechanical shovels and brushes feel wrong next to sonic and electronic field kit. This plan replaces the dig *feel* — beams, waves, dust, energy and heat, upgrades, and found alien add-ons — without throwing away the excavation systems that already work.

Anthony's brief (t332u, t333u):

- Sonic excavator and the other tools kick up a little dust and small rock chunks around the dig spot.
- The excavator shows a **beam that extrudes to a variable round chisel**, using a sphere and other shapes in a semi-transparent volume (SphereCollider or the like).
- Sonic precision uses a **visual wave or multi-line rendered lines** that leave the device at a fine point, spread out, hit different points of the ground, and then remove / deform the soil layers.
- Tools can be **upgraded** (precision, depth, and other features) and must **look futuristic**.
- **Add-on modules** are crafted or found at alien locations. Rare alien variants of the tools themselves can also be found.
- **Blueprints** are earned, found, or produced by researching alien fragments and other items.

The archaeology stays careful and skill-based. Power and focus still trade speed against the risk of cracking a find. Energy and heat replace tool durability.

---

## 2. What stays, what changes

The hybrid already in the project is the right foundation. This plan does not rewrite it.

| Layer | Owner today | What this plan does |
|---|---|---|
| Gameplay truth | `ExcavationGrid` / `ExcavationSite` (cell fill, layers, hardness, artifact exposure and condition, `excavation.sites` save) | Same rules, same save. New tools feed `DigToolSpec` (radius, removal, maxHardness, damage, crude, rate, cells, minBite) plus energy, heat, head shape, and module flags. |
| World mesh | Digger PRO via `IExcavationSurface` / `DiggerExcavationSurface` | Still one (or a few) buffered carves per stroke. Extend the adapter to pass **brush type** (Sphere, RoundedCube, Stalagmite, HalfSphere, Custom). **No vendor Digger files are edited.** |
| Aim | `DigTool` (3 m reach, LMB / RT hold, keys 2–8 from the hotbar, `DigAimRing`) | Keep hold-to-dig. Replace the flat aim ring, while a sonic tool is out, with the beam / wave preview. Ring can remain for non-sonic tools or as a faint ground contact mark. |
| Yield | `StrataFinds` on accepted strokes | Unchanged. |
| Lab cleaning | `ResearchOp.Cleaning` on the ship | Unchanged. Field "clean %" on a find is a new, separate meter (soil still on the surface), not a substitute for lab cleaning. |

Digger's current path (ZEN-8) queues **one full-strength sphere per stroke**, centred on the cut columns, with a pending buffer cap of 24. That budget is a hard constraint on the precision fan (see §8 and §16). Runtime voxel height is 0.1 m; `MinRadius` is about 1.5 voxels (~0.15 m or more). Preview heads must not advertise a cut smaller than Digger will actually carve.

---

## 3. The tool set

Proposed field kit. Dig tools keep the existing hotbar keys and **keep their item IDs** so current saves, recipes, and starting kits still work (display names and held meshes change). New support tools get new IDs.

| Tool | Replaces / extends | Item ID (proposal) | Default hotkey | Role |
|---|---|---|---|---|
| **Sonic Excavator** | Excavation shovel | `item.tool.excavation_shovel` | 2 | Bulk removal. Beam + shape-shifting chisel head. Power and Focus. Crude: can jar or damage visible (and nearly visible) finds. |
| **Precision Emitter** | Precision excavation tool | `item.tool.precision_tool` | 3 | Controlled mid-work. Narrow beam, small head or a short 3–5 line fan. Never crude. The gap between bulk and brush. |
| **Ultrasonic Brush** (sonic precision) | Excavation brush | `item.tool.brush` | 4 | Last centimetres and surface skim. Fine-point fan of lines, thin wide bites, **clean %** on the aimed find. Never crude. `maxHardness` 1 (dust / loose only), same as today's brush. |
| **Resonance Burster** (optional) | Powered drill | `item.tool.powered_drill` | 5, or a module on the excavator | Ice / rock / compacted bands (`maxHardness` 4). See open question 2. Until decided: keep as a separate crude tool with a sonic look and a tight cone / stalagmite head. |
| **Ground-Penetrating Scanner** | Visual upgrade of the Depth Probe (not a new hotbar tool, proposal) | stays `item.tool.depth_probe` | 6 | Short-lived hologram of buried *shapes and density*, plus today's depth / offset readout. Still never names the object. The handheld surface scanner (key 1) stays the site-finder. See open question 1. |
| **Resonance Probe** | New | `item.tool.resonance_probe` | 7 (or next free hotbar slot) | Pulse a find or a soil patch: material and a rough age band in the field. Rare alien materials "sing". Does not replace ship-lab `MaterialAnalysis` / `AgeEstimation` — those stay the authoritative results. |
| **Stasis Lifter** | New, augments hold-E recovery | `item.tool.stasis_lifter` | 8 (or next free) | Cradles a fragile find so it can be lifted earlier or without the pry penalty, at an energy cost. Existing hold-E recover remains if this tool is not equipped. |

Starting kit on a new game: Sonic Excavator + Precision Emitter + Ultrasonic Brush (same three items the player already owns, renamed). The drill, probe, GPS hologram, Resonance Probe, and Stasis Lifter stay craft, upgrade, or find gated, as they are today or as new recipes.

### 3.1 Sonic Excavator

**Role.** The workhorse. Walk the pit, cut loose and compacted soil in chunks, stop when something solid answers.

**Base `DigToolSpec` (from today's shovel, until retuned in playtests):** radius 0.45 m (scroll-adjustable), removal 2.0, maxHardness 2, damage 0.6, crude true, rate 5.6 /s, no cell cap. Stroke still goes to `ExcavationSite.Stroke` → grid rules (including the half-cell crude-find bite and the "jar" warning when cutting just above a hidden find) → one Digger carve.

**Digger op:** `ExcavationStroke.Remove`, brush type from the selected head (default Sphere). Leftover lump trimming stays as it is.

**Dials (while equipped):**

| Control | Effect |
|---|---|
| **Power** | Scales removal and rate up, and damage / energy / heat with them. High Power near a find is how you crack it. |
| **Focus** | Tightens the head and lowers damage and removal. At high Focus the tool is slower and safer, but still crude until Artifact-Safe mode is installed. |

**Risk.** Same as today's shovel: visible-find cuts cost condition (`damage × fragility × take`); a stroke immediately above a hidden find jars it (15% of that). Toasts keep the same advice, with updated tool names.

### 3.2 Precision Emitter

**Role.** Small, shallow, controlled bites once the soil "feels different" or a find has emerged.

**Base spec (today's precision tool):** radius 0.1 m, removal 0.15, maxHardness 2, damage 0, crude false, rate 5 /s, cells 1, minBite 0.02. Safe around finds.

**Visual.** A thinner beam to a small sphere / disc, or a short 3–5 line pulse. Not the full brush fan.

**Digger op:** Remove, Sphere (or HalfSphere for a shallow skim). Radius is raised to `CarveRadius` when mirroring, same as now, so the hole and the grid stay in step — the *visual* stays tight so it still reads as precise.

### 3.3 Ultrasonic Brush (sonic precision)

**Role.** Last dust and loose soil on and around a find. Wide, thin, soft, never damaging.

**Base spec (today's brush):** radius 0.6 m, removal 0.035, maxHardness 1, damage 0, crude false, rate 7 /s, cells 1, minBite 0.005. Refuses compacted / rock / ice the same way the brush does now (`CanCut` / hardness).

**Visual.** Fine tip, N line-rendered rays that spread and strike scattered ground points, with a travelling wave pulse (see §8).

**Clean %.** A new per-find field value, 0–100, stored on `GridArtifact` (and therefore in `BuriedArtifactState` / `excavation.sites`). It measures how much of the find's *exposed surface cells* still have dust/loose fill. The brush raises it; the excavator does not. At 100% the find is field-clean and ready to lift (or to take to the lab). This is *not* `ResearchOp.Cleaning` — lab cleaning still reveals surface detail and is required for identification as it is today. The HUD shows "CLEAN 72%" when a find is in the brush's aim.

**Digger op:** visually many small hits; gameplay is still one grid stroke (wide, shallow) and **at most one or two** Digger carves (HalfSphere / low-strength Sphere, or Smooth on already-open pit walls). See §8.

### 3.4 Ground-Penetrating Scanner

**Proposal: not a seventh hotbar tool.** The Depth Probe already pulses or holds LMB at the pit floor and reports how far down the nearest 1–3 objects are, and which way they are offset, without naming them (Mk I–III via `scanner.depth_probe`). GPS is the **hologram layer** on that probe:

- Mk I stays bands + a surface ring (today).
- Mk II adds a short-lived, semi-transparent density hologram in a volume under the aim (blobs, not named models).
- Mk III / Deep-Pulse module: larger volume, slightly sharper silhouettes, still abstract.

The handheld surface scanner (`item.tool.handheld_scanner`, key 1) remains the tool that finds *sites*. GPS does not steal that job.

If you would rather have a distinct "GPR brick" on the hotbar, the same hologram can live on a new item instead — that is open question 1.

### 3.5 Resonance Probe

**Role.** A field "tap" on an exposed find (or a suspicious soil patch). A pulse reads a **material family** and a **coarse age band**, with error that shrinks with upgrades. Rare alien materials play a unique sustain (the "sing").

**What it is not.** It does not complete `ResearchOp.MaterialAnalysis` or `AgeEstimation`. Those stay ship-lab jobs and still publish their Discovery facts. The probe writes a *preliminary* note on the artifact instance and a journal line ("Field resonance: ceramic, late-era band") so the lab result can confirm or contradict it.

**Use.** Equip, aim at an exposed find or the ground, LMB pulse (cooldown). Hold LMB for a longer, hungrier "listen" with a better reading.

### 3.6 Stasis Lifter

**Role.** Today's recover rules stay: a find can be taken from 50% exposure; below 90% it is pried loose and loses condition. With the Stasis Lifter equipped and paid for in energy, the player can:

- Lift from 50% exposure **without** the pry penalty, or
- Lift a little earlier (upgrade: down to ~35%) at a higher energy cost.

If the tool is holstered or the battery is empty, hold-E behaves exactly as it does now. The lifter is a projector: a short cylindrical or shell field around the find, not a beam chisel.

---

## 4. Inputs (keyboard and mouse)

Existing bindings we must not steal: **E** interact / recover, **F** helmet lamp, **I** inventory, **Tab** tool wheel, **1** handheld scanner, **2–8** hotbar, **LMB** use, **RT** use.

| Input | Sonic Excavator / Precision | Ultrasonic Brush | Probe / Lifter |
|---|---|---|---|
| **LMB hold** (or RT) | Fire / dig, paced by `rate` and energy | Fan pulse, paced by `rate` | Pulse (tap) or live reading (hold), as the Depth Probe does today |
| **LMB release** | Beam retracts, heat starts falling | Wave fades | Field drops |
| **Scroll wheel** | Chisel **size** (radius), clamped to `[MinRadius, maxRadius]` for this tool / upgrade | Fan **spread** (outer radius) | Hologram / field **radius** |
| **Shift + Scroll** | Beam **reach** (emitter to head), clamped (base 1.2–3.0 m; upgrades extend toward today's 3 m `DigTool.Reach` and past it) | Tip-to-ground reach | — |
| **Q / C** (or mouse-wheel click) | Cycle **head shape** (only shapes the player has unlocked) | — | — |
| **R** | Cycle **Power** preset (Low / Med / High) | — | — |
| **T** | Cycle **Focus** preset (Wide / Mid / Tight), or a small on-tool dial in the HUD | — | — |
| **V** (module) | Toggle Artifact-Safe or Auto-Layer-Stop when that module is installed | — | Deep Pulse (GPS) |
| **Hotbar 2–8 / Tab wheel** | Equip / holster, same as today | same | same |

If Q is already taken on the rover or a wheel, mouse-wheel click + an on-HUD shape strip is the fallback. Final binds can move after you try them; the HUD always shows the live ones.

Gamepad (later pass): right trigger = fire, right stick click or D-pad = shape, bumpers = size, same Tab wheel.

---

## 5. Stats, energy, heat, and Digger mapping

### 5.1 Stat block (per tool, per Mk, plus modules)

Extended on top of `DigToolSpec` (data on the existing `DigToolDefinition` assets, or a small companion SO so we do not break current assets):

- `radius`, `removal`, `maxHardness`, `damage`, `crude`, `rate`, `cells`, `minBite` — already exist.
- `reachMin` / `reachMax` — beam length.
- `sizeMin` / `sizeMax` — scroll size.
- `energyPerSecond` / `energyPerPulse` — drain while firing.
- `heatPerSecond`, `heatMax`, `coolPerSecond`, `overheatSeconds` — see §5.2.
- `unlockedShapes` — flags, also granted by modules.
- Feature flags: `autoLayerStop`, `artifactSafe`, `multiBeam`, `deepPulse`, etc.

Mk I / II / III upgrades (the existing `CraftRecipe` upgrade path: `upgradeId` + `upgradeLevel`) raise the numbers. Modules add shapes and features, and can also add a modest stat bonus.

### 5.2 Energy and heat (new; nothing like this exists on tools today)

There is no suit battery today (`ConsolePower` is for ship consoles; fuel cells feed the ship). This plan adds a small **Suit Power** pool.

- **Shared suit energy**, shown whenever a powered tool is out. Base capacity, e.g. 100, raised by a Heat-Sink / Cell-Bay module and by a later suit upgrade (`suit.power`, same `GameSession.ApplyUpgrade` pattern as `ship.hold`).
- Drain only while the tool is firing or a pulse is in flight (GPS hologram, stasis field, resonance listen). Idle tools cost nothing.
- Recharge: aboard the ship, in the shuttle, and in the rover (docked or seated), automatically. In the field, a **Power Cell** (refined on the existing Refiner from the same family as today's Fuel Cell, or a Fuel Cell itself if you prefer not to add an item — open question 4 is about the source, not the bar) can be used from the inventory / wheel for a partial refill.
- Empty battery: the tool holsters its beam, plays a power-down, and refuses strokes. The mechanical *rules* never run dry of a "durability" stat — you just cannot fire.

**Heat** is **per tool**, not shared. It rises while firing, falls when idle (`coolPerSecond`, faster with a Heat Sink module and in thin atmospheres / vacuum, slower in dense air). At 100% the tool **overheats**: a forced cooldown (no fire, warning toast and a warn-tint on the bar), then it resumes. No durability loss, no broken tool. Overheating mid-stroke simply stops the next stroke.

Energy and heat scale with Power, size, and (for the excavator) how hard the band is.

### 5.3 Digger mapping

| Tool / head | `ExcavationStroke` | Digger `BrushType` (already in the vendor enum; adapter today hardcodes Sphere) | Notes |
|---|---|---|---|
| Excavator — Sphere | Remove | `Sphere` | Default. Matches today's carve. |
| Excavator — Flattened disc | Remove | `HalfSphere` (shallow) or `Sphere` at a slightly lower centre | Honest limitation: Digger's size is **one float**. A disc *preview* will not match a perfectly flat voxel cut. We use HalfSphere / a shallower centre and keep the preview slightly rounded so the player is not lied to. |
| Excavator — Cube / chisel | Remove | `RoundedCube` | Good for trench walls and square pits. |
| Excavator — Cone | Remove | `Stalagmite` | Point-down for a narrow deep bite; inverted if Digger's upside-down stalagmite is usable from the runtime API. |
| Precision Emitter | Remove | `Sphere` or `HalfSphere` | Small radius, already raised to `CarveRadius` for the grid/visual match. |
| Ultrasonic Brush | Remove (thin) or Smooth on open walls | `HalfSphere` / low opacity `Sphere` | One carve for the cluster, not one per line. |
| Leftover lumps | Remove | as today | Unchanged. |
| Fill / debug | Fill | `Sphere` | Unchanged, not in normal play. |

`IExcavationSurface.Apply` should gain an optional brush (default Sphere) so gameplay still never calls Digger directly. `DiggerExcavationSurface` maps it. Vendor files stay untouched. `shadowlessDigs` stays on.

Layer texture index still comes from `SoilLayer.diggerTexture`, so Mars / Moon layers keep their look.

---

## 6. Excavator beam and chisel heads

Three visual parts, plus a gameplay trigger:

1. **Emitter** — a muzzle on the held mesh (a socket on the futuristic body, see §14). Idle: a dim cyan point. Firing: brighter, with a short charge-up (Power-up clip).
2. **Beam** — a `LineRenderer` (or a thin tapered mesh) from the emitter to the head centre, colour `#68DFD6` with a warmer `#FFA967` core at high Power. Length is the reach (Shift+Scroll). The beam only exists while the tool is out and aimed; it retracts on holster or empty battery.
3. **Head** — a semi-transparent URP mesh (Sphere / flattened Sphere / Cube / Cone) at the aim point, scaled to the current size. Unlit / additive or a simple transparent URP Lit at ~15–25% opacity, cyan, with a thin harder rim. This is the "variable round chisel."
4. **Trigger** — a matching trigger collider (`SphereCollider` or a mesh collider for cube / cone) on the head. It does **not** deform the world by itself. It is used for:
   - artifact proximity (highlight, "find in envelope" warning, Artifact-Safe / Auto-Layer-Stop),
   - a cheap overlap test so the HUD can say the head is in dust vs. hanging in air,
   - VFX spawn bounds.

**Aim.** Same ray as today (`DigTool.Aim`, 6 m ray, 3 m + reach upgrades to fire, skip the player and buried-find colliders). The head sits at the hit point, offset along the normal so the volume reads as cutting *into* the soil (same idea as today's carve centre sitting slightly above the new floor). If the aim is off a site or out of reach, the head tints to the invalid colour (warn coral) the way `DigAimRing.State.Invalid` does now.

**Scroll size and distance** are stored per tool on the session (so they survive holster) and clamped per Mk.

**Shape change** is a short morph (0.15–0.25 s) of the preview mesh, with a holographic dissolve, not an instant pop.

---

## 7. Precision wave (Ultrasonic Brush, light version on the Precision Emitter)

- **N `LineRenderer`s** (base N = 7, Mk II = 9, Multi-Beam module = 12–16) leave a single fine tip on the wand and spread in a narrow cone toward the ground.
- Each frame (or each pulse, at `rate`) the lines aim at **jittered points** inside the brush radius on the aimed surface: a disc of raycasts along the view, with a small spiral or ring offset so they do not stack. If a ray misses (pit wall, sky), that line draws short and does no work.
- A **wave pulse** (a brighter segment or a secondary wide `LineRenderer` ring) runs down the fan once per stroke so the "sonic" read is obvious.
- **Gameplay:** the hit points are *visual*. The accepted stroke is still **one** `ExcavationSite.Stroke` at the aim centre with the brush `DigToolSpec` (wide, thin, safe). Digger gets **one** shallow carve at the cluster centroid (two only if the hits clearly split, e.g. a thin ridge). This avoids flooding `ModifyAsyncBuffured` the way the old per-cell carves did.
- Hit sparks / micro-dust spawn at the raycast points so it *feels* like many small bites.

Precision Emitter uses the same code with a smaller N and a tighter cone.

---

## 8. FX: dust and rock chunks

The project already builds **ParticleSystem** effects in URP (weather, FTL streaks). There is **no VFX Graph** usage in `_Project`. Dust and chips should use **ParticleSystem**, in the same pooled style as `WeatherParticles`, so we are not introducing a second FX stack.

**Dust.** A small world-space burst at the head (excavator) or at each line hit (brush), 8–20 soft particles, short life (0.4–0.8 s), size 2–6 cm, soft alpha material (the weather visual already has an alpha particle material we can reuse or copy). Spawn only while a stroke is accepted. **Tint from `SoilLayer.color`** (and `SoilPalette.For(bodyId, material)`), so Moon dust is grey and Mars dust is rust.

**Rock chunks.** A small **pool** (16–24) of low-poly pebbles (a few placeholder meshes, later maybe a ruin-pack leftover if the licence allows — otherwise simple generated meshes). On an accepted excavator or drill stroke (not the brush): spawn 2–6 chunks in the head volume, give each a **Rigidbody**, a short random impulse (out of the pit and slightly up), and a lifetime of 2–4 s with a fade. **Gravity** follows the body's environment (`EnvironmentDefinition.gravity`, already applied to the player). Scale impulse and "hang time" with `g / 9.81`, the same way rover springs already do — on the Moon chunks loft further and settle slower. Tint / material from the cut layer. Chunks **ignore the player** (no shoving) and despawn on sleep or timeout. They are cosmetic: they do not yield extra materials (yield still comes from `StrataFinds`).

**Budget.** One dust system per local player (emit, don't instantiate). Chunks are pooled. No trails on every pebble. No collision against Digger meshes at High quality — Low collision, or overlap only with terrain / default, to avoid spikes in a fresh pit.

---

## 9. Audio hooks

Clips live in the gitignored pack `Assets/Pro Sound Collection v4.26 Audio/ProSoundCollection/<Category>/Wavs/`. Wire them through a small `DigToolAudio` catalog (same idea as `UISoundCatalog` / `ProSoundFootstepCatalog`), with Kenney / procedural fallbacks so the project still runs if the pack is absent. Do not commit the WAVs.

Candidate folders and example clip names (all under `ProSoundCollection/`, stereo set; mono twins exist in `ProSoundCollectionMONO/`):

| Event | Folder | Candidate clips |
|---|---|---|
| Tool equip / power up | `SciFi/Wavs` | `sci-fi_power_up_01` …, `sci-fi_device_item_power_up_flash_01`, `sci-fi_power_on_rise_beep_01` |
| Idle emitter hum | `ElectricityHums/Wavs`, `SciFi/Wavs` | `hum_electric_neon_light_loop_01`, `sci-fi_deep_electric_hum_loop_01`, `hum_motor_elec_neon_loop_01` |
| Excavator firing loop | `SciFi/Wavs`, `SciFiWeapons/Wavs` | `sci-fi_electric_pulse_hum_01`–`09`, `sci-fi_weapon_blaster_laser_deep_drone_01`–`04`, `sci-fi_sub_bass_rumble_loop` (very low, under the pulse) |
| Brush / precision wave | `SciFi/Wavs`, `SwitchesButtonsGearsLevers/Wavs` | `sci-fi_hologram_electric_buzz_01`, `electric_toothbrush_small_motor_whir_01`–`02` (light, high) |
| Head-shape change | `SciFi/Wavs` | `sci-fi_forcefield_holo_power_down_01`, short `sci-fi_electric_pulse_power_down_01` |
| Soil / rock contact | `Foley` / impacts in the pack | `bullet_impact_dirt_01`–`08`, `bullet_impact_gravel_*`, `bullet_impact_rock_*`, `dirt_rice_pouring_gravel_debris_01`–`06` (layer-picked: dust / loose / rock) |
| Find jar / damage | `SciFi/Wavs`, `AlarmsBeepsSiren/Wavs` | a short warning beep (`sci-fi_alarm_warning_loop_*` as a one-shot, or `alarm_beep_warning_01`) — keep it short |
| GPS / resonance pulse | `SciFi/Wavs` | `sci-fi_radar_ui_scan_01`–`02`, `sci-fi_scan_target_01`–`05` |
| Stasis field | `SciFi/Wavs` | `sci-fi_forcefield_hum_loop_01`–`10`, `sci-fi_shield_device_small_01`–`03` |
| Overheat / empty | `SciFi/Wavs` | `sci-fi_power_down_01`–`03`, `sci-fi_electric_pulse_power_down_01`–`03`, `sci-fi_alarm_warning_loop_01` (one-shot) |
| Rare material "sing" | `SciFi/Wavs`, `CollectiblesItemsPowerup/Wavs` | a sustained `sci-fi_forcefield_hum_loop_*` plus a soft `chime_bell_positive_ring_01`–`03` |

Pitch still wanders slightly per stroke, as `DigTool` does now with the procedural scrape. Loops fade out on release; they do not cut.

---

## 10. Upgrade tree

Two layers, both using systems we already have:

1. **Mk I / II / III stat tracks** — same pattern as `scanner.depth_probe`, `scanner.handheld`, `ship.hold`: `CraftRecipe` with `upgradeId` + `upgradeLevel`, `CraftCategory.Upgrades`, usually `CraftStation.Ship`, optional `unlockFact`. `GameSession.UpgradeLevel` / `ApplyUpgrade` / `UpgradeMax` gain new ids.
2. **Feature modules** — inventory items in slots on a tool (see §11). Some Mk III effects can instead be modules, so the tree stays short and the "cool features" stay optional.

Proposed upgrade ids:

| Id | Tool | What Mk II / Mk III raise |
|---|---|---|
| `tool.sonic_excavator` | Excavator | Precision (tighter min size, lower base damage), depth (`cells` / removal), reach, energy efficiency, heat capacity |
| `tool.precision_emitter` | Precision Emitter | Tighter cone, slightly higher `maxHardness`, faster rate, efficiency |
| `tool.ultrasonic_brush` | Brush | Fan count, clean % rate, slightly wider skim, efficiency |
| `tool.resonance_burster` | Drill / burster | Hardness headroom, lower damage for the same bite, heat sink |
| `scanner.depth_probe` | already exists | Mk II / III already track more objects, deeper, more accurately — **add the hologram at Mk II** and Deep Pulse as a module or Mk III |
| `tool.resonance_probe` | Resonance Probe | Less age/material error, shorter cooldown, "sing" range |
| `tool.stasis_lifter` | Stasis Lifter | Lower energy per lift, earlier safe threshold, larger field |
| `suit.power` | Suit | Energy capacity and recharge rate |

**Per-tool tree (excavator as the full example; others are slimmer):**

```
Mk I  (starting)
  ├─ Precision I → II → III     smaller min head, lower damage multiplier
  ├─ Depth I → II → III         more removal / cells per stroke (still no digging past bedrock)
  ├─ Reach I → II               beam length and fire range
  ├─ Efficiency I → II          energy per second
  └─ Heat Sink I → II           heat cap and cool rate
        └─ (modules, any time after their blueprint is known)
              Head: Disc, Cube/Chisel, Cone
              Auto-Layer-Stop
              Artifact-Safe mode
              Multi-Beam (brush / precision; on excavator = a double-head or a side lobe)
```

Tracks are independent (you can raise Heat Sink without Depth), but each track is ordered (Efficiency II needs Efficiency I), which matches `CraftRecipe` today ("Install the previous upgrade first").

**Cool features (modules, unless noted):**

| Feature | Tool | What it does |
|---|---|---|
| **Auto-Layer-Stop** | Excavator, Precision | Stops the stroke at a layer boundary (dust→loose, loose→compacted, or any band harder than a threshold) so you do not punch through into a new material by accident. A soft chime and a HUD tick when it trips. |
| **Artifact-Safe mode** | Excavator | While on, the excavator is treated as **not crude** (no jar, no condition loss) and bites like a slower precision tool when the head trigger overlaps a find. Costs more energy. Toggle (V). |
| **Multi-Beam** | Brush, Precision, later Excavator | More fan lines, or a second small head offset from the first (trench cutting). Still one or two Digger ops, never N. |
| **Deep-Pulse scan** | Depth Probe / GPS | A longer, hungrier pulse: larger hologram volume, briefly "paints" density below the pit floor. Cooldown + energy. |
| **Layer paint** (nice-to-have) | Excavator | Head outline tints to the *next* band's colour when Auto-Layer-Stop is armed. |
| **Resonance tag** | Resonance Probe | Marks a find in the Discovery Journal with the field reading. |
| **Stasis hold** | Stasis Lifter | Holds the field for a few seconds after release so you can re-grip (useful in low g). |

---

## 11. Add-on modules and alien variants

**Slots.** Each tool has 1–3 module slots (Mk I: 1, Mk II: 2, Mk III: 3). Slots are on the tool instance, saved with the inventory item (unique-instance tools already exist as a flag on `ItemDefinition`; we should turn it on for these so two excavators can be built differently).

**Module items.** `item.module.<name>` (category Equipment or a new Modules category), mass and value like other parts, crafted at the **ship workshop** (`CraftStation.Ship`, next to today's upgrade recipes) or, later, a workbench terminal. Some recipes can also run at the **Refiner** only when they need a refine-style wait (melted alloy housings); default is the crafting panel, not the ore refiner, so the refiner stays about materials.

**Install / remove.** From the inventory or a small "Tool Bay" tab on the existing ship crafting / equipment UI (mock up that tab before building it). Unequipping returns the module to the backpack. In the field you can *use* an already-installed toggle (Artifact-Safe, Deep Pulse); you cannot swap modules mid-dig unless we later add a field kit bench.

**Sources.**

- **Crafted** from a known blueprint + ingredients (alloy, circuits, wiring — the parts already in `Data/Crafting`).
- **Found** as loot: ruin caches, site chests, journal-style world pickups, rare drops in alien locations (the RUINS packs and later altar / monument sites). Found modules can be used without a blueprint; researching one can *unlock the blueprint* so you can craft more.
- **Rewarded** from Discovery chain nodes (`DiscoveryReward` already supports `setFact` and `journal` text).

**Rare alien tool variants.** Unique items, e.g. `item.tool.alien_excavator_<civ>`, found at late sites, not crafted. They look like the human tool family but "off" (warmer emissive, irregular head morph, a glyph strip). They come with an extra slot or a unique built-in module (for example a head shape we do not sell, or a safer default). They are not required for the main path. Researching one (`ResearchOp.TechnologyAnalysis`, already used for installable tech via `ArtifactDefinition.installTarget`) can unlock a human-craftable module that is a weaker echo of the alien trick, and writes a Captain's Log / journal entry.

---

## 12. Blueprint system

We already have the hook: `CraftRecipe.unlockFact` + `CraftingService.Known` + `Discovery` facts (`Facts.Research`, `Facts.Installed`, `Facts.Civilization`, chain rewards, journal text on `DiscoveryReward`). Blueprints should reuse that, not invent a second unlock graph.

**`BlueprintDefinition`** (new SO): id, display name, flavour text, the `factKey` it publishes (e.g. `blueprint.tool.excavator.disc_head`), the recipe or module it unlocks, and a source tag (Earned, Found, Researched) for the journal filter.

**Three sources:**

| Source | How | Hook |
|---|---|---|
| **Earned** | Finish an objective, a Discovery chain node, a notoriety rank, or a "firsts" milestone (first time a find emerges, first field-clean, first site floor, first jarred find recovered in good condition). | `DiscoveryReward.setFact` + `journal`. Notoriety ranks already gate vendor stock; they can also grant a fact. |
| **Found** | World pickup in a ruin, a site chest, a buried cache, or a vendor one-off. The pickup is an inventory item `item.blueprint.<id>`. Using it (or depositing it at the ship archive) publishes the fact and archives the item so it cannot be sold by accident. | Same pickup path as `JournalPickup`. Item stays in the inventory until used, so it can be carried home. |
| **Researched** | Lab job on an **alien fragment**, a recovered artifact, or a found module. | Add a research op, proposal: `ResearchOp.ReverseEngineering` (reagents + time, like `TechnologyAnalysis`). Completing it publishes `Facts.Research(id, ReverseEngineering)` and the blueprint fact. Existing ops can also grant a blueprint as a side reward on selected artifacts (the way `installTarget` already gates installed tech). |

**Discovery Journal.** A "Blueprints" filter or section lists known / in-progress / rumoured (if a chain has hinted). New entries use the same maroon/crimson terminal style (`Xenoarch.uss`). Unlock toasts stay short: "Blueprint added: Disc Chisel Head."

**Captain's Log.** The Morpheus-style Captain's Log is still a planned UI. Until that screen ships, blueprint story text goes into the Discovery Journal (and `DiscoveryReward.journal`). When the log exists, milestone and alien-variant unlocks should also write a log card, same as other discoveries.

**Vendors.** Equipment suppliers can sell *some* human Mk I modules once the fact is known, at a mark-up, following `VendorDefinition` rank gates. They should not sell unique alien variants.

---

## 13. Futuristic visual language

Year 2187, semi-realistic archaeology, a little fantasy. Tools should read as **field instruments**, not guns.

- **Body:** dark gunmetal / polymer, compact, one-handed for the brush and probes, slightly longer for the excavator. No wooden shafts, no shovel blade, no bristle head.
- **Light:** emissive seams and a small holographic strip on the housing, in the UI palette only — **cyan `#68DFD6`** for ready / beam / info, **orange `#FFA967`** for headings, size ticks, and high Power, **warn coral `#FF968C`** for heat and damage. Idle emissive is dim; firing raises it.
- **Heads:** the chisel is a **shape-shifting hard-light / sonic envelope**, not a swapping physical bit (the physical muzzle stays). Morph between sphere, disc, cube, and cone with a short holographic dissolve.
- **Readouts:** a tiny holographic band on the tool itself (energy ticks, shape glyph) plus the screen HUD (§14). The on-tool band is flavour; the HUD is the readable source.
- **Alien variants:** same silhouette language so they still parse as "your excavator," but irregular glyphs, a warmer or broken emissive, and a slightly different morph (e.g. a faceted "crystal" head).
- **Held meshes.** Today's `HeldToolCatalog` still points at shovel / stubby brush / dual precision / rock drill prefabs. Phase 1 can keep those sockets and hang a placeholder primitive-and-emissive mesh off `HeldToolMeshFactory` so playtests work. Final meshes and icons need a later art pass and a separate visual approval. Do not reuse vendor ruin meshes for the human tools.

Placeholder first-person kit remains `Assets/FPS_Starter_Pack` (arms and animator for devices, not weapons).

---

## 14. HUD (existing Xenoarch UI only)

No new visual language. Colours and typography follow `XenoarchPalette` / `Xenoarch.uss`: panel `#761C27` / `#4E121A` at ~85% (today's dig hint already uses `PanelDark` at 0.85), headings and bars `#FFA967`, info and selection `#68DFD6`, body text `#E8E2D0`, secondary `#5CA7A8` / dim bone, warnings `#FF968C` on `#B03B3B` fills, frame `#AA404E`. Jost is the planned UI face; the current hint is UITK default / theme. Keep the **bottom-centre dig hint** (it already wraps at 72% width and sits above the hotbar) and extend it, rather than adding a second panel.

While a sonic tool is out, the hint block shows:

1. Line 1: tool name, Mk, shape glyph + size (e.g. `SPHERE  0.45 m`), reach, and the fire prompt.
2. Line 2: today's layer / hardness / depth / site line (`Digging: loose regolith (hardness 1.0, tool OK)  depth 0.42 m  |  Ma11`).
3. A compact **ENERGY** bar (cyan / orange fill on a `secondary-soft` track, same proportions as `.xa-hold-fill`) and a **HEAT** bar (orange, flipping to warn coral above ~80%).
4. When a find is in the head or under the brush: **COND** (artifact condition) and, for the brush, **CLEAN %**.
5. Installed-module ticks (Safe, Layer-Stop, Multi-Beam) as short labels, not a new icon system.

Invalid aim and bedrock refusals stay as they are (clunk + flash line). Overheat and empty battery use the existing toast / flash path in warn colour.

A full terminal "Tool Bay" (slots, upgrade tracks, blueprints) is a later UI and needs its own mockup before layout work.

---

## 15. Performance budget

- **Digger:** keep one primary carve per accepted stroke. Precision fan is visual. Pending queue stays capped (`MaxPendingCarves = 24`). If the player out-digs the buffer, drop oldest pending carves as today. Never call `ModifyAsyncBuffured` per fan line.
- **Op rate:** still gated by `DigToolSpec.rate` and `IsBusy`. Energy / heat are an extra gate, not a replacement.
- **FX:** one looping dust `ParticleSystem` (emit/stop). Chunk pool 16–24, no per-stroke instantiate. Low-quality particle collision.
- **Lines:** cap N at 16 renderers, reused, disabled when holstered.
- **Hologram (GPS):** a short-lived transparent mesh or a small set of phantom primitives, not a second voxel mesh. Despawn in ~3–5 s.
- **Triggers:** one collider on the head. No per-frame overlap spam; check on stroke and on a short timer.

---

## 16. Migration and save compatibility

**Recommendation: keep the existing dig item IDs** (`item.tool.excavation_shovel`, `item.tool.precision_tool`, `item.tool.brush`, `item.tool.powered_drill`). Change `displayName`, description, icons (after approval), held meshes, and the `DigToolDefinition` names. Old saves, hotbars, crates, and recipes keep working. The player who had a shovel now holds a Sonic Excavator.

- `DigToolSpec` fallbacks in `DigTools` stay as numeric defaults; labels in toasts and the HUD use the new names.
- Grid and `excavation.sites` stay compatible. Add **optional** fields: `clean01` on `BuriedArtifactState` (missing = treat as 0, or as 100 if already recovered). New energy / heat / suit power in a new save section, e.g. `player.suit` `{ energy, cells }`. Module loadouts on unique tool instances in inventory state. Missing section = full energy, no modules, Mk I.
- Blueprints are facts; unknown facts are simply locked recipes, which is already the case.
- **Fallback during the transition:** a development toggle (`DigTool.legacyVisuals` or a config flag) can show the old meshes and the aim ring if a playtest build needs to ship before the placeholders are ready. Do not keep two gameplay rule paths. Once you sign off, the shovel / brush meshes retire from play (they can stay in the project unused).
- Starting kit and "New Test All" grant the same item IDs as today, plus later the new probe / lifter when those ship.
- Journal copy that says "switch to the precision tool (3) or brush (4)" needs a wording pass (`Journal_dig_shovel`, emerge toasts in `DigTool`).

---

## 17. Implementation phases (rough sizes)

S ≈ a day or less, M ≈ 2–4 days, L ≈ a week of engineering (not including final art). All visual / HUD work in a phase still needs mockup approval before it lands in the game. No phase is "tested" until it has been play-tested. Vendor Digger files stay untouched. Compile, commit, and push follow the usual project rules.

| Phase | Work | Size |
|---|---|---|
| **0** | This plan and mockups, your approval, answers to the open questions | — |
| **1** | Beam + head preview (sphere first), scroll size / reach, HUD bars and shape/size line, placeholder held mesh. **No rule changes.** Strokes still use today's tools under the new look. | M |
| **2** | Dust ParticleSystem + pooled chunks (layer tint, planet gravity), audio catalog wired to Pro Sound with fallbacks | M |
| **3** | Power / Focus, energy + heat service (`player.suit`), overheat, recharge at ship / shuttle / rover | M |
| **4** | Adapter brush types (cube, cone, disc-as-HalfSphere), shape cycle, leftover lumps still work | S–M |
| **5** | Precision / brush fan (LineRenderers + wave), one/two-carve budget, field **clean %** + save field | M |
| **6** | Stasis Lifter + Resonance Probe (new items, recipes, prompts). GPS hologram on the Depth Probe if you take that option. | M |
| **7** | Mk tracks in `GameSession` + `CraftRecipe`s (precision, depth, reach, efficiency, heat) | M |
| **8** | Module slots, craft / find / install, Auto-Layer-Stop, Artifact-Safe, Multi-Beam, Deep Pulse | M |
| **9** | Blueprint SOs, earned / found / researched sources, Journal section, fact hooks, optional Reverse Engineering op | M |
| **10** | Final held meshes, icons, alien variant look — art, after a separate visual pass | L (art) |
| **11** | Copy pass, balance (energy, heat, damage), playtest, remove legacy visuals | M |

Suggested build order after approval: **1 → 2 → 3 → 5** (the feel you asked for) then **4, 6, 7–9**. Art can trail.

---

## 18. Open questions

1. **Ground-Penetrating Scanner:** keep it as a hologram upgrade on the existing Depth Probe (key 6), or add a separate hotbar tool?
2. **Powered drill:** retire it into a Resonance Burster **module / head** on the excavator, or keep it as its own key-5 sonic drill for ice and rock?
3. **Precision tool:** keep three dig tools (Excavator / Precision Emitter / Ultrasonic Brush) so keys 2–4 stay familiar, or fold mid-work into the excavator's Focus dial and drop the third item?
4. **Suit energy source:** a new Power Cell refined on the ship, or should existing Fuel Cells also charge the suit in the field? And should found blueprints be inventory objects you can lose until archived, or instant Discovery facts on pickup?

---

## 19. Concept mockups

Labelled **CONCEPT MOCKUP** — not in-game screenshots. Palette matches `XenoarchPalette` / `Xenoarch.uss`. Saved on the box (not committed):

| File | What it shows |
|---|---|
| `/workspace/sonic_tools_mockups/01_excavator_beam_sphere.png` | First-person excavator: emitter, cyan/orange beam, semi-transparent sphere head cutting a pit, dust and rock chunks |
| `/workspace/sonic_tools_mockups/02_precision_wave_fan.png` | Ultrasonic Brush: fine tip, fan of line-rendered waves hitting scattered ground points, a find with a clean % readout |
| `/workspace/sonic_tools_mockups/03_hud_tool_readouts.png` | Bottom-centre HUD in the existing style: energy, heat, head shape and size, layer / depth, condition |
| `/workspace/sonic_tools_mockups/04_futuristic_tools_and_modules.png` | Tool family look (emissive seams, holographic strips, shape-shifting heads) and add-on module slots |

No gameplay code is changed with this document.
