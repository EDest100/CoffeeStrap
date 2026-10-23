# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([string]$InputExecutable = '.build/publish/CoffeeStrap.exe', [string]$PayloadDirectory,
      [string]$EntryAssembly = 'CoffeeStrap.dll', [string]$OutputExecutable = '.build/compact-host/CoffeeStrap.exe',
      [string]$Version = '0.10.1', [ValidateSet(4,5)][int]$Algorithm = 5,
      [ValidateSet(0,1048576,16777216,67108864)][uint32]$BlockSize = 67108864,
      [string]$WorkDirectory = '.build/compact-host/build')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$work = [IO.Path]::GetFullPath($WorkDirectory,$root)
if (!$work.StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Portable staging must remain inside this workspace.' }
New-Item -ItemType Directory -Path $work -Force | Out-Null
if (!$PayloadDirectory) {
    $source = [IO.Path]::GetFullPath($InputExecutable, $root)
    $payloadRoot = Join-Path $work 'payload'
    New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
    $sdk = (& dotnet --version).Trim()
    $sdkRoot = Split-Path (Get-Command dotnet).Source -Parent
    [Reflection.Assembly]::LoadFrom((Join-Path $sdkRoot "sdk/$sdk/Microsoft.NET.HostModel.dll")) | Out-Null
    [long]$header = 0
    if (![Microsoft.NET.HostModel.Bundle.Bundler]::IsBundle($source,[ref]$header)) { throw 'Input must be the SDK-verified framework-dependent bundle.' }
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($source))
    try {
        $reader.BaseStream.Position = $header
        $major = $reader.ReadUInt32(); $minor = $reader.ReadUInt32(); $count = $reader.ReadInt32(); $bundleId = $reader.ReadString()
        if ($major -ne 6 -or $count -lt 1 -or $count -gt 1024) { throw 'Unsupported input bundle.' }
        $reader.BaseStream.Position += 40
        $entries = for ($i=0;$i -lt $count;$i++) {
            $offset=$reader.ReadInt64(); $size=$reader.ReadInt64(); $compressed=$reader.ReadInt64(); $type=$reader.ReadByte(); $name=$reader.ReadString()
            if ($offset -lt 0 -or $size -lt 0 -or $size -gt 64MB -or $compressed -ne 0 -or $offset+$size -gt $header) { throw 'Invalid input bundle file.' }
            [pscustomobject]@{name=$name;size=$size;offset=$offset}
        }
        $expected = @()
        foreach ($entry in $entries) {
            $file = [IO.Path]::GetFullPath((Join-Path $payloadRoot $entry.name))
            if (!$file.StartsWith($payloadRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Input entry escaped the payload root.' }
            if ([IO.Path]::GetExtension($file) -notin @('.dll','.json')) { throw 'Unexpected runtime payload type.' }
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file)) | Out-Null
            $reader.BaseStream.Position=$entry.offset
            [IO.File]::WriteAllBytes($file,$reader.ReadBytes([int]$entry.size))
            $expected += $file
        }
        $extra = @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Where-Object { $_.FullName -notin $expected })
        if ($extra.Count -ne 0) { throw 'The fixed payload staging folder has stale files. Use a fresh staging root before packaging.' }
    } finally { $reader.Dispose() }
}
else { $payloadRoot = [IO.Path]::GetFullPath($PayloadDirectory,$root) }
Add-Type -Path (Join-Path $PSScriptRoot 'Portable/PayloadBuilder.cs')
$blob = Join-Path $work 'payload.bin'
$timer = [Diagnostics.Stopwatch]::StartNew()
$lzmsBlockSize = if ($Algorithm -eq 5) { $BlockSize } else { 0 }
[CoffeePayloadBuilder]::Build($payloadRoot,$EntryAssembly,$blob,$Algorithm,$lzmsBlockSize)
$timer.Stop()
$output = [IO.Path]::GetFullPath($OutputExecutable,$root)
if (!$output.StartsWith($root + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Portable output must remain inside this workspace.' }
if (Get-CimInstance Win32_Process -Filter "name='CoffeeStrap.exe'" | Where-Object ExecutablePath -eq $output) { throw 'The target launcher is running; leave it open and use staging.' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$sdkRoot = Split-Path (Get-Command dotnet).Source -Parent
$native = Get-ChildItem (Join-Path $sdkRoot 'packs/Microsoft.NETCore.App.Host.win-x64') -Directory | Where-Object { ([version]$_.Name).Major -eq 10 } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$native) { throw 'The .NET 10 x64 native host pack is required.' }
$native = Join-Path $native.FullName 'runtimes/win-x64/native'
$vs = & 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe' -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'The installed Visual Studio C++ build tools are required.' }
$resource = Join-Path $work 'Host.rc'
$parsedVersion = [version]$Version
$versionParts = @($parsedVersion.Major,$parsedVersion.Minor,[Math]::Max(0,$parsedVersion.Build),[Math]::Max(0,$parsedVersion.Revision)) -join ','
$icon = (Join-Path $root 'Froststrap/CoffeeStrap.ico').Replace('\','/')
$manifest = (Join-Path $root 'Froststrap/app.manifest').Replace('\','/')
$payload = $blob.Replace('\','/')
@"
#pragma code_page(65001)
#include <windows.h>
1 ICON "$icon"
1 RT_MANIFEST "$manifest"
301 RCDATA "$payload"
1 VERSIONINFO
FILEVERSION $versionParts
PRODUCTVERSION $versionParts
FILEFLAGSMASK 0x3fL
FILEOS VOS_NT_WINDOWS32
FILETYPE VFT_APP
BEGIN
 BLOCK "StringFileInfo"
 BEGIN
  BLOCK "040904b0"
  BEGIN
   VALUE "CompanyName", "CoffeeStrap contributors\0"
   VALUE "FileDescription", "CoffeeStrap\0"
   VALUE "FileVersion", "$Version\0"
   VALUE "ProductName", "CoffeeStrap\0"
   VALUE "ProductVersion", "$Version\0"
   VALUE "OriginalFilename", "CoffeeStrap.exe\0"
  END
 END
 BLOCK "VarFileInfo"
 BEGIN
  VALUE "Translation", 0x0409, 1200
 END
END
"@ | Set-Content -LiteralPath $resource -Encoding utf8
$res = Join-Path $work 'Host.res'
$src = Join-Path $PSScriptRoot 'Portable/Host.cpp'
$obj = Join-Path $work 'Host.obj'
$command = '"{0}\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul && rc /nologo /fo "{1}" "{2}" && cl /nologo /std:c++20 /EHsc /W4 /WX /O1 /MT /GL /D_UNICODE /DUNICODE /I"{3}" "{4}" "{1}" /Fo"{5}" /Fe"{6}" /link /SUBSYSTEM:WINDOWS /LTCG /OPT:REF /OPT:ICF "{3}\libnethost.lib" cabinet.lib bcrypt.lib shell32.lib ole32.lib advapi32.lib user32.lib' -f $vs,$res,$resource,$native,$src,$obj,$output
& cmd /d /s /c $command
if ($LASTEXITCODE -ne 0) { throw 'Native portable-host compilation failed.' }
$manifest = & (Join-Path $PSScriptRoot 'Read-PortablePayload.ps1') -Executable $output
$report = [pscustomobject]@{ format=$manifest.format;algorithm=$Algorithm;blockSize=$lzmsBlockSize;entry=$EntryAssembly;payloadBytes=(Get-Item $blob).Length;executableBytes=(Get-Item $output).Length;uncompressedFileBytes=$manifest.expandedBytes;fileCount=$manifest.files.Count;compressionSeconds=$timer.Elapsed.TotalSeconds;executableSha256=(Get-FileHash $output -Algorithm SHA256).Hash;runtimeBundled=$false;nativeApi='Windows Compression API and .NET nethost/hostfxr';requires='.NET 10 x64' }
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'package.json') -Encoding utf8
$report | ConvertTo-Json
