# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([string]$Executable = '.build/compact-host/test-host/CoffeeStrap.Preview.exe',
      [string]$ThemeArchive = '.build/theme-package/CoffeeStrap-Espresso.zip')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$exe = [IO.Path]::GetFullPath($Executable,$root)
$work = Join-Path $root '.build/compact-host/ui'
New-Item -ItemType Directory -Path $work -Force | Out-Null
function Run([string]$Name,[string[]]$Arguments) {
    $output = Join-Path $work $Name
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    $info.ArgumentList.Add($output)
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $info.WorkingDirectory=$root; $info.Environment['COFFEESTRAP_RUNTIME_CACHE']=Join-Path $root '.build/compact-host/preview-runtime'
    $process=[Diagnostics.Process]::Start($info)
    $stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(1000)) { if ($timer.Elapsed.TotalMinutes -gt 10) { throw "Owned $Name preview exceeded ten minutes. Inspect it before retrying." } }
    $text=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()
    $text | Set-Content (Join-Path $work "$Name.log") -Encoding utf8
    $exitCode=$process.ExitCode; $process.Dispose()
    if ($exitCode -ne 0) { throw "$Name failed ($exitCode). See $work/$Name.log." }
    Write-Host "PASS: compressed host $Name"
    return $output
}
$roundTrip=@('--portable-host-identity','café 日本語','quote "inside"','C:\a path\')
$identityOutput=Run 'identity' $roundTrip
$identity=Get-Content (Join-Path $identityOutput 'host-identity.json') -Raw | ConvertFrom-Json
if ($identity.processPath -ne $exe -or $identity.apartment -ne 'STA' -or $identity.args.Count -ne $roundTrip.Count+1) { throw 'Host identity, UI apartment or argument count changed.' }
for ($index=0;$index -lt $roundTrip.Count;$index++) { if ($identity.args[$index+1] -cne $roundTrip[$index]) { throw 'Host argument round-trip changed.' } }
if (!$identity.profileRoot.StartsWith($identityOutput+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Preview settings escaped their isolated fixture.' }
$payload=& (Join-Path $PSScriptRoot 'Read-PortablePayload.ps1') -Executable $exe
$expectedBase=[IO.Path]::GetFullPath((Join-Path (Join-Path $root '.build/compact-host/preview-runtime') $payload.identity))
if ($identity.appBase -ne ('\\?\'+$expectedBase+'\') -or $identity.appAssembly -ne ('\\?\'+(Join-Path $expectedBase 'CoffeeStrap.dll'))) { throw 'Managed code did not load from the verified embedded cache.' }
Run 'community-graphics-region-compact' @('--revision6-only') | Out-Null
Run 'resources' @('--resource-audit') | Out-Null
Run 'espresso' @('--coffee-theme-only',[IO.Path]::GetFullPath($ThemeArchive,$root)) | Out-Null
[pscustomobject]@{nativeHost=$exe;identityAndArgumentsPassed=$true;staUiThread=$true;isolatedSettings=$true;managedCache=$expectedBase;normalGuiAndRobloxTested=$false} | ConvertTo-Json | Set-Content (Join-Path $work 'host-checks.json') -Encoding utf8
