# Loading screen textures (Resources/UI/Loading)

Used by `LoadingScreenView` (FTL loading backdrop). Crops and bakes only — the source files stay where they are.

## Loading_MilkyWay.png
Crop and downscale of `Assets/_Project/Art/Skyboxes/Textures/Sky_MilkyWay_8k.jpg` (2048×1024, exposure lowered so the band stays faint).

Source: NASA/Goddard Space Flight Center Scientific Visualization Studio, **Deep Star Maps 2020**
(https://svs.gsfc.nasa.gov/4851), file starmap_2020_8k.exr (celestial coordinates, equirectangular).
Gaia DR2: ESA/Gaia/DPAC. The plain star map without constellation figures was used.
NASA SVS imagery is public domain / free to use with the credit above (NASA media guidelines); Gaia data credit
ESA/Gaia/DPAC (CC BY-SA 3.0 IGO for the Gaia-derived map). Keep this credit line in the game credits.

## Loading_PlanetLimb.png, Loading_PlanetEarth.png, Loading_PlanetMoon.png
Lit sphere bakes (atmosphere rim added in the bake) from the Solar System Scope albedos already in `Assets/_Project/Art/Space/Sol` (Mars 8k, Earth 8k, Moon 8k).
Licence: **CC BY 4.0** (https://creativecommons.org/licenses/by/4.0/). Attribution required in the game credits:
"Planet textures: Solar System Scope (solarsystemscope.com), CC BY 4.0".

## Loading_Galaxy.png
Original procedural spiral-galaxy sprite made for this screen (no third-party texture).
