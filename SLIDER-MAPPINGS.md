# Current 0.7.3 graphics mappings

- **Maximum distance + lowest effects** sets `SavedQualityLevel=10` in Roblox's GlobalBasicSettings XML and `DFIntDebugFRMQualityLevelOverride=1` in ClientAppSettings. This follows Froststrap's documented pairing and was confirmed in game by the user on the current client. No separate far-plane flag, game script, hook or memory access is needed.
- The persisted `LockMaximumRenderDistance` key now means maximum **saved slider**, not FRM 21. It pins SavedQualityLevel to 10 on GBS load/save and fresh-disk player-launch preparation. It supplies FRM 1 only when no override exists; an explicit 1–21 selection is preserved. Disabling the preference returns the saved slider to manual control without clearing the selected FRM value. Disabling the FRM override exits the paired mode.
- The native saved slider is disabled while maximum-distance mode is selected; the 1–21 FRM control remains enabled. Leave Roblox's in-game slider at 10. Its live value is not continuously monitored or overwritten; lowering it during play can reduce distance. The game still owns streaming and fog.
- The plastic preset now uses saved slider 10 / FRM 1, texture override 3 and its existing material replacements. The standalone distance/effects preset preserves texture, mesh, renderer and mod settings.

## Historical 0.7.2 mappings (superseded above)

- Override Graphics Quality level is the requested 1–21 FRM override, not an independent effects or distance flag. The saved 0–10 in-game slider remains editable even while maximum-distance mode is on; FRM 21 still controls both rendering distance and quality. Current supported local flags cannot split those behaviors.
- Black-texture compatibility selects `FFlagDebugGraphicsPreferD3D11=True`, removes Vulkan/OpenGL preferences and paused voxel lighting, and selects texture override 3. It preserves the FRM selection and lock. Global explicit renderer/texture/voxel settings take priority over per-place profiles. It is a workaround to test, not a proven live fix.
- Flat plastic assets retain original 128×2048 color/normal atlas and 512×512 detail sizes; water normals retain 256×256. All include complete mip chains, with new cache keys replacing old 4×4 outputs. Particle color lookups retain 128×32 and hidden shape maps retain their shipping dimensions.
- Fire, smoke, sparkles and forcefield styles persist in Settings.json and use the normal original-file backup/restore path. Installed content mods win. Custom game-provided images are not redirected.
- FPS telemetry and its helper are removed; optional game-name captions remain.

## Historical 0.7.1 mappings (superseded above)

- Maximum-distance lock defaults on for new settings and configurations missing the new option. It enforces `DFIntDebugFRMQualityLevelOverride=21` at flag load/save and launch sanitization. Conflicting per-place profiles and saved in-game graphics positions do not lower it. The advanced rendering control and saved slider are disabled in this mode.
- Disabling the lock removes the fixed FRM value and allows the advanced 1–21 override again. Lower values are not a separate effects control and can produce black distant textures. Restoring Roblox visuals explicitly turns the lock off.
- Plastic textures + maximum distance selects lock on/FRM 21, texture override 3, saved graphics 1, saved MaxQuality/Vignette off, normal voxel updates, and flat plastic color/normal/detail maps. Game-authored lighting, materials and post effects are not overridden.
- Texture quality remains independent (0–3, both allowlisted texture override flags). Grass/mesh mappings remain unchanged.
- Generated DDS textures now include every mip down to 1x1, including non-power-of-two dimensions. Prepared texture cache names changed so earlier single-level files are regenerated. Default sky faces also replace existing indoor faces unless the user turns that option off or a mod explicitly supplies an indoor face.

## Historical 0.7.0 mappings (superseded above)

# Current 0.7.0 graphics mappings

- Texture quality: integer 0–3, `DFFlagTextureQualityOverrideEnabled=True` plus `DFIntTextureQualityOverride`. Off removes both. Selected global keys take precedence over per-place profiles at launch. Roblox can change exact visual behavior.
- Effects quality override: integer 1–21, `DFIntDebugFRMQualityLevelOverride`. This is the former mislabeled Render distance control. Higher levels can enable lighting effects; it does not provide an independent draw-distance lock.
- In-game graphics level: saved XML 0–10. The high-texture/low-effects preset selects 1, texture override 3, FRM override 1, and disables saved MaxQuality/Vignette. The preset does not claim to disable every game-authored post effect.
- New visual asset settings persist in Settings.json and use the same backup/restore path as mods on the next launch. Sky palette replaces default faces; smooth water replaces 25 normal maps; smooth plastic replaces two normal maps; hidden celestial discs replace sun/moon images. Game-authored assets can take precedence.

## Historical mappings (superseded where noted above)

# Active 0.6 customization

Mesh Detail is now exposed again. Its 0–9 integer mapping and four stored values are exactly the table below. Reading the first threshold rounds to the nearest level, fixing the former 5-to-4 persistence defect. Disabling removes all four keys; level zero deliberately stores zero. Each advanced threshold can instead be set independently from 0 to 10,000; custom distances are not a mesh-resolution percentage.

Grass minimum/maximum distances independently support 0–10,000; motion factor supports 0–100. These are explicit local overrides, not verified universal game effects. Custom grass values are displayed as Custom values in the preset selector rather than being mislabeled Roblox default.

The Roblox graphics dropdown now includes a fixed 1–21 rendering level via `DFIntDebugFRMQualityLevelOverride`. It remains separate from the saved 0–10 graphics-quality slider. Final launch sanitization reapplies the user's chosen rendering level after any per-place profile and removes that profile's rendering override when the control is disabled. The flag affects additional quality features as well as viewing distance. The permanent-in-session effect still needs a live Roblox graphics-slider test; file persistence alone does not prove it.

The 18-key allowlist was refreshed from Roblox's official DevForum JSON on October 21, 2026: https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569.json . Graphics changes apply on a subsequent Player settings load/launch. Restore Roblox visuals removes all curated flag overrides and disables the saved vignette/max-quality effects; it does not replace experience-authored lighting.

## Historical 0.4/0.5 inspection

The following notes describe the prior UI and the legacy defect; the active changes above supersede the statements that mesh customization is absent.

# Existing slider mappings (inspection before 0.5 changes)

The active 0.4 Game Tweaks UI has **no mesh-detail slider**. It exposes six enum cards. The retained legacy FastFlagsViewModel and preserved 0.3 UI have the following exact mapping; these are code values, not measured Roblox effects.

Mesh Detail range: 0-9, integer steps. For n, write floor([2000,1500,1000,500] * n / 9) to `DFIntCSGLevelOfDetailSwitchingDistance`, `DFIntCSGLevelOfDetailSwitchingDistanceL12`, `DFIntCSGLevelOfDetailSwitchingDistanceL23`, and `DFIntCSGLevelOfDetailSwitchingDistanceL34`, respectively. Enabling sets n=5. Disabling removes all four overrides; n=0 writes four zeros rather than resetting. The legacy getter uses floor(firstValue*9/2000), so n=5 reads back as 4. This legacy defect is not a verified linear detail scale, and the unused slider has not been restored.

| Level | Four stored values |
|---|---|
| 0 | 0, 0, 0, 0 |
| 1 | 222, 166, 111, 55 |
| 2 | 444, 333, 222, 111 |
| 3 | 666, 500, 333, 166 |
| 4 | 888, 666, 444, 222 |
| 5 | 1111, 833, 555, 277 |
| 6 | 1333, 1000, 666, 333 |
| 7 | 1555, 1166, 777, 388 |
| 8 | 1777, 1333, 888, 444 |
| 9 | 2000, 1500, 1000, 500 |

The flag names suggest CSG level-of-detail distance thresholds, not a universal mesh resolution percentage. They are in the published allowlist snapshot, but official exact visual/numeric semantics were not found. Nothing was measured in a live game. File edits apply on a subsequent client settings load/launch; no live hot-reload was verified.

Other 0.4 controls:

- Texture quality enum: Default removes both keys; Levels 0/1/2 set `DFFlagTextureQualityOverrideEnabled=True` plus `DFIntTextureQualityOverride=0/1/2`. Numeric semantics are not officially specified. Not texture unloading; next launch; no benchmark.
- MSAA enum: Default removes `FIntDebugForceMSAASamples`; Off/2x/4x write 1/2/4. Renderer may ignore/fall back. No measured FPS gain.
- Renderer enum: Default removes all three preference flags; chosen D3D11/Vulkan/OpenGL sets only that preference True. Availability/fallback is client-dependent; next launch.
- Preserve render resolution: Default removes `DFFlagDisableDPIScale`; On sets True. Intended DPI render-scaling preference, not a measured performance boost.
- Gray sky: Default removes `FFlagDebugSkyGray`; On=True. No live effect verified.
- Grass: Default removes three grass keys; Hidden sets min/max distance and movement factor to 0. Existing project preset, no general claim about all grass/assets.
- Saved graphics quality slider: 0-10, integer steps, clamps writes to `GlobalBasicSettings` `SavedQualityLevel`; 0 is automatic, missing-value getter fallback 1, while the bundled new-file template starts at 2. No dedicated reset button. Fixture persistence passed. It correctly writes quality, not Studio volume, after the 0.4 fix. No live update verified.
- Saved FPS text field: writes `FramerateCap`; any value below 1 becomes -1 (default sentinel), missing-value fallback 60. Not an external live FPS switcher.
- Former Global Settings audio sliders: 0-1, step 0.1, MasterVolume/MasterVolumeStudio and VoiceChatVolume map to their same-named XML fields. The old party slider uses a missing alias `Audio.PartyChatVolume` while the registry defines `Audio.PartyVoiceVolume`; it therefore does not write that intended XML field. Missing getter fallback 1.0; no dedicated reset. Haptic slider also used 0-1/0.1 but its VM property was int, so fractional values were not reliably represented. That obsolete Global Settings surface is removed by this revision rather than presenting it as a proven effect.

Account Permissions is `AllowCookieAccess`: access to Roblox authentication cookies for authenticated API behavior, not OS ACL permissions. Default remains false; default-on needs explicit security-sensitive confirmation. No cookie value was read for this inspection.
