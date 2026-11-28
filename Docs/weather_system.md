# Weather system (t313u)

Ported from ASH's weather (`Assets/_ASH/Scripts/World/Weather`: profile-per-state, director with blending and a scheduler of calm periods and storm-class events, shaped gusts, ambience loops with muffling, thunder) into `DMI.Xenoarch.Weather` (`Assets/_Project/Scripts/Runtime/Weather`). ASH itself was only read. ASH's Enviro 3 prefabs and purchased QP01 wind/rain clips are not reused (third-party licenses); wind, rain, grit and helmet static are synthesised in code, thunder is ASH's CC0 clips.

* `WeatherDirector` is created at runtime (no placed objects). It picks the body's `WeatherProfile` on each scene load, never runs in `Ship_Starter`, `TradeHub_Sol`, menu/bootstrap or while the loading screen is up, and follows the player camera.
* `WeatherProfile` (Assets/_Project/Data/Weather, one per body + one default per climate, listed in `Resources/Weather/WeatherCatalog`): atmosphere pressure, temperature, wind, sound character, particle colours and the list of weather states with weights, durations and storm flags.
* Particles: pooled `ParticleSystem` volumes around the camera (rain, snow, ash, embers, blowing dust, regolith motes) + up to 3 pooled dust devils; capped by a budget; fall speed = drag-limited terminal speed scaled by sqrt(planet gravity) and air density; airless motes are ballistic at the planet's gravity.
* Sound: wind (soft/strong/howl by strength) volume, pitch and dullness come from atmosphere density (vacuum silent, Mars faint and low); rain soft/heavy; grit; helmet static on airless bodies; thunder delayed by distance. Muffled inside the rover.
* Fog and sun dimming (storms) are applied relative to what the scene/sun director set and restored afterwards.

## Test controls
Catalog (`Resources/Weather/WeatherCatalog.asset`): `weatherEnabled`, `intensityMultiplier` (0-3), `extremePreset`, `particleBudgetScale`.
Hotkeys (need `enableDebugHotkeys`): Ctrl+Shift+W next weather, E extreme preset, Q weather on/off, = / - intensity +-0.25, R back to scheduler, 0 reset test settings. Menu: Xenoarch > Weather (Create Weather Assets, Reset Profiles To Defaults, Next Weather, Toggle Extreme Preset, Print Current Weather).
Runtime changes persist in PlayerPrefs (`xenoarch.weather.*`); `0` clears them.
