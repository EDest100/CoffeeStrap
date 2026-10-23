# One EXE, smaller download

CoffeeStrap 0.10.1 keeps the complete Windows application inside one approximately **13.4 MB EXE**, compared with 48.1 MB in 0.10.0. It still requires the separately installed **.NET 10 x64 runtime**. No app DLLs sit beside the distributed EXE. Fonts, all 33 translated cultures, Avalonia, Skia, HarfBuzz, ANGLE, mods, flags, browser sign-in and signature verification remain included.

This is a download-size reduction. The application libraries total about **47.8 MB logically**. At startup the host prepares `%LOCALAPPDATA%\CoffeeStrap\RuntimeCache\<payload-sha256>`. On the tested NTFS volume, `GetCompressedFileSizeW` totals approximately **28.0 MB** for those cached files; filesystem metadata is additional. Unsupported volumes use ordinary files. This cache compression affects only CoffeeStrap's own payload directory, not settings, mods, Roblox, other files, GPU settings or Windows policy.

## How it runs

The regular .NET SDK publishes a framework-dependent, uncompressed single-file bundle. `Build-Portable.ps1` reads that verified manifest and preserves every application entry byte-for-byte. Windows' documented Compression API compresses their concatenated data using LZMS buffer mode with a 64 MiB block limit. Default, 16 MiB and 64 MiB blocks were compared before selecting this setting. A small x64 Windows host contains the compressed data, manifest, original icon, application manifest and version resource.

At startup the host checks the bounded manifest and hashes. A named mutex serializes cache preparation. Missing or damaged files are decompressed, verified and replaced using unique adjacent staging files and atomic replacement. Existing files are hashed before loading. Linked cache paths, duplicate destinations and unexpected DLL/JSON files cause an explicit startup error. SHA-256 here detects damaged/mismatched cached data; it does not replace verifying the download's published checksum or publisher identity.

The statically linked Microsoft `nethost` API finds the installed `hostfxr`. The host calls `hostfxr_initialize_for_dotnet_command_line` and `hostfxr_run_app` to run CoffeeStrap **in its own process**, on an STA UI thread. `Environment.ProcessPath` remains the distributed EXE, so shortcuts, protocol registration, relaunching and launcher copies retain their existing target. No child launcher, process-memory access, injection, game patching or network dependency download is used. This is a framework-dependent native host, not SDK single-file compression; the SDK's self-contained-only compression restriction remains respected.

Cold startup includes decompression and cache writes; later startups reuse verified files. In an interleaved local `--version` probe, one new cache took roughly 1.09 seconds versus 0.17 seconds for the raw SDK bundle. Five subsequent launches had medians of 0.219 seconds and 0.162 seconds respectively. These are command-line elapsed times on one development machine with filesystem/OS caches warm, not GUI startup, clean-machine, FPS or cross-hardware benchmarks.

## Cache location and repair

- Settings, profiles, credentials, mods and Roblox downloads retain their existing locations outside `RuntimeCache`.
- With CoffeeStrap closed, removing the runtime cache is safe; the next launch regenerates it. Different payloads use separate hash directories. Close all old builds before clearing their cache directories. The host does not delete caches that another build might still use.
- `COFFEESTRAP_RUNTIME_CACHE` optionally selects another absolute directory. Its parent must already exist and paths must not be links. Development checks use workspace-local overrides and do not touch user settings.
- Locked or unwritable cache files produce an error and retain original files. Clear the lock or correct the directory permissions and retry. A damaged embedded payload requires downloading the EXE again.
- A missing/incompatible .NET installation produces a runtime-install message. No runtime is silently installed. A fresh machine without .NET has not been tested in this pass.

## Implementation and references

The authoritative host and parser are in `tools/Portable/Host.cpp`. Its tests include that exact implementation. `PayloadBuilder.cs` and `PayloadInspector.cs` are development tools, not bundled dependencies. The format uses a versioned header, bounded file manifest with relative names/sizes/SHA-256, aggregate hashes and one compressed data buffer. It limits the file count to 1,024, each file to 64 MiB and expanded data to 128 MiB. The packager includes only the Windows x64 application payload. Build outputs remain inside the workspace; active release EXEs are left untouched.

Primary references:

- [Microsoft native .NET hosting](https://learn.microsoft.com/en-us/dotnet/core/tutorials/netcore-hosting) and the exact [.NET 10.0.12 hostfxr declarations](https://github.com/dotnet/runtime/blob/v10.0.12/src/native/corehost/hostfxr.h).
- [Windows Compression API](https://learn.microsoft.com/en-us/windows/win32/cmpapi/using-the-compression-api), including algorithm tradeoffs and buffer mode.
- [Documented LZMS block-size limits](https://learn.microsoft.com/en-us/windows/win32/api/compressapi/ne-compressapi-compress_information_class) and [SetCompressorInformation](https://learn.microsoft.com/en-us/windows/win32/api/compressapi/nf-compressapi-setcompressorinformation).
- [Per-directory compression inheritance](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_set_compression) and [compressed-file size](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getcompressedfilesizew).
- [Extended Windows paths](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation), used without changing global long-path policy.
- [.NET single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview). Blind trimming/AOT, removing translations/renderers/fonts and bypassing the SDK compression guard were rejected. No additional compression dependency was installed.

Microsoft's `nethost` MIT notice is retained in `LICENSES/dotnet-nethost-MIT.txt`. Source remains available in the CoffeeStrap repository. Final size, hashes, file inventory and native/CLI checks are emitted to `.build/release-size.json`, `.build/release-checks.json` and the release checksum file.
