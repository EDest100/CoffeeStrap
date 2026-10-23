# CoffeeStrap Espresso

Espresso shadows, caramel accents and cream highlights for local Roblox artwork. Install from **Texture Editor > Community Mods > CoffeeStrap Espresso**, save and relaunch Roblox through CoffeeStrap. This uses the same download, priority, backup and restore pipeline as other community mods.

## What's themed

- Local menu, settings, backpack, chat, loading, controller and avatar UI PNGs, including available sprite sheets.
- Keyboard/mouse and other local cursor PNGs. Alpha and image dimensions are preserved.
- CoffeeStrap's own mark on the available loading, top-bar and in-game-menu logo images. These deliberate logo replacements retain the destination dimensions.
- BuilderIcons Regular and Filled icon fonts, with caramel COLR/CPAL color glyphs. Text fonts, character mappings, outlines, names and metrics are preserved.
- Six default sky faces and their indoor counterparts, generated as matching coffee-colored cube faces with full mip chains.
- Default fire, smoke, sparkle and forcefield color ramps. Particle shapes and alpha masks are preserved.
- A warm, pale default plastic diffuse map. Normal maps and other rendering data are left alone.

Games control their own uploaded textures, scripted colors, lighting, remote images and sky asset IDs. Roblox can also change or retire local paths. File replacement cannot override all of those. This pack doesn't inject code/shaders, edit process memory, patch executable binaries, change FastFlags or change the saved graphics 10 / FRM 1 pairing. It isn't an FPS optimization claim.

## Disable, restore and conflicts

Use **Disable** on the card, then relaunch through CoffeeStrap to restore the backed-up originals. The trash icon moves the installed pack to the launcher's local ModTrash and removes its registration; the next launch restores originals. A higher-priority enabled mod or explicit built-in visual tweak may override a theme asset. Check **Last file-mod application** for exact file outcomes. Don't manually overwrite your live Roblox installation to test the pack.

## Regenerate from source

The generator is [CoffeeThemeBuilder.cs](../tools/Preview/CoffeeThemeBuilder.cs), compiled into the development preview tool only. The palette and generated sky are source code, not a downloaded executable tool. No theme ZIP is embedded in the launcher.

1. Build the preview tool as described in [BUILDING.md](BUILDING.md).
2. Obtain an **unmodified** Roblox client content tree, containing `content/textures`, `ExtraContent/textures`, `ExtraContent/LuaPackages` and `PlatformContent/pc/textures`. Do not use an installation containing other mods as your input. Official client ZIPs put these roots in separate packages; the version must match across packages.
3. Run:

```powershell
dotnet tools/Preview/bin/Release/net10.0/CoffeeStrap.Preview.dll --build-coffee-theme "C:\path\to\pristine-client" .build/theme-package version-9ac8595712b447a7
```

The output is `CoffeeStrap-Espresso.zip`, an asset preview and a build manifest. The ZIP has a per-file source hash/coverage manifest, fixed timestamps and full-mip generated DDS maps. The original input is read only. The preview shows actual pack assets; it is not a Roblox screenshot. Original Roblox artwork/icon outlines remain owned by their respective owners; the CoffeeStrap generator and original theme code use MPL-2.0. Keep original font attribution tables.

The font writer follows Microsoft's [COLR](https://learn.microsoft.com/en-us/typography/opentype/spec/colr), [CPAL](https://learn.microsoft.com/en-us/typography/opentype/spec/cpal) and [OpenType checksum](https://learn.microsoft.com/en-us/typography/opentype/spec/otff) specifications. It adds color tables to monochrome TrueType icon fonts without changing outlines or executable code.
