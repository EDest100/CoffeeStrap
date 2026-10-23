# CoffeeStrap branding and retained upstream identities

CoffeeStrap is an independent local fork of Froststrap, Fishstrap and Bloxstrap. Local product metadata, executable, app icon, menus, dialogs, picker titles and suggested filenames use CoffeeStrap. The brown gradient logo and cup accent are real source assets in `branding/` and compiled application resources. Original copyright headers, contributor credits, licenses, upstream README and artwork remain.

The 0.5 audit checks 33 resource cultures, visible XAML/C# strings, enum labels and app metadata, with no unexpected matches in the audited fields. Resource keys and legacy numeric icon IDs remain compatible; old Froststrap icon choices resolve to CoffeeStrap assets and duplicate choices are hidden. New UI copy is English pending translation.

[Swiftify](https://github.com/shredos67/Swiftify) informed round surfaces, compact navigation and hierarchy. No Swiftify code or artwork was copied. The logo-only Discord control links to https://discord.gg/CZ3P4Rw9D4; glyph licensing is in `branding/Discord-NOTICE.txt` and the Windows package's `Discord-NOTICE.txt`.

## Deliberate exceptions

- About, contributor and license pages identify the original projects. These are attribution, not missing product renames.
- Internal `Froststrap` namespaces, resource/serialization keys, legacy enum IDs, interprocess locks, platform compatibility filenames, environment overrides and Studio plugin filenames remain where changing them could break compatibility. Internal source/log names are not product-facing labels.
- Existing upstream configuration/community catalog, support/documentation, mod-generator and Studio-plugin URLs remain. No CoffeeStrap online service or repository was fabricated. Remote catalog data remains upstream-provided; the current shell does not display its upstream alert banner as a CoffeeStrap notice.
- Upstream release URLs and Discord application IDs remain in legacy source contracts, but automatic updates and launcher/game/Studio Discord presence are inert in this revision. No external Discord application was registered. An outdated-version notice requires a newer compatible CoffeeStrap release and a verified CoffeeStrap destination; none currently exists.
- Original/legacy assets remain as compatibility resources and license evidence. The app's default icon, executable and window branding use CoffeeStrap.

Audit: `artifacts/previews-v0.5/branding-verification.json`. This is not a claim that every source occurrence of an upstream name was deleted. Prior branding records remain with the prior-version backups.
