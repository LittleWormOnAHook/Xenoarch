# XENOARCH — Artifact and Research System

_Status: **Planned** (Phase 1C). Phase 1A foundations: `ItemDefinition.uniqueInstance`, `InventoryModel.AddInstance` (duplicate-proof), GUID instance ids, `WorldPersistence` consumed ids._

## Artifact definition (`ArtifactDefinition : DefinitionAsset`)
id, display name, unidentified display name, description (layered: unidentified / identified / researched), civilization id, estimated age range, material, category (Cultural, Tool, Jewelry, Ceremonial, Document, Tablet, Data crystal, Mechanical component, Scientific instrument, Navigation device, Power source, Ancient technology, Unknown), rarity (Common, Uncommon, Rare, Exceptional, Legendary, Unique), fragility, historical significance, technological significance, base value, research operations available, translation content (for documents), associated coordinates/technology/discoveries, linked item definition id (for inventory).

`Unique` rarity: generation excludes it from random tables; the discovery database records that it was found, so it can never be generated again.

## Artifact instance (state, section `artifacts.instances`)
instanceId (GUID), definitionId, condition (0–1), research completion per operation, translation completion (0–1), discovery location (site id, body id), discovered time, ownership (held/stored/sold/installed), sale record.

Inventory holds `itemId + instanceId`; the instance record holds everything else.

## Value
`value = base × rarityMult × condition × (1 + researchBonus) × significanceMult × buyerInterest`. Buyer interest comes from the vendor (Economy doc). Market demand is static per vendor in Phase 1.

## Research operations
Cleaning, Identification, Material analysis, Age estimation, Civilization identification, Restoration, Translation, Technology analysis. Each operation definition: duration, required lab capability level, required tools/modules, prerequisites (other operations), effects (reveal fields, condition change, value bonus, unlocks). Pattern borrowed from ASH/Genesis timed crafting (`CanCraft/TryCraft`), reimplemented.

## Translation
Separate progression: `translation = f(language knowledge of civ, translator equipment, related documents translated, lab capability, document condition)`, capped per tier, so partial translations are normal. Text is revealed in segments; segments can carry partial coordinates (precision grows with translation). Translated knowledge is copied into the discovery database (journal), so it survives selling the artifact.

## Civilization (`CivilizationDefinition`)
id, name (and unknown placeholder name), historical period, language id, writing system, symbol set, architecture style, tech traits, cultural traits, preferred materials, known locations, associated artifacts, historical records, disappearance status. Player knowledge per civilization stored in `knowledge.civilizations` (fields revealed, language knowledge 0–1).

## Phase 1 content
- One civilization (working name to be chosen with Anthony).
- ~30 placeholder artifacts across categories/rarities, including: Moon metallic tablet (document; partial translation → Mars coordinates), Mars damaged navigation device (→ extrasolar coordinates), alien sensor module (technology; installable scanner upgrade).
