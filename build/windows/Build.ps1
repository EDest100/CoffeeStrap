# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([switch]$Publish, [switch]$NoRestore, [switch]$StageOnly, [string]$PublishDirectory = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$oldPath = $env:PATH
$oldRustFlags = $env:RUSTFLAGS
try {
    # Override upstream's nightly-only -Z option for this process, without changing toolchains.
    $cargoBin = Join-Path $env:USERPROFILE '.cargo\bin'
    if (Test-Path -LiteralPath $cargoBin) { $env:PATH = $cargoBin + ';' + $env:PATH }
    $env:RUSTFLAGS = '-C debuginfo=0'
    if ($Publish) { $env:RUSTFLAGS += ' -C target-feature=+crt-static' }
    Push-Location -LiteralPath $projectRoot
    try {
        $arguments = @('build','Froststrap\Froststrap.csproj','-c','Release','-p:AppVersion=0.11.1','-p:UsedAvaloniaProducts=','-v','minimal')
        if ($Publish) {
            $destination = [IO.Path]::GetFullPath($PublishDirectory, $projectRoot)
            if (!$destination.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish output must be inside this workspace.' }
            $exe = Join-Path $destination 'CoffeeStrap.exe'
            if (!$StageOnly -and (Get-Process -Name CoffeeStrap -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })) { throw 'Close CoffeeStrap before replacing this release. The running app was left alone. Use -StageOnly to verify a new build without replacing it.' }
            $stage = Join-Path $projectRoot '.build\publish'
            if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
            $arguments[0] = 'publish'
            $arguments += @('-r','win-x64','--self-contained','false','-p:EnableCompressionInSingleFile=false','-p:DebugType=None','-p:DebugSymbols=false','-p:CopyDebugSymbolFilesFromPackages=false','-p:CopyDocumentationFilesFromPackages=false','-o',$stage)
        }
        if ($NoRestore) { $arguments += '--no-restore' }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
        if ($Publish) {
            & (Join-Path $PSScriptRoot 'Verify-Release.ps1') -Executable (Join-Path $stage 'CoffeeStrap.exe')
            & (Join-Path $PSScriptRoot 'Measure-Release.ps1') -Executable (Join-Path $stage 'CoffeeStrap.exe')
            $compact = Join-Path $projectRoot '.build/compact-host/CoffeeStrap.exe'
            & (Join-Path $PSScriptRoot 'Build-Portable.ps1') -InputExecutable (Join-Path $stage 'CoffeeStrap.exe') -OutputExecutable $compact -Version '0.11.1'
            & (Join-Path $PSScriptRoot 'Verify-Release.ps1') -Executable $compact
            & (Join-Path $PSScriptRoot 'Measure-Release.ps1') -Executable $compact
            if ((Get-Item -LiteralPath $compact).Length -ge 20000000) { throw 'The release exceeded the 20 MB executable budget; inspect .build/release-size.json.' }
            Copy-Item -LiteralPath $compact -Destination (Join-Path $stage 'CoffeeStrap.exe') -Force
            if ($StageOnly) {
                & (Join-Path $PSScriptRoot 'Package.ps1') -PublishDirectory $stage
                Write-Output 'Verified build staged in .build/publish. The current Release executable was left alone.'
                return
            }
            if (Get-Process -Name CoffeeStrap -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { throw 'CoffeeStrap was opened during publishing. The verified build is staged in .build/publish; the running release was left alone.' }
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $stage 'CoffeeStrap.exe') -Destination $exe -Force
            Remove-Item -LiteralPath $stage -Recurse -Force
            & (Join-Path $PSScriptRoot 'Package.ps1') -PublishDirectory $destination
        }
    } finally { Pop-Location }
} finally {
    $env:PATH = $oldPath
    $env:RUSTFLAGS = $oldRustFlags
}
