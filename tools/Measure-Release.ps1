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
if (!('CoffeePayloadInspector' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'Portable/PayloadInspector.cs') }
$sdkBundle = ![CoffeePayloadInspector]::HasPayload($exe)
if ($sdkBundle -and ![Microsoft.NET.HostModel.Bundle.Bundler]::IsBundle($exe,[ref]$header)) { throw 'Expected an SDK bundle or CoffeeStrap portable executable.' }
$entries = @()
$cryptoDirectory = Join-Path $root '.build/crypto-release-probe'
New-Item -ItemType Directory -Path $cryptoDirectory -Force | Out-Null
$cryptoFile = Join-Path $cryptoDirectory 'BouncyCastle.Cryptography.dll'
if ($sdkBundle) {
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($exe))
    try {
        $reader.BaseStream.Position = $header
        $major = $reader.ReadUInt32(); $minor = $reader.ReadUInt32(); $count = $reader.ReadInt32(); $bundleId = $reader.ReadString()
        if ($major -ne 6 -or $count -lt 1 -or $count -gt 1024) { throw 'Unsupported single-file manifest.' }
        $reader.BaseStream.Position += 40
        for ($index=0; $index -lt $count; $index++) {
            $offset = $reader.ReadInt64(); $size = $reader.ReadInt64(); $compressed = $reader.ReadInt64(); $type = $reader.ReadByte(); $name = $reader.ReadString()
            if ($offset -lt 0 -or $size -lt 0 -or $compressed -ne 0 -or $offset + $size -gt $header) { throw 'Invalid framework-dependent bundle entry.' }
            $entries += [pscustomobject]@{ name=$name; size=$size; offset=$offset; type=$type }
        }
        $crypto = @($entries | Where-Object name -eq 'BouncyCastle.Cryptography.dll')
        if ($crypto.Count -ne 1 -or $crypto[0].size -ge 100KB) { throw 'Expected the compact signature-verification dependency.' }
        $reader.BaseStream.Position = $crypto[0].offset
        [IO.File]::WriteAllBytes($cryptoFile, $reader.ReadBytes([int]$crypto[0].size))
    } finally { $reader.Dispose() }
    $format = "SDK bundle $major.$minor"; $expanded = ($entries | Measure-Object size -Sum).Sum; $allocated = $null
}
else {
    $payload = & (Join-Path $PSScriptRoot 'Read-PortablePayload.ps1') -Executable $exe
    $entries = $payload.files; $format = $payload.format; $expanded = $payload.expandedBytes
    $cacheRoot = Join-Path $root '.build/compact-host/measure-runtime'
    $info = [Diagnostics.ProcessStartInfo]::new($exe,'--version')
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $info.Environment['COFFEESTRAP_RUNTIME_CACHE']=$cacheRoot
    $process=[Diagnostics.Process]::Start($info)
    if (!$process.WaitForExit(30000)) { throw 'Portable payload verification timed out.' }
    $stdout=$process.StandardOutput.ReadToEnd(); $stderr=$process.StandardError.ReadToEnd(); $exitCode=$process.ExitCode; $process.Dispose()
    if ($exitCode -ne 0) { throw "Portable payload could not start: $stderr" }
    $cache = Join-Path $cacheRoot $payload.identity; $allocated = 0L
    foreach ($entry in $entries) {
        $file = Join-Path $cache $entry.name
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Portable cache mismatch: $($entry.name)" }
        $allocated += [CoffeePayloadInspector]::AllocatedBytes($file)
    }
    $crypto = @($entries | Where-Object name -eq 'BouncyCastle.Cryptography.dll')
    if ($crypto.Count -ne 1 -or $crypto[0].size -ge 100KB) { throw 'Expected the compact signature-verification dependency.' }
    Copy-Item -LiteralPath (Join-Path $cache $crypto[0].name) -Destination $cryptoFile -Force
}
foreach($pattern in @('^Puppeteer','^WebDriverBiDi','^HotAvalonia','^Avalonia\.(Desktop|X11|Native|Wayland|FreeDesktop)','^NWayland','^(coreclr|hostfxr|hostpolicy|nethost)\.dll$')) {
    if ($entries.name -match $pattern) { throw "Unexpected Windows release dependency: $pattern" }
}
& (Join-Path $PSScriptRoot 'Verify-Crypto.ps1') -Assembly $cryptoFile
$bytes = (Get-Item -LiteralPath $exe).Length
$baseline = 64426697L
$prior0100 = 48097958L
$report = [pscustomobject]@{ executableBytes=$bytes; executableSha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash; prior0100Bytes=$prior0100; savedFrom0100Bytes=$prior0100-$bytes; reductionFrom0100Percent=[math]::Round(100*(1-$bytes/$prior0100),2); prior091Bytes=$baseline; savedBytes=$baseline-$bytes; reductionPercent=[math]::Round(100*(1-$bytes/$baseline),2); runtimeBundled=$false; applicationDllsBesideExe=$false; format=$format; expandedApplicationBytes=$expanded; cacheAllocatedBytesOnTestVolume=$allocated; entries=$entries; actualBundledCryptoChecksPassed=$true }
$report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $root '.build/release-size.json') -Encoding utf8
Write-Output ('Actual EXE: {0:N2} MiB, {1:N2}% smaller than 0.9.1; shipped Ed25519 checks passed.' -f ($bytes/1MB),$report.reductionPercent)
