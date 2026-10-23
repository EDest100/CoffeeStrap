# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
if (!('CoffeePayloadInspector' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'Portable/PayloadInspector.cs') }
$data = [CoffeePayloadInspector]::Read([IO.Path]::GetFullPath($Executable))
$reader = [IO.BinaryReader]::new([IO.MemoryStream]::new($data))
try {
    $magic = [Text.Encoding]::ASCII.GetString($reader.ReadBytes(8))
    if ($magic -ne 'CSCOFF01' -or $reader.ReadUInt32() -ne 1) { throw 'Unknown portable payload.' }
    $algorithm = $reader.ReadUInt32(); $count = $reader.ReadUInt32(); $manifestSize = $reader.ReadUInt32()
    $expanded = $reader.ReadUInt64(); $compressed = $reader.ReadUInt64()
    $manifestHash = $reader.ReadBytes(32); $dataHash = $reader.ReadBytes(32)
    if ($algorithm -notin @(4,5) -or $count -lt 1 -or $count -gt 1024 -or $manifestSize -gt 1MB -or $expanded -gt 128MB -or $compressed -gt 128MB) { throw 'Invalid portable limits.' }
    $entry = [Text.Encoding]::ASCII.GetString($reader.ReadBytes($reader.ReadUInt16()))
    $manifest = $reader.ReadBytes($manifestSize)
    if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifest)) -ne [Convert]::ToHexString($manifestHash)) { throw 'Portable manifest digest mismatch.' }
    $metadata = [IO.BinaryReader]::new([IO.MemoryStream]::new($manifest))
    try {
        $offset = 0L
        $files = for ($index=0;$index -lt $count;$index++) {
            $nameSize = $metadata.ReadUInt16(); $size = $metadata.ReadUInt64(); $hash = [Convert]::ToHexString($metadata.ReadBytes(32)); $name = [Text.Encoding]::ASCII.GetString($metadata.ReadBytes($nameSize))
            if ($nameSize -lt 1 -or $nameSize -gt 512 -or $size -gt 64MB -or $name -match '(^/|\\|:|(^|/)\.\.?(/|$))') { throw 'Invalid portable file.' }
            [pscustomobject]@{ name=$name;size=$size;sha256=$hash;offset=$offset }
            $offset += $size
        }
        if ($offset -ne $expanded -or $metadata.BaseStream.Position -ne $metadata.BaseStream.Length -or $files.name -notcontains $entry) { throw 'Incomplete portable manifest.' }
    } finally { $metadata.Dispose() }
    $packedOffset = $reader.BaseStream.Position
    if ($packedOffset + $compressed -ne $data.Length) { throw 'Portable data length mismatch.' }
    [pscustomobject]@{format='CoffeeStrap portable host v1';algorithm=$algorithm;entry=$entry;expandedBytes=$expanded;compressedBytes=$compressed;identity=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data)).ToLowerInvariant();files=$files;packedOffset=$packedOffset;dataSha256=[Convert]::ToHexString($dataHash)}
} finally { $reader.Dispose() }
