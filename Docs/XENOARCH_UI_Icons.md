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
