# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([string]$PublishDirectory = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$publishRoot = [IO.Path]::GetFullPath($PublishDirectory, $projectRoot)
if (!$publishRoot.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Release must stay inside the workspace.' }
$executable = Join-Path $publishRoot 'CoffeeStrap.exe'
if (!(Test-Path -LiteralPath $executable)) { throw 'Publish CoffeeStrap before packaging.' }
if ((Get-Item -LiteralPath $executable).VersionInfo.ProductVersion -notlike '0.11.0*') { throw 'Expected CoffeeStrap 0.11.0.' }
foreach ($notice in @('LICENSE','THIRD_PARTY_NOTICES.txt')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination $publishRoot -Force
}
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
($hash + '  CoffeeStrap.exe') | Set-Content -LiteralPath (Join-Path $publishRoot 'SHA256SUMS.txt') -Encoding ascii
Write-Output ('Release ready: {0:N2} MiB executable, with consolidated license notices.' -f ((Get-Item -LiteralPath $executable).Length / 1MB))
