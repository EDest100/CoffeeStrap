# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([string]$PublishDirectory = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$publishRoot = [IO.Path]::GetFullPath($PublishDirectory, $projectRoot)
if (!$publishRoot.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release must stay inside the workspace.' }
$executable = Join-Path $publishRoot 'CoffeeStrap.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Publish CoffeeStrap before packaging.' }
if ((Get-Item -LiteralPath $executable).VersionInfo.ProductVersion -notlike '0.10.1*') { throw 'Expected CoffeeStrap 0.10.1.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'branding\Discord-NOTICE.txt') -Destination $publishRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSES') -Destination $publishRoot -Recurse -Force
$readme = @"
CoffeeStrap 0.10.1 - Windows x64

Install the Microsoft .NET 10 x64 runtime, then run CoffeeStrap.exe. This build excludes .NET. Runtime download: https://dotnet.microsoft.com/download/dotnet/10.0 . Keep the license notices with redistributed copies.

The EXE contains the complete application payload compressed with the Windows Compression API. It verifies and extracts its application libraries to %LOCALAPPDATA%\CoffeeStrap\RuntimeCache\<payload-hash>, then uses Microsoft's native .NET hosting API in the same process. No app DLLs are needed beside the EXE, and no application dependencies are downloaded. This reduces the download size, not the logical size of the extracted application. Only this cache is marked for NTFS compression when supported; other files and Windows settings are untouched. Missing or damaged cached files are repaired on startup. You may remove RuntimeCache while CoffeeStrap is closed; it is separate from settings/mods and will regenerate. Each distinct payload has its own cache, so old build caches may be removed after closing those builds. COFFEESTRAP_RUNTIME_CACHE can point to another absolute cache directory with an existing parent.

Roblox downloads, network access and Windows system components are still required. This is the launcher, not an offline copy of Roblox. Preferences, mods and game downloads use your Windows user profile rather than this Release folder.

New Cloud volume maps setting: Original, 50% or 25% dimensions for built-in 3D cloud shape/detail maps and the 2D fallback. It applies after mod priority selection on the next launch and keeps existing smaller mip data without changing cloud simulation. Original removes the budget; disabling the mod/budget restores preserved files. It only affects experiences that use these default clouds. Tiny or unsupported compressed textures are retained and reported as skipped. Actual Direct3D WARP texture creation and byte readback pass; Roblox gameplay appearance and FPS/VRAM gains have not been measured.

Game Tweaks now exposes Alt+Enter fullscreen handling: Roblox default, Native fullscreen or Roblox-handled. Save and relaunch before using Alt+Enter. Native fullscreen availability depends on renderer/driver and can affect popouts, cursor behavior or switching apps. A performance gain is not guaranteed. The renderer is never silently changed. Explicit global handling takes priority over assigned experience profiles; Roblox default removes the global override and allows a profile's choice. Restore Roblox visuals clears this override too.

New Game Tweaks: apply a windowed client-size preset to a running Roblox player without restarting. Refresh players, choose the target and use Apply to player now. Restore previous size retains the first backup across preset changes; Keep this size, leaving Game Tweaks or closing Settings clears that restore point and keeps the applied size. Later manual resizing is respected. The selected preset is also saved for future launches. Fullscreen, maximized and minimized windows are skipped. Plain default stud shading and cloud detail mip budgets apply on the next CoffeeStrap launch. The graphics-10 / effects-1 pairing remains available. No process-memory editing or code injection is used. Gameplay appearance and performance vary by experience; no measured FPS gain is claimed.

Smaller Windows build: selects the Windows renderer directly, excludes development hot reload and duplicate UI resources, and links only the existing Ed25519 verification path from Bouncy Castle. Browser sign-in uses an installed Chromium browser in a separate temporary profile through its documented DevTools protocol. It no longer carries a general browser-automation stack or downloads Chromium. If no compatible browser is installed, use Quick sign-in or manual sign-in.

CoffeeStrap source and release history: https://github.com/EDest100/CoffeeStrap . This build includes CoffeeStrap Espresso in Community Mods; download and relaunch to apply. Custom game assets retain their own appearance. CoffeeStrap contains MPL-2.0 components; supply their corresponding source when redistributing as required by their licenses.
"@
$readme | Set-Content -LiteralPath (Join-Path $publishRoot 'README.txt') -Encoding utf8
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
($hash + '  CoffeeStrap.exe') | Set-Content -LiteralPath (Join-Path $publishRoot 'SHA256SUMS.txt') -Encoding ascii
Write-Output ('Release ready: {0:N2} MiB executable, with license notices. No duplicate ZIP or debug symbols.' -f ((Get-Item -LiteralPath $executable).Length / 1MB))
