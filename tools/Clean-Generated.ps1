# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
[CmdletBinding()]
param([switch]$KeepBuildCache)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$targets = [Collections.Generic.List[string]]::new()
$targets.Add((Join-Path $root 'artifacts'))
if (!$KeepBuildCache) {
    foreach ($project in @('Froststrap','backend','tools/Preview','libs/ColorPicker','libs/LucideAvalonia/LucideAvalonia','libs/FluentAvalonia/src/FluentAvalonia')) {
        $targets.Add((Join-Path $root "$project/bin"))
        $targets.Add((Join-Path $root "$project/obj/Release"))
    }
    $targets.Add((Join-Path $root 'backend/target'))
}
foreach ($directory in @('verification','verification-focused','verification-assets')) { $targets.Add((Join-Path $root ".build/$directory/fixture-data")) }
$active = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like "$root\*" -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -like '*CoffeeStrap.Preview.dll*') })
if ($active.Count) { throw 'A workspace app or preview is still running. Wait or close it yourself before cleaning; no process was stopped.' }
$total = 0L
foreach ($target in $targets) {
    $resolved = [IO.Path]::GetFullPath($target)
    if (!$resolved.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw "Cleanup target escaped the workspace: $resolved" }
    if (!(Test-Path -LiteralPath $resolved)) { continue }
    if ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Cleanup target is a link: $resolved" }
    $relative = [IO.Path]::GetRelativePath($root,$resolved)
    $tracked = & git -C $root ls-files -- $relative
    if ($LASTEXITCODE -ne 0 -or $tracked) { throw "Cleanup target contains tracked source: $relative" }
    $size = (Get-ChildItem -LiteralPath $resolved -Recurse -File | Measure-Object Length -Sum).Sum
    Remove-Item -LiteralPath $resolved -Recurse -Force
    $total += $size
}
foreach ($file in Get-ChildItem -LiteralPath $root -File -Filter 'revision*.log') { $total += $file.Length; Remove-Item -LiteralPath $file.FullName -Force }
[pscustomobject]@{ removedBytes = $total; removedGiB = [math]::Round($total / 1GB,3); release = 'Release/CoffeeStrap.exe'; sourceAndUserDataPreserved = $true } | ConvertTo-Json
