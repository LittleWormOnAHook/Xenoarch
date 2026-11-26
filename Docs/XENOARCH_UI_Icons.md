# Xenoarch UI icon style (t151u-t155u)

Anthony approved the mockups in `Docs/Images/UI_Icons/` (01 hotbar, 02 inventory, 03 map markers, 04 pause menu + journal) on 2026-11-26. **Rule: show mockups before any visual UI change.**

## One style
- **Items:** flat white silhouettes (game-icons.net, CC BY 3.0, plus our own white Genesis tool glyphs) in `Art/Icons/GameIcons` and `Art/Icons/Items`. Every PNG is a 128x128 canvas with the glyph cropped and scaled to fill 78% of it, so every item gets the same size and padding in any slot.
- **Item tint by category** (`--xa-icon-*` in `Resources/UI/XenoarchPalette.uss`; classes `.xa-item-icon--*` in `Xenoarch.uss`):
  - tools, consumables, valuables and other: bone white
  - artifacts: heading orange
  - parts (Misc): teal
  - materials: their own colour at 35% over bone white, set in code because it differs per item
- **Menus, map markers and journal states:** Kenney Game Icons (CC0). Cropped copies are in `Art/Icons/UI` (glyph at 95% of 128x128), with classes `.xa-icon--{name}`.
  - Pause menu buttons use `.xa-glyph` (20 px, secondary text, dark on hover).
  - Journal lines use `.xa-journal-icon` (16 px): checkmark (teal) for done, arrowRight (orange) for available, locked (dim) for locked.
  - Map markers use `.xa-map-icon` (18 px over a 22 px dark halo): target (site red), star (cache gold), home (base), arrowDown (landing green). The player arrow and shuttle stay vector shapes.
- **Code:** `DMI.Xenoarch.UI.XenoarchIcons` (`ApplyItem`, `Glyph`, `EnsureStyles`) only adds classes. Sizes, images and tints live in the USS.
- **Not changed yet (not in the approved mockups):** the main menu glyphs (`.xa-glyph--play/continue/quit`, Genesis set), the crosshairs, terminal and vendor screens, and the settings, save-slot and confirm buttons.

## ZEN-22: Xenoarch icon library (replaces the flat-tint rule above)
- **Library:** `Assets/Xenoarch/UI/Icons/{Items,Tools,Resources,Artifacts,Research,Navigation,Ship,Interface,Unassigned}`,
  192 v3 icons cut from Anthony's two HD sheets (256 px, transparent, soft 2 px alpha edge). Manifest:
  `Assets/Xenoarch/UI/Icons/icon_manifest.csv` (sheet, row, col, file, use, matched ids); audit: `icon_item_audit.csv`.
- **Import:** Sprite (2D and UI), Single, Full Rect, alpha is transparency, sRGB, no mipmaps, Bilinear, Clamp,
  max 256, compression None. GUIDs are fixed (md5 of the path) so re-imports keep item references.
- **Items:** 56 item definitions point at library icons: 38 Matched plus 18 Needs-review best fits from the audit
  (Anthony, t166u: "insert all the icons"). 12 items have no library art and keep their old icon:
  material.helium3, artifact.mars_shrine_idol (pack art is a scientist bust), mars_bead_string, mars_dust_filter,
  moon_glyph_shard, moon_survey_spike, moon_vault_plate, vael_comb, vael_gauge, vael_hinge, vael_pendant, vael_scroll_case.
- **Slot layers:** slot background, icon (untinted), corner accent (`Icons/Slot/slot_corner_accent.png`, a separate
  white lower-left fade, max 28% alpha, tinted by USS), then the slot's own selection/hover border. `XenoarchIcons.ApplyItem`
  adds the accent child. Accent colours (`--xa-accent-*` in XenoarchPalette.uss): gray Common, blue Materials (materials,
  resources, valuables), teal Tech (tools, equipment, parts, ship modules, data), green Organic (no items yet), purple Rare
  artifacts, gold Exceptional+ artifacts. Category (`ItemDefinition.category`) and rarity (`ArtifactDefinition.rarity`)
  stay separate fields; `XenoarchIcons.AccentFor` combines them only for the one accent channel.
- **Menus / map / journal:** library icons where matched, shown untinted (`.xa-icon--pack`): pause Settings
  (icon_ui_settings), journal done (icon_ui_complete), map dig site (icon_nav_anomaly), base (icon_nav_base_outpost),
  landing zone (icon_nav_landing_zone). No library art yet, so Kenney stays: save, load, resume, main menu, quit, journal
  open/locked arrows and lock, map cache star. Crosshairs unchanged.