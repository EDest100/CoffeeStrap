# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([Parameter(Mandatory)][string]$Assembly)
$ErrorActionPreference = 'Stop'
$context = [Runtime.Loader.AssemblyLoadContext]::new('CoffeeStrap-Crypto-Probe', $true)
try {
    $library = $context.LoadFromAssemblyPath([IO.Path]::GetFullPath($Assembly))
    $signerType = $library.GetType('Org.BouncyCastle.Crypto.Signers.Ed25519Signer', $true)
    $keyType = $library.GetType('Org.BouncyCastle.Crypto.Parameters.Ed25519PublicKeyParameters', $true)
    $keyConstructor = $keyType.GetConstructor([type[]]@([byte[]],[int]))
    $vectors = @(
        @('d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a','','e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b'),
        @('3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c','72','92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00'),
        @('fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025','af82','6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a')
    )
    $longVector = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'References/Ed25519-Rfc8032-1023.json') -Raw | ConvertFrom-Json
    $vectors += ,@($longVector.publicKey, $longVector.message, $longVector.signature)
    function Verify([byte[]]$Key, [byte[]]$Message, [byte[]]$Signature) {
        $keyObject = $keyConstructor.Invoke([object[]]@($Key,0))
        $signer = [Activator]::CreateInstance($signerType)
        $signer.Init($false,$keyObject)
        $signer.BlockUpdate($Message,0,$Message.Length)
        return $signer.VerifySignature($Signature)
    }
    $checks = 0
    for ($index = 0; $index -lt $vectors.Count; $index++) {
        $vector = $vectors[$index]
        $key = [Convert]::FromHexString($vector[0]); $message = [Convert]::FromHexString($vector[1]); $signature = [Convert]::FromHexString($vector[2])
        if (!(Verify $key $message $signature)) { throw "RFC 8032 valid signature $index was rejected." }; $checks++
        $signature[0] = $signature[0] -bxor 1
        if (Verify $key $message $signature) { throw 'Changed signature was accepted.' }; $checks++
        $signature = [Convert]::FromHexString($vector[2])
        $changed = [byte[]]::new($message.Length + 1); $message.CopyTo($changed,0)
        if (Verify $key $changed $signature) { throw 'Changed message was accepted.' }; $checks++
        if (Verify $key $message ([byte[]]::new(64))) { throw 'Zero signature was accepted.' }; $checks++
        if (Verify $key $message ([byte[]]::new(63))) { throw 'Truncated signature was accepted.' }; $checks++
        $signature = [Convert]::FromHexString($vector[2]); $signature[63] = 255
        if (Verify $key $message $signature) { throw 'Noncanonical scalar was accepted.' }; $checks++
        $wrongKey = [Convert]::FromHexString($vectors[($index + 1) % $vectors.Count][0])
        if (Verify $wrongKey $message ([Convert]::FromHexString($vector[2]))) { throw 'Wrong public key was accepted.' }; $checks++
    }
    Write-Output "Ed25519 published dependency: $checks RFC 8032 positive and rejection checks passed."
} finally { $context.Unload() }
