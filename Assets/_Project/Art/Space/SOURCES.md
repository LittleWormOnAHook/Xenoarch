# Space art sources (t105u)

## Sol planets (Art/Space/Sol)
Earth (albedo 8k, clouds, normal, specular), Moon 8k, Mars 8k:
**Solar System Scope** textures, https://www.solarsystemscope.com/textures/ (based on NASA imagery / elevation data).
Licence: **CC BY 4.0** (https://creativecommons.org/licenses/by/4.0/). Attribution required in the game credits:
"Planet textures: Solar System Scope (solarsystemscope.com), CC BY 4.0". Earth daymap/clouds/normal and Moon were
fetched from their Wikimedia Commons uploads (File:Solarsystemscope_texture_8k_*.{jpg,tif}, CC BY 4.0, author
Solar System Scope); Mars and the Earth specular map from solarsystemscope.com directly. Resized / re-encoded
(clouds, normal, specular to 4k) only.

## PlanetPack02 (Assets/_Vendor/PlanetPack02, NOT in git)
Unity Asset Store "Planets Pack" (textures made with Grand Designer, ignishot.com), copied from Anthony's
Dark Matter Genesis project (Genesis untouched). Standard Unity Asset Store EULA (single entity licence held by
Anthony / Dark Matter Interactive): use in his projects is allowed; redistribution of the raw assets is not, so the
folder is under the gitignored Assets/_Vendor/. The vendor built-in shaders are not used; URP ports are
Art/Shaders/Xenoarch_Planet.shader and Xenoarch_PlanetAtmosphere.shader (rewritten, not copied), and the
materials in Art/Space/PlanetPack reference the vendor textures by GUID (they show untextured on a clone
without the vendor folder). Used for: Jupiter (Jupiter.jpg), exoplanet arrival views in Proxima (earth5),
Barnard's Star (earth4), Epsilon Eridani (earth2), and the two nebula skies (Background3/4).
