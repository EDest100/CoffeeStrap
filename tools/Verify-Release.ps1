# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([string]$Executable = 'Release/CoffeeStrap.exe')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$source = [IO.Path]::GetFullPath($Executable, $root)
$sandbox = [IO.Path]::GetFullPath((Join-Path $root '.build/release-smoke'))
if (!$sandbox.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Smoke output escaped the workspace.' }
if (Test-Path -LiteralPath $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
New-Item -ItemType Directory -Path $sandbox -Force | Out-Null
$exe = Join-Path $sandbox 'CoffeeStrap.exe'
Copy-Item -LiteralPath $source -Destination $exe
$results = @()
foreach ($argument in @('--version','--help')) {
    $info = [Diagnostics.ProcessStartInfo]::new($exe,$argument)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true; $info.WorkingDirectory = $sandbox
    $info.Environment['DOTNET_DISABLE_GUI_ERRORS'] = '1'
    $info.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $sandbox 'bundle-cache'
    $info.Environment['DOTNET_HOST_TRACE'] = '1'
    $trace = Join-Path $sandbox ('host-' + $argument.Substring(2) + '.log')
    $info.Environment['DOTNET_HOST_TRACEFILE'] = $trace
    $process = [Diagnostics.Process]::Start($info)
    if (!$process.WaitForExit(30000)) { throw 'Release startup did not exit within 30 seconds.' }
    $stdout = $process.StandardOutput.ReadToEnd(); $stderr = $process.StandardError.ReadToEnd()
    $result = [pscustomobject]@{ argument = $argument; exit = $process.ExitCode; stdout = $stdout; stderr = $stderr }
    $process.Dispose()
    if ($result.exit -ne 0) { throw "Release $argument failed: $stderr" }
    $traceText = [IO.File]::ReadAllText($trace)
    if (!$traceText.Contains('is_framework_dependent=1') -or $traceText.Contains('Using internal fxr')) { throw 'The executable did not prove framework-dependent startup.' }
    $results += $result
}
function Get-Imports([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $pe = [BitConverter]::ToInt32($bytes,60); $optional = $pe + 24
    $sections = $optional + [BitConverter]::ToUInt16($bytes,$pe + 20)
    $count = [BitConverter]::ToUInt16($bytes,$pe + 6)
    function Convert-Rva([uint32]$Rva) {
        for ($i = 0; $i -lt $count; $i++) {
            $section = $sections + $i * 40
            $size = [math]::Max([BitConverter]::ToUInt32($bytes,$section + 8),[BitConverter]::ToUInt32($bytes,$section + 16))
            $address = [BitConverter]::ToUInt32($bytes,$section + 12)
            if ($Rva -ge $address -and $Rva -lt $address + $size) { return [int]([BitConverter]::ToUInt32($bytes,$section + 20) + $Rva - $address) }
        }
        throw 'Invalid PE import address.'
    }
    $directory = $optional + $(if ([BitConverter]::ToUInt16($bytes,$optional) -eq 0x20b) { 112 } else { 96 })
    $rva = [BitConverter]::ToUInt32($bytes,$directory + 8)
    if ($rva -eq 0) { return }
    $position = Convert-Rva $rva
    while ([BitConverter]::ToUInt32($bytes,$position + 12) -ne 0) {
        $name = Convert-Rva ([BitConverter]::ToUInt32($bytes,$position + 12)); $end = $name
        while ($bytes[$end] -ne 0) { $end++ }
        [Text.Encoding]::ASCII.GetString($bytes,$name,$end - $name)
        $position += 20
    }
}
$native = @(Get-ChildItem -LiteralPath (Join-Path $sandbox 'bundle-cache') -Recurse -File -Filter '*.dll')
if ($native.Name -notcontains 'notify.dll' -or $native.Name -notcontains 'libSkiaSharp.dll') { throw 'The bundle is missing expected native libraries.' }
$imports = @{}
foreach ($file in @((Get-Item -LiteralPath $exe)) + $native) {
    $names = @(Get-Imports $file.FullName); $imports[$file.Name] = $names
    if ($names | Where-Object { $_ -match '^(VCRUNTIME|MSVCP)' }) { throw "External VC runtime dependency in $($file.Name). Rebuild with static CRT." }
}
foreach ($file in $native) { $handle = [Runtime.InteropServices.NativeLibrary]::Load($file.FullName); [Runtime.InteropServices.NativeLibrary]::Free($handle) }
$report = [pscustomobject]@{ executableSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash; frameworkDependentStartup = $true; runtimeBundled = $false; requiredRuntime = '.NET 10 x64'; cli = $results; nativeImports = $imports; nativeLoadsPassed = $true; normalGuiAndRobloxTested = $false }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root '.build/release-checks.json') -Encoding utf8
Remove-Item -LiteralPath $sandbox -Recurse -Force
Write-Output 'Framework-dependent CLI startup, native imports and native library loads passed; .NET is not bundled.'
