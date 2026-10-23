# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([string]$Executable = 'Release/CoffeeStrap.exe')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$source = [IO.Path]::GetFullPath($Executable, $root)
if (!('CoffeePayloadInspector' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'Portable/PayloadInspector.cs') }
$portable = [CoffeePayloadInspector]::HasPayload($source)
$payload = if ($portable) { & (Join-Path $PSScriptRoot 'Read-PortablePayload.ps1') -Executable $source }
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
    $info.Environment['COFFEESTRAP_RUNTIME_CACHE'] = Join-Path $sandbox 'runtime-cache'
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
if ($portable) {
    $cache = Join-Path (Join-Path $sandbox 'runtime-cache') $payload.identity
    foreach ($file in $payload.files) {
        if ((Get-FileHash -LiteralPath (Join-Path $cache $file.name) -Algorithm SHA256).Hash -ne $file.sha256) { throw "Extracted payload mismatch: $($file.name)" }
    }
    $libraries = @(Get-ChildItem -LiteralPath $cache -Recurse -File -Filter '*.dll')
}
else { $libraries = @(Get-ChildItem -LiteralPath (Join-Path $sandbox 'bundle-cache') -Recurse -File -Filter '*.dll') }
$native = @($libraries | Where-Object { !(& (Join-Path $PSScriptRoot 'Portable/Inspect-PE.ps1') -Path $_.FullName).managed })
if ($native.Name -notcontains 'notify.dll' -or $native.Name -notcontains 'libSkiaSharp.dll') { throw 'The bundle is missing expected native libraries.' }
$imports = @{}
foreach ($file in @((Get-Item -LiteralPath $exe)) + $native) {
    $names = @((& (Join-Path $PSScriptRoot 'Portable/Inspect-PE.ps1') -Path $file.FullName).imports); $imports[$file.Name] = $names
    if ($names | Where-Object { $_ -match '^(VCRUNTIME|MSVCP)' }) { throw "External VC runtime dependency in $($file.Name). Rebuild with static CRT." }
}
foreach ($file in $native) { $handle = [Runtime.InteropServices.NativeLibrary]::Load($file.FullName); [Runtime.InteropServices.NativeLibrary]::Free($handle) }
$report = [pscustomobject]@{ executableSha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash; portableHost = $portable; extractedFilesVerified = $(if ($portable) {$payload.files.Count} else {0}); frameworkDependentStartup = $true; runtimeBundled = $false; requiredRuntime = '.NET 10 x64'; cli = $results; nativeImports = $imports; nativeLoadsPassed = $true; normalGuiAndRobloxTested = $false }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $root '.build/release-checks.json') -Encoding utf8
Remove-Item -LiteralPath $sandbox -Recurse -Force
Write-Output 'Framework-dependent CLI startup, native imports and native library loads passed; .NET is not bundled.'
