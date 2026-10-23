# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([string]$Executable = '.build/compact-host/CoffeeStrap.exe')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$work = Join-Path $root '.build/compact-host/verification'
New-Item -ItemType Directory -Path $work -Force | Out-Null
$exe = [IO.Path]::GetFullPath($Executable,$root)
$payload = & (Join-Path $PSScriptRoot 'Read-PortablePayload.ps1') -Executable $exe
$checks = [Collections.Generic.List[object]]::new()
function Check([bool]$Value,[string]$Name) {
    $checks.Add([pscustomobject]@{name=$Name;passed=$Value})
    $checks | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $work 'checks.json') -Encoding utf8
    if (!$Value) { throw "Portable check failed: $Name" }
    Write-Output "PASS: $Name"
}
$native = Get-ChildItem (Join-Path (Split-Path (Get-Command dotnet).Source -Parent) 'packs/Microsoft.NETCore.App.Host.win-x64') -Directory | Where-Object { ([version]$_.Name).Major -eq 10 } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$native) { throw 'The .NET 10 x64 native host pack is required.' }
$native = Join-Path $native.FullName 'runtimes/win-x64/native'
$vs = & 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe' -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$nativeTests = Join-Path $root '.build/compact-host/HostTests.exe'
$command = '"{0}/Common7/Tools/VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul && cl /nologo /std:c++20 /EHsc /W4 /WX /O1 /MT /DUNICODE /D_UNICODE /I"{1}" "{2}/Portable/HostTests.cpp" /Fo"{3}/HostTests.obj" /Fe"{4}" /link /SUBSYSTEM:CONSOLE /OPT:REF "{1}/libnethost.lib" cabinet.lib bcrypt.lib shell32.lib ole32.lib advapi32.lib user32.lib' -f $vs,$native,$PSScriptRoot,$work,$nativeTests
& cmd /d /s /c $command > (Join-Path $work 'native-build.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Native payload test compilation failed.' }
$blob = Join-Path $work 'payload.bin'; [IO.File]::WriteAllBytes($blob,[CoffeePayloadInspector]::Read($exe))
& $nativeTests $blob > (Join-Path $work 'native-tests.log') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Native payload validation failed. See native-tests.log.' }
function Start-Probe([string]$Cache,[string[]]$Arguments = @('--version')) {
    $info = [Diagnostics.ProcessStartInfo]::new($exe)
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.WorkingDirectory=$root;$info.Environment['COFFEESTRAP_RUNTIME_CACHE']=$Cache
    $timer=[Diagnostics.Stopwatch]::StartNew();$process=[Diagnostics.Process]::Start($info)
    return [pscustomobject]@{process=$process;timer=$timer;stdout=$process.StandardOutput.ReadToEndAsync();stderr=$process.StandardError.ReadToEndAsync()}
}
function Complete-Probe($Probe) {
    $process=$Probe.process; $timer=$Probe.timer
    if(!$process.WaitForExit(30000)){throw 'Owned portable CLI did not exit within thirty seconds.'}
    $timer.Stop();$result=[pscustomobject]@{exit=$process.ExitCode;stdout=$Probe.stdout.GetAwaiter().GetResult();stderr=$Probe.stderr.GetAwaiter().GetResult();milliseconds=$timer.Elapsed.TotalMilliseconds};$process.Dispose();return $result
}
function Invoke-Probe([string]$Cache,[string[]]$Arguments = @('--version')) { Complete-Probe (Start-Probe $Cache $Arguments) }
$cache = Join-Path $work ('runtime-'+[guid]::NewGuid().ToString('N'))
$first=Invoke-Probe $cache
$first | ConvertTo-Json | Set-Content (Join-Path $work 'first-launch.json')
Check ($first.exit -eq 0 -and $first.stdout -match '0\.10\.') 'cold standalone host starts the real managed CLI'
$folder=Join-Path $cache $payload.identity
Check (@($payload.files|Where-Object {(Get-FileHash -LiteralPath (Join-Path $folder $_.name) -Algorithm SHA256).Hash -ne $_.sha256}).Count -eq 0) 'every extracted file matches the embedded file hash'
Check (@($payload.files|Where-Object name -Match '^(coreclr|hostfxr|hostpolicy|nethost)\.dll$').Count -eq 0) '.NET runtime and a dynamic nethost dependency are not bundled'
$warm=Invoke-Probe $cache @('--help'); Check ($warm.exit -eq 0 -and $warm.stdout -match 'CoffeeStrap') 'warm help launch reuses the verified cache'
$config=Join-Path $folder 'CoffeeStrap.runtimeconfig.json';$original=[IO.File]::ReadAllBytes($config)
[IO.File]::WriteAllText($config,'damaged fixture')
$repaired=Invoke-Probe $cache
Check ($repaired.exit -eq 0 -and [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($config))) -eq [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($original))) 'damaged cached settings are repaired atomically before hosting'
[IO.File]::Delete($config);$missing=Invoke-Probe $cache
Check ($missing.exit -eq 0 -and (Test-Path -LiteralPath $config)) 'a missing cached file is regenerated'
$locked=[IO.File]::Open($config,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
try{$failed=Invoke-Probe $cache;Check ($failed.exit -ne 0 -and $failed.stderr -match 'Close other CoffeeStrap') 'sharing failures report an error without starting the runtime'}finally{$locked.Dispose()}
Check ((Invoke-Probe $cache).exit -eq 0) 'cache mutex and file ownership recover after a failed launch'
$foreign=Join-Path $folder 'HOSTFXR.DLL';[IO.File]::WriteAllText($foreign,'untrusted fixture')
try{$blocked=Invoke-Probe $cache;Check ($blocked.exit -ne 0 -and $blocked.stderr -match 'Unexpected application library') 'an unlisted runtime DLL is rejected case-insensitively'}finally{[IO.File]::Delete($foreign)}
Check ((Invoke-Probe $cache).exit -eq 0) 'removing the unlisted fixture allows a clean launch'
$destination=Join-Path $work 'junction-destination';New-Item -ItemType Directory -Path $destination -Force|Out-Null
$sentinel=Join-Path $destination 'keep.txt';[IO.File]::WriteAllText($sentinel,'keep')
$junction=Join-Path $work ('junction-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Junction -Path $junction -Value $destination | Out-Null
try{$rejected=Invoke-Probe $junction;Check ($rejected.exit -ne 0 -and $rejected.stderr -match 'link' -and [IO.File]::ReadAllText($sentinel) -eq 'keep') 'a linked cache root is rejected without modifying its destination'}finally{Remove-Item -LiteralPath $junction -Force}
$unicode=Join-Path $work ('café 日本語 '+[guid]::NewGuid().ToString('N'));$international=Invoke-Probe $unicode
Check ($international.exit -eq 0) 'Unicode and spaced cache paths work'
$concurrentCache=Join-Path $work ('concurrent-'+[guid]::NewGuid().ToString('N'))
$one=Start-Probe $concurrentCache; $two=Start-Probe $concurrentCache
$oneResult=Complete-Probe $one; $twoResult=Complete-Probe $two
Check ($oneResult.exit -eq 0 -and $twoResult.exit -eq 0) 'concurrent cold launches safely share the cache preparation mutex'
$concurrentFolder=Join-Path $concurrentCache $payload.identity
Check (@($payload.files|Where-Object {(Get-FileHash -LiteralPath (Join-Path $concurrentFolder $_.name) -Algorithm SHA256).Hash -ne $_.sha256}).Count -eq 0) 'concurrent extraction produces the exact complete payload'
Check ((Get-Item $exe).Length -lt 20000000) 'the complete executable is below 20 decimal MB'
Write-Output "$($checks.Count) portable integration checks passed."
