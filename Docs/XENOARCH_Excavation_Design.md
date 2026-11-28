# XENOARCH — Excavation Design and Technology Decision

_Phase 1A investigation, updated 2026-11-25 for Digger PRO. Status: **Planned** (gameplay in Phase 1C). Adapter interface + Digger adapter: **Implemented (compile-level)**._

## 1. Requirements (from the Phase 1 spec)
Detect a buried object → remove surrounding material → expose it progressively → use the Sonic Excavator's Fine focus and the Ultrasonic Brush near fragile finds → recover it → move it to inventory. Tool choice affects artifact condition. Excavation state must persist (no respawn, no reset). Localized dig sites only; no whole-planet deformation.

## 2. Options compared

| Approach | How | Advantages | Limitations | Performance | Cost (solo dev) |
|---|---|---|---|---|---|
| **Unity Terrain heightmap modification** | `TerrainData.SetHeights` / `SetHeightsDelayLOD` under the tool | Built-in, collision for free, works with existing terrain painting | Heightfield only: no overhangs, tunnels, chambers, or objects partially embedded in walls; resolution limited (e.g. 1 m+ per sample on large terrain, too coarse for brush work); terrain shared by whole scene, so saving deltas = saving height patches; holes need TerrainHoles | Cheap for small patches; `SyncHeightmap` + collider rebake costs ms per edit | S–M |
| **Localized voxel excavation** (marching cubes/dual contouring on a small grid, e.g. 4×4×3 m at 5–10 cm) | Density field per dig site, remesh dirty chunks (Jobs + Burst), MeshCollider rebuild | True 3D: trenches, walls, overhangs, buried chambers; precise; easy to save (compressed density/material bytes per site); per-voxel material/hardness supports tool rules | Needs meshing code, chunking, collider rebakes; texturing via triplanar shader; must blend visually into surrounding terrain | Fine at site scale (a few 16³–32³ chunks); needs Burst for responsiveness | M–L |
| **Modular diggable soil volumes** | Site is a stack of discrete soil "cells"/layers (prefab blocks or layered slabs, e.g. 25–50 cm). Tools remove/thin cells; artifacts sit in cells | Very simple, deterministic, trivially saveable (bitset/levels per cell), easy tool rules (excavator removes cell, ultrasonic brush removes last layer), fast | Blocky unless dressed with decals/particles/smoothing; coarse resolution; limited organic shapes | Excellent | S |
| **Mesh-based excavation** (CSG/boolean subtraction on a mesh) | Subtract tool shapes from a ground mesh (runtime CSG libraries) | Arbitrary shapes, no grid | Runtime CSG is fragile (degenerate triangles, cost grows with edit count), hard to serialize (store op list and replay), vendor libs needed | Degrades over many edits | M–L, high risk |
| **Digger PRO (vendor, installed)** | Voxel layer on top of Unity Terrain: Burst jobs, chunked meshes, LODs; editor sculpting + runtime API (`DiggerMasterRuntime.ModifyAsyncBuffured`) | True 3D (caves, overhangs, tunnels) blended into the terrain; Terrain holes handled for us; voxel layers by depth with texture + destructible/indestructible; per-slot runtime persistence built in | Vendor code (do not edit); voxel resolution tied to terrain resolution × chunk settings, so very fine brush work stays a gameplay/VFX layer; persistence is Digger's own files, not our JSON | Burst/Jobs, async buffered edits; fine at site scale | S (adapter) |
| **ASH surface deformation** (`ASHDeformationField` + `ASHDeformer` + accumulation shader) | Ping-pong render texture (R = layer pushed aside) sampled by a displacement/blend shader | Proven in URP in-house; great for footprints, dust/regolith trails, ultrasonic brush strokes on a surface layer | **Visual only**: no collision, no volume, no buried objects; texture window follows the player; saves only via texture bytes | Cheap | Reuse for VFX: S. As excavation: not viable |

## 3. Recommendation (decided 2026-11-25): hybrid, Digger PRO + Xenoarch cell data

Anthony chose Digger PRO (installed under `Assets/Digger`). The design is a **hybrid**:

| Layer | Owner | What it does |
|---|---|---|
| Gameplay truth | Xenoarch `ExcavationSite` (Phase 1C) | Per-site bounded cell grid (variable cell size per site, e.g. 0.25 m default, finer for delicate sites). Each cell: remaining fill 0–1, material id (regolith/compacted/rock/ice), hardness. Artifacts placed in cells. Tool rules, artifact exposure, condition damage, recovery threshold. Saved in our versioned JSON (`excavation.sites`). |
| World surface | Digger PRO via `IExcavationSurface` | Mirrors each accepted tool stroke as a voxel edit on the terrain (dig/add/smooth/paint holes). Gives the visual hole + collision, overhangs and tunnel walls. |
| Entrances to underground areas | Unity Terrain holes **or** Digger | Authored entrances: Terrain hole (PaintHoles) + authored cave/room prefab. Player-dug entrances: Digger dig strokes through to an authored chamber volume. |
| Polish (optional) | ASH deformation field | Dust/ultrasonic brush streaks on exposed surfaces. |

Why: Digger gives real 3D digging and terrain sculpting without us writing a mesher; our cell grid keeps tool/condition rules deterministic, cheap to test and save, and independent of the vendor (if Digger is ever replaced, only the adapter changes).

### 3.1 Code boundary (implemented)
- `DMI.Xenoarch.Excavation.IExcavationSurface` (runtime assembly): `Apply(in ExcavationEdit)` with `ExcavationStroke {Remove, Fill, Smooth, TerrainHole}`, `materialIndex`, `radius`, `strength`; `Persist(saveSlot)`; `IsBusy`.
- `DMI.Xenoarch.Integrations.Digger.DiggerExcavationSurface` (separate asmdef referencing `Digger.Core`, `Digger.Runtime`): maps strokes to `ActionType.Dig/Add/Smooth/PaintHoles` and calls `DiggerMasterRuntime.ModifyAsyncBuffured(position, BrushType.Sphere, action, textureIndex, opacity, size)`. `Persist` calls `SetPersistenceDataPathPrefix(slot)` + `PersistAll()`.
- Gameplay never calls Digger directly. No vendor files edited.

### 3.2 How the cell grid drives Digger (Phase 1C)
1. Tool ray hits the site → `ExcavationSite` resolves affected cells, applies tool rules (Sonic Excavator removes a whole cell layer quickly, and in Fine focus a thin layer; Ultrasonic Brush only the last dust layer; Sonic Burster breaks hard material), computes artifact damage.
2. For each accepted stroke the site emits one `ExcavationEdit` at the stroke centre (radius ≈ tool radius, strength from removed fraction, `materialIndex` from the exposed cell material) → Digger.
3. Rock/ice cells the current tool cannot break are refused by our rules first. Digger voxel layers can additionally be marked **indestructible** below the site floor (Advanced Voxel Generator depth layers) so nobody digs out of the site.
4. Artifacts are our objects (prefabs placed from cell data), not voxels; exposure is computed from cell fill, so Digger mesh precision does not affect gameplay.

### 3.3 Persistence
- Our save: cell arrays (byte-packed/RLE) + recovered artifact ids + consumed-id mechanism (no respawn).
- Digger save: its own voxel files under `Application.persistentDataPath/DiggerData/<slot>/`. We call `Persist(slot)` whenever we save, and on load set the same prefix before the scene's Digger initialises. Risk: two stores can drift (crash between writes). Mitigation for 1C: store a Digger "generation" counter in our save; if it mismatches, rebuild the surface by replaying site strokes from cell data (strokes are deterministic). Copy/delete the Digger folder when a slot is copied/deleted.

### 3.4 Authoring unique planet terrain with Digger (Moon/Mars)
Anthony wants Digger's editor sculpting for caves, overhangs and arches. Workflow:
1. Create/shape the Unity Terrain (heightmap, terrain layers). Digger uses the terrain's layers for voxel texturing; keep Moon (regolith/basalt/ice) and Mars (soil/dust/rock) layer sets small (≤ 4–8 layers).
2. `Tools > Digger > Setup terrains` adds Digger to the scene's terrains. On URP this imports Digger's URP 17 shader package automatically (or import `Assets/Digger/Shaders/URP/Digger-URP17-shaders.unitypackage` by hand).
3. Select the `Digger Master` object and sculpt in the Scene view: **Dig** (caves, tunnels), **Add** (overhangs, arches, cliffs), **Smooth**, **Paint**; brushes Sphere / HalfSphere / RoundedCube / Stalagmite (also upside-down for stalactites). Digger PRO extras: **Easy Overhangs** operation and **Spline Walker** (dig a tunnel along a Bezier spline: `Tools > Digger > Create Bezier Spline`).
4. Set chunk size / resolution once per scene before sculpting (changing them clears edits). Keep the `Scene data folder` under `Assets/_Project/Data/Digger/<Scene>` so authored voxel data lives with our scenes and in git (Git LFS if it grows).
5. For dig sites in sculpted terrain: author site bounds in our `ExcavationSite`, optionally pre-dig the trench rim in the editor, and mark the bedrock below the site indestructible.
6. For play: `Tools > Digger > Setup for runtime` adds `DiggerMasterRuntime`; add `DiggerExcavationSurface` next to it. Optionally `Tools > Digger > Setup NavMeshComponents` if NPCs/drones need navmesh in caves.
7. Before lighting bakes: `Tools > Digger > Prepare lightmapping`; for static, non-diggable showcase caves `Save meshes as assets`.
8. Large underground complexes (temples, chambers) stay authored prefabs; Digger provides the natural cave/tunnel approach and the entrance cut.

### 3.5 Digger PRO install check (2026-11-25)
- Folder: `Assets/Digger` (46 MB; modules Core, Runtime, AdvancedOperations, Splines; 8 asmdefs; demo scenes under `Digger/Demo`).
- Dependencies: Burst 1.8.29, Collections, Mathematics and AI Navigation present. All 8 Digger assemblies compiled with no errors (`Library/ScriptAssemblies/Digger.*.dll`, 12:59 ET); no `error CS` or exceptions in Editor.log after import.
- URP: ships `Shaders/URP/Digger-URP17-shaders.unitypackage` (matches URP 17.4). Not yet unpacked; Digger imports it automatically on `Setup terrains` when URP is active. Built-in `Shaders/Standard` shaders are unused on URP (harmless). HDRP package is irrelevant.
- Runtime API used: `DiggerMasterRuntime.ModifyAsyncBuffured` / `ModifyAsync` / `Modify`, `ModificationParameters` (Position, Brush, Action, TextureIndex, Opacity, Size, IsIndestructible, Callback), `PersistAll`, `DeleteAllPersistedData`, `SetPersistenceDataPathPrefix`, `SetupRuntimeTerrain`. Compatible with the cell-data design above.
- Not done yet (needs a terrain scene): an in-editor test dig on a URP terrain to confirm materials are not pink and edits persist across a reload. Phase 1C first task.

## 4. Open questions for Anthony
1. Default cell size (proposal 0.25 m; delicate sites 0.1 m).
2. Should Moon/Mars placeholder scenes switch from primitive ground to Unity Terrain + Digger now (end of 1A) or at the start of 1C?
3. Git: Digger scene data can get large; OK to enable Git LFS for `Assets/_Project/Data/Digger/**`?
