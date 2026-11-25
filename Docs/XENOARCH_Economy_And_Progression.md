# XENOARCH — Economy and Progression

_Status: **Planned** (Phase 1D) unless noted._

## Credits
Single currency. Section `player.wallet` {credits}. Starting credits configurable in `XenoarchConfig` (to add).

## Vendors (`VendorDefinition`)
id, type (Museum, University, Private collector, Corporation, Equipment supplier, Shipyard, Information broker, Auction house, Independent trader), buy interests (category/civilization/rarity multipliers), stock list (item id, price, quantity or infinite), services (refuel price per unit, research/translation services). Vendor transactions recorded in `economy.transactions` (for journal and to prevent re-buying unique stock). Phase 1: one Artifact Buyer, one Equipment Supplier, refuelling, ship upgrades at TradeHub_Sol. Pattern from ASH `ASHVendor` + `ASHEconomySettings`.

## Equipment and tiers
Tools are `ItemDefinition`s (Implemented in 1A for the 5 starter tools) plus an `EquipmentProfile` (Phase 1B/1C) with scanner/excavation stats: detection range, max depth, resolution, material ID, accuracy, scan time, power, false-positive rate, supported anomaly types; excavation rate, precision, damage factor.

## Ship
`ShipDefinition` (class, hull, reactor capacity, fuel capacity, FTL range, scanner capability, cargo, lab capability, shielding, upgrade slots). `ShipModuleDefinition` (slot type, stat modifiers, requirements, compatibility). State `ship.state` (hull, fuel, installed module ids/instances). Recovered alien tech installs as a module or equipment modifier when compatibility + research prerequisites are met.

## Notoriety
Global score (section `progression.notoriety`), awarded for discoveries, sites, research, translations, civilization identification, rare tech, chain completion, contracts, significant finds. Ranks (configurable thresholds in a `NotorietyConfig` SO): Unknown Explorer → Independent Excavator → Recognized Archaeologist → Renowned Explorer → Galactic Authority → Legendary Discoverer. Unlock table: rank → vendor stock, locations, contracts, information. Architecture leaves room for separate scientific/commercial/faction tracks (same component, different track id).

## Materials and refining (Phase 1C, built 2026-11-25)
The loop: gather deposits on planet surfaces, then either use them directly or refine them on the ship into fuel cells and lab reagents. Fuel lets the ship travel; reagents let the lab research artifacts; artifacts unlock new sites, which leads to more travel.

**Raw materials** (`MaterialDefinition`, `Data/Materials`): stackable items that appear as deposits (placeholder rock clusters) on bodies. Hold E to collect. A harvested deposit returns after 1200 s of play time.

| Material | Where | Nodes / scene | Per node | Direct use |
|---|---|---|---|---|
| Regolith | Moon, Mars | 5 | 3-5 | none (refine) |
| Water Ice | Moon, Mars | 3 | 2-4 | none (refine) |
| Helium-3 Regolith | Moon | 2 | 1-2 | none (refine) |
| Iron Ore | Moon | 3 | 2-3 | none (refine, 1D upgrades) |
| Mineral Salts | Mars | 3 | 2-4 | lab reagent, 1 unit each |
| Methane Frost | Mars | 3 | 2-3 | ship fuel, 4 each |

**Refined products:** Fuel Cell (25 ship fuel), Lab Reagent (1 reagent unit), Alloy Ingot (kept for Phase 1D ship/tool upgrades).

**Refiner** (ship, `RefinerTerminal`; `RefineRecipe` assets in `Data/Materials`). One batch runs at a time. Inputs are taken when a batch starts, and cancelling gives them back. Outputs land in cargo when the batch finishes; if cargo is full, the batch waits for "Collect". Batches keep running while you are off the ship.

| Recipe | In | Out | Time |
|---|---|---|---|
| Electrolysis | 2 Water Ice | 1 Fuel Cell | 12 s |
| He-3 fuel pellets | 1 Helium-3 + 1 Water Ice | 3 Fuel Cells | 20 s |
| Regolith leach | 3 Regolith | 1 Lab Reagent | 8 s |
| Brine reagent | 1 Mineral Salts + 1 Water Ice | 3 Lab Reagents | 10 s |
| Smelt alloy | 3 Iron Ore | 1 Alloy Ingot | 15 s |

**Refuel ship** (on the refiner): fills the tank from fuel items in cargo, biggest first. Mk I tank is 100; Earth to Mars costs about 5.

**Lab costs** (reagent units, paid when an operation starts): Cleaning, Identification, Material and Age analysis 1 each; Civilization identification 2; Translation 2 per pass; Restoration 3; Technology analysis 3. Raw Mineral Salts count as reagent.

**Start kit:** 8 lab reagents and 2 fuel cells. Test durations are short on purpose; balance later.

**Save:** section `materials.state` holds harvested deposits and the refiner batch. Materials themselves are inventory items.

**Open / later:** credits value for selling materials (TradeHub vendors), refiner tiers (parallel batches, better yields), deposit models instead of placeholder rocks, scanner ping for deposits.
