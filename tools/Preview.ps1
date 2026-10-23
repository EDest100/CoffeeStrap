# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$oldPath = $env:PATH
$oldRustFlags = $env:RUSTFLAGS
try {
    $env:PATH = (Join-Path $env:USERPROFILE '.cargo\bin') + ';' + $env:PATH
    $env:RUSTFLAGS = '-C debuginfo=0'
    Push-Location -LiteralPath $root
    try {
        $arguments = @('build','tools\Preview\CoffeeStrap.Preview.csproj','-c','Release',
            '-p:UsedAvaloniaProducts=','-p:AppVersion=0.10.0','-v','minimal')
        if ($NoRestore) { $arguments += '--no-restore' }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Preview build failed.' }
        & dotnet 'tools\Preview\bin\Release\net10.0\CoffeeStrap.Preview.dll' (Join-Path $root '.build\verification')
        if ($LASTEXITCODE -ne 0) { throw 'Preview rendering failed.' }
        $fixtures = [IO.Path]::GetFullPath((Join-Path $root '.build\verification\fixture-data'))
        if (!$fixtures.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped the workspace.' }
        if (Test-Path -LiteralPath $fixtures) { Remove-Item -LiteralPath $fixtures -Recurse -Force }
        $previewPython = Join-Path $root '.tooling\python313\python.exe'
        if (Test-Path -LiteralPath $previewPython) {
            & $previewPython (Join-Path $PSScriptRoot 'Encode-Preview-Animation.py')
            if ($LASTEXITCODE -ne 0) { throw 'Animation preview encoding failed.' }
            & $previewPython (Join-Path $PSScriptRoot 'Verify-Simplification-Scope.py')
            if ($LASTEXITCODE -ne 0) { throw 'Settings continuity check failed.' }
            & $previewPython (Join-Path $PSScriptRoot 'Verify-Branding.py')
            if ($LASTEXITCODE -ne 0) { throw 'Branding verification failed.' }
            & $previewPython (Join-Path $PSScriptRoot 'Verify-Contrast.py')
            if ($LASTEXITCODE -ne 0) { throw 'Contrast verification failed.' }
        }
        & (Join-Path $PSScriptRoot 'Write-Preview-Gallery.ps1')
    } finally { Pop-Location }
} finally {
    $env:PATH = $oldPath
    $env:RUSTFLAGS = $oldRustFlags
}
