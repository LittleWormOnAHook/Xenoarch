# XENOARCH — Excavation Design and Technology Decision

_Phase 1A investigation. Status: **Planned** (no excavation code yet; implementation is Phase 1C)._

## 1. Requirements (from the Phase 1 spec)
Detect a buried object → remove surrounding material → expose it progressively → use precision tools near fragile finds → recover it → move it to inventory. Tool choice affects artifact condition. Excavation state must persist (no respawn, no reset). Localized dig sites only; no whole-planet deformation.

## 2. Options compared

| Approach | How | Advantages | Limitations | Performance | Cost (solo dev) |
|---|---|---|---|---|---|
| **Unity Terrain heightmap modification** | `TerrainData.SetHeights` / `SetHeightsDelayLOD` under the tool | Built-in, collision for free, works with existing terrain painting | Heightfield only: no overhangs, tunnels, chambers, or objects partially embedded in walls; resolution limited (e.g. 1 m+ per sample on large terrain, too coarse for brush work); terrain shared by whole scene, so saving deltas = saving height patches; holes need TerrainHoles | Cheap for small patches; `SyncHeightmap` + collider rebake costs ms per edit | S–M |
| **Localized voxel excavation** (marching cubes/dual contouring on a small grid, e.g. 4×4×3 m at 5–10 cm) | Density field per dig site, remesh dirty chunks (Jobs + Burst), MeshCollider rebuild | True 3D: trenches, walls, overhangs, buried chambers; precise; easy to save (compressed density/material bytes per site); per-voxel material/hardness supports tool rules | Needs meshing code, chunking, collider rebakes; texturing via triplanar shader; must blend visually into surrounding terrain | Fine at site scale (a few 16³–32³ chunks); needs Burst for responsiveness | M–L |
| **Modular diggable soil volumes** | Site is a stack of discrete soil "cells"/layers (prefab blocks or layered slabs, e.g. 25–50 cm). Tools remove/thin cells; artifacts sit in cells | Very simple, deterministic, trivially saveable (bitset/levels per cell), easy tool rules (shovel removes cell, brush removes last layer), fast | Blocky unless dressed with decals/particles/smoothing; coarse resolution; limited organic shapes | Excellent | S |
| **Mesh-based excavation** (CSG/boolean subtraction on a mesh) | Subtract tool shapes from a ground mesh (runtime CSG libraries) | Arbitrary shapes, no grid | Runtime CSG is fragile (degenerate triangles, cost grows with edit count), hard to serialize (store op list and replay), vendor libs needed | Degrades over many edits | M–L, high risk |
| **ASH surface deformation** (`ASHDeformationField` + `ASHDeformer` + accumulation shader) | Ping-pong render texture (R = layer pushed aside) sampled by a displacement/blend shader | Proven in URP in-house; great for footprints, dust/regolith trails, brush strokes on a surface layer | **Visual only**: no collision, no volume, no buried objects; texture window follows the player; saves only via texture bytes | Cheap | Reuse for VFX: S. As excavation: not viable |

## 3. Recommendation
**Phase 1C prototype: modular diggable soil volume with per-cell depth (a "layered cell grid"), designed so its data model can later drive a voxel mesher.**

- Each excavation site defines a bounded grid (e.g. 6 × 6 m, 0.25 m cells, up to 2 m depth = 24×24 columns × 8 layers). Each cell stores remaining fill (0–1) and a material id (regolith, compacted, rock) with hardness.
- Tools act on cells under a ray hit with a radius: shovel removes 0.25 m quickly; precision tool 0.05 m; brush only removes the last 0.02 m "dust" and reveals; drill breaks hard material. Removal near an artifact cell (proximity check against artifact bounds) applies condition damage scaled by tool aggressiveness and artifact fragility.
- Rendering v1: per-column height mesh (a small heightfield mesh **inside the site bounds only**, rebuilt for dirty columns) + side walls; surrounding scene terrain gets a hole/cutout at the site footprint. This gives smooth-enough trenches without voxel meshing.
- Artifacts are spawned from site data at authored/seeded positions inside the grid; exposed fraction = how much of their bounds is above remaining fill. Recovery allowed at a configurable exposure threshold.
- Persistence: one save section `excavation.sites` storing per-site cell arrays (RLE/byte-packed) + recovered artifact instance ids. Reloading rebuilds the mesh from data; recovered artifacts never respawn (same consumed-id mechanism as Phase 1A `WorldPersistence`).
- Upgrade path: if trench walls/tunnels become important (underground structures), swap the per-column mesher for a Burst marching-cubes mesher reading the same cell data (cells → densities). Gameplay, tools and saves stay unchanged.
- Reuse ASH's deformation-field technique for **visual** brush strokes/dust on top of the cell mesh (optional polish).

Why not the others first: heightmap terrain cannot hold embedded objects or fine brush work; full voxels are the right long-term tech but too much engine work before the loop is proven; runtime CSG is fragile; ASH deformation has no volume.

## 4. Open questions for Anthony
1. Cell size target (0.25 m feels right for the shovel; precision/brush work then uses sub-cell fill). 
2. Do Phase 1 sites need vertical chamber walls/tunnels (Mars "buried chamber")? If yes, budget the marching-cubes mesher in 1C, or represent the chamber as an authored room revealed when a cell column breaks through.
3. Any vendor digging asset Anthony already owns (e.g. a voxel/terrain-dig package)? It could replace the mesher behind the same data model.
