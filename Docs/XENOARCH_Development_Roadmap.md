# XENOARCH — Development Roadmap

_Status legend: Planned · Implemented · Tested · Incomplete._

| Phase | Scope | Status |
|---|---|---|
| **1A Foundation** | Reuse audit, architecture, docs, folder structure, asmdefs, bootstrap + scene management with fades, controller adapter (vendor FPC pending), interaction, stable-id item definitions, inventory, versioned save, 6 placeholder scenes, smoke check + EditMode tests, git | Implemented — **awaiting Editor compile/verification and Anthony's review** |
| 1B Exploration | Celestial body + star system definitions (Sol), environment profiles + gravity, navigation terminal UI, travel via ship state, ship interior stations wired, orbital scan + handheld surface scan, archaeological site definitions; vendor FPC integration | Planned (do not start until 1A review) |
| 1C Excavation & research | Layered-cell diggable volume (see Excavation Design), artifact definitions (~30) and instances, condition/damage, research + translation, civilization, discovery chain evaluator, Moon→Mars chain | Planned |
| 1D Economy & progression | Credits, vendors, trade hub, equipment + ship upgrade purchases, fuel, notoriety + unlocks, recoverable alien tech (sensor module), discovery journal UI | Planned |
| 1E Integration | Full loop playthrough (21 acceptance criteria), persistence validation, scene transition tests, compile clean, limitations doc | Planned |

## Recommended order inside 1B
1. Vendor FPC adapter (unblocks real walkthrough testing).
2. Body/system definitions + `galaxy.discovery` state + nav terminal (UI Toolkit).
3. `ship.state` (fuel) + travel rules.
4. Environment profile + gravity.
5. Orbital scan (data-driven anomalies, progressive reveal) → site definitions.
6. Handheld surface scan (detect buried items in a site volume; feeds 1C).
