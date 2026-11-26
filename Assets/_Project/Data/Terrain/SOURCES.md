# Terrain textures (Data/Terrain)

| File | Source | Licence | Notes |
|---|---|---|---|
| Mars_Soil.png | "Ground099" by ambientCG, https://ambientcg.com/view?id=Ground099 (copied from the Dark Matter Genesis 4K CC0 set `Terrain_HiRes_4K/Set05_Ash_Regolith/ACG_Ground099`) | CC0 1.0 (public domain) | Colour map desaturated and recoloured to Martian rust (slightly darker than the surface so dug layers read clearly), 2048 px. Replaces the flat placeholder; same asset GUID, so the terrain and Digger pick it up. |
| Moon_Soil.png | same source as above | CC0 1.0 | Recoloured to grey lunar regolith, 2048 px. |
| Terrain_Soil_Normal.png | Ground099 normal map (OpenGL), same source | CC0 1.0 | Downscaled to 2048 px; assigned to Mars_Soil / Moon_Soil terrain layers by Editor/XenoarchTerrainTextureImport. |

The Genesis project was only read from; nothing in it was changed.

## Sol PBR surface layers (Art/Terrain/SolPBR, t110u)

All copied (read-only) from the Dark Matter Genesis set `Assets/_Project/Art/Terrain/Terrain_HiRes_4K` (its README.txt per
texture records the original source and licence). Processed by `Tools/sol_terrain_textures.py` (repo root):
2048 px; albedo re-graded from luminance onto a body palette (the Genesis copies were tinted toward Io colours; regolith
also has its low-frequency blotches flattened so tiling is less visible); normal (OpenGL) resized; mask repacked for URP
(R metallic 0, G AO, B height, A smoothness).

| Layer folder | Original | Licence | Use |
|---|---|---|---|
| Mars_Regolith | "Ground099" by ambientCG, https://ambientcg.com/view?id=Ground099 | CC0 1.0 | Mars surface (slot 0), rust regolith |
| Mars_Rock | "rock_0005" by TextureCan, https://www.texturecan.com/ | CC0 1.0 | Mars rock on slopes |
| Mars_CrackedCrust | "ground_0046" by TextureCan, https://www.texturecan.com/details/652/ | CC0 1.0 | Mars duricrust patches on flats |
| Mars_SoilPBR | "ground_0004" by TextureCan, https://www.texturecan.com/details/17/ | CC0 1.0 | Mars dig walls (Mars_Soil layer), darker compacted soil |
| Moon_Regolith | "Ground099" by ambientCG | CC0 1.0 | Moon surface (slot 0), grey regolith |
| Moon_Rock | "Rock030" by ambientCG, https://ambientcg.com/view?id=Rock030 | CC0 1.0 | Moon rock on slopes |
| Moon_SoilPBR | "ground_0004" by TextureCan | CC0 1.0 | Moon dig walls (Moon_Soil layer) |

TerrainLayer assets: `Data/Terrain/Layers/TL_*.terrainlayer` (created once, never overwritten; edit them freely).
