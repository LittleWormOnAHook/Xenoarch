# XENOARCH — Discovery Chain System

_Status: **Planned** (Phase 1C). Builds on save sections and stable ids from Phase 1A._

## Model
A chain is a **graph of nodes**, not a list. `DiscoveryChainDefinition` (id, title, nodes). Each `DiscoveryNode`:
- id, type (Artifact recovered, Research op completed, Translation ≥ x, Coordinates known (precision ≥ x), Location visited, Site scanned, Technology installed, Civilization identified), target id(s), threshold.
- prerequisites: list of node ids with mode **All** / **Any** / **Count(n)** (branching and multiple prerequisites).
- optional flag (does not block completion).
- requirements to *attempt* (equipment tags, lab capability, translation tier).
- rewards on completion: reveal location/body/system, add known coordinates, unlock technology/research op, notoriety, journal entry.

Progress is event-driven: systems publish facts ("artifact X recovered", "translation of instance Y reached 0.4"); the chain evaluator marks nodes complete when conditions hold and prerequisites are satisfied. Partial information is represented by thresholds (e.g. coordinates at precision 0.4 reveal a region, 0.8 the site).

State (section `discovery.chains`): per chain, completed node ids, node progress values, completion time. Never resets on reload; facts are idempotent.

## Phase 1 chain (Moon → Mars → beyond)
1. Recover metallic tablet on the Moon (Moon_Excavation_01).
2. Identification: extraterrestrial origin (civ id revealed).
3. Translation ≥ 0.35 → partial coordinates on Mars (region) → Mars landing zone becomes selectable.
4. Orbital scan of Mars near coordinates → anomaly → site revealed.
5. Excavate buried chamber → recover damaged navigation device (+ optional: alien sensor module).
6. Research navigation device → coordinates outside Sol (new system entry, locked "future expedition").
Optional branch: sensor module → technology analysis → install on handheld scanner (scanner tier +1).
