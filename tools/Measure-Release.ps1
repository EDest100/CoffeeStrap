# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([string]$Executable = 'Release/CoffeeStrap.exe')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$exe = [IO.Path]::GetFullPath($Executable, $root)
$sdk = (& dotnet --version).Trim()
$sdkRoot = Split-Path (Get-Command dotnet).Source -Parent
[Reflection.Assembly]::LoadFrom((Join-Path $sdkRoot "sdk/$sdk/Microsoft.NET.HostModel.dll")) | Out-Null
[long]$header = 0
if (![Microsoft.NET.HostModel.Bundle.Bundler]::IsBundle($exe,[ref]$header)) { throw 'Expected an actual .NET single-file executable.' }
$stream = [IO.File]::OpenRead($exe)
$reader = [IO.BinaryReader]::new($stream)
$entries = @()
try {
    $stream.Position = $header
    $major = $reader.ReadUInt32(); $minor = $reader.ReadUInt32(); $count = $reader.ReadInt32(); $bundleId = $reader.ReadString()
    if ($major -ne 6 -or $count -lt 1 -or $count -gt 1024) { throw 'Unsupported single-file manifest.' }
    $stream.Position += 40
    for ($index=0; $index -lt $count; $index++) {
        $offset = $reader.ReadInt64(); $size = $reader.ReadInt64(); $compressed = $reader.ReadInt64(); $type = $reader.ReadByte(); $name = $reader.ReadString()
        if ($offset -lt 0 -or $size -lt 0 -or $compressed -ne 0 -or $offset + $size -gt $header) { throw 'Invalid framework-dependent bundle entry.' }
        $entries += [pscustomobject]@{ name=$name; size=$size; offset=$offset; type=$type }
    }
    foreach($pattern in @('^Puppeteer','^WebDriverBiDi','^HotAvalonia','^Avalonia\.(Desktop|X11|Native|Wayland|FreeDesktop)','^NWayland','^(coreclr|hostfxr|hostpolicy)\.dll$')) {
        if ($entries.name -match $pattern) { throw "Unexpected Windows release dependency: $pattern" }
    }
    $crypto = @($entries | Where-Object name -eq 'BouncyCastle.Cryptography.dll')
    if ($crypto.Count -ne 1 -or $crypto[0].size -ge 100KB) { throw 'Expected the compact signature-verification dependency.' }
    $stream.Position = $crypto[0].offset
    $cryptoDirectory = Join-Path $root '.build/crypto-release-probe'
    New-Item -ItemType Directory -Path $cryptoDirectory -Force | Out-Null
    $cryptoFile = Join-Path $cryptoDirectory 'BouncyCastle.Cryptography.dll'
    [IO.File]::WriteAllBytes($cryptoFile, $reader.ReadBytes([int]$crypto[0].size))
} finally { $reader.Dispose(); $stream.Dispose() }
& (Join-Path $PSScriptRoot 'Verify-Crypto.ps1') -Assembly $cryptoFile
$bytes = (Get-Item -LiteralPath $exe).Length
$baseline = 64426697L
$report = [pscustomobject]@{ executableBytes=$bytes; executableSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash; prior091Bytes=$baseline; savedBytes=$baseline-$bytes; reductionPercent=[math]::Round(100*(1-$bytes/$baseline),2); runtimeBundled=$false; applicationDllsBesideExe=$false; headerVersion="$major.$minor"; entries=$entries; actualBundledCryptoChecksPassed=$true }
$report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $root '.build/release-size.json') -Encoding utf8
Write-Output ('Actual EXE: {0:N2} MiB, {1:N2}% smaller than 0.9.1; shipped Ed25519 checks passed.' -f ($bytes/1MB),$report.reductionPercent)
