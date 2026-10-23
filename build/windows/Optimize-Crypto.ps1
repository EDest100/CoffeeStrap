# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([Parameter(Mandatory)][string]$Assembly, [Parameter(Mandatory)][string]$Linker,
      [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$inputFile = [IO.Path]::GetFullPath($Assembly)
$outputRoot = [IO.Path]::GetFullPath($Output)
$root = [IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
if (!$outputRoot.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Crypto build output escaped the workspace.' }
if (!(Test-Path -LiteralPath $Linker)) { throw 'Restore the declared Microsoft.NET.ILLink.Tasks build dependency before publishing. No tool was downloaded.' }
$runtime = [Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
if ([Environment]::Version.Major -ne 10) { throw 'Run the crypto publishing step with PowerShell on .NET 10.' }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
& dotnet $Linker -x (Join-Path $PSScriptRoot 'CryptoRoots.xml') -d (Split-Path $inputFile -Parent) -d $runtime --action skip --trim-mode skip --action link BouncyCastle.Cryptography --warnaserror -out $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Ed25519 dependency linking failed.' }
$result = Join-Path $outputRoot 'BouncyCastle.Cryptography.dll'
if (!(Test-Path -LiteralPath $result) -or (Get-Item -LiteralPath $result).Length -ge (Get-Item -LiteralPath $inputFile).Length) { throw 'Expected a smaller Ed25519 verification assembly.' }
& (Join-Path $PSScriptRoot 'Verify-Crypto.ps1') -Assembly $result
