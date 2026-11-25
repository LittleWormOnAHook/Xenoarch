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
