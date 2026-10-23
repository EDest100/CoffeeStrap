# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
param([Parameter(Mandatory)][string]$Path)
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
$imports = @()
if ($rva -ne 0) {
    $position = Convert-Rva $rva
    while ([BitConverter]::ToUInt32($bytes,$position + 12) -ne 0) {
        $name = Convert-Rva ([BitConverter]::ToUInt32($bytes,$position + 12)); $end = $name
        while ($bytes[$end] -ne 0) { $end++ }
        $imports += [Text.Encoding]::ASCII.GetString($bytes,$name,$end - $name)
        $position += 20
    }
}
[pscustomobject]@{ managed = [BitConverter]::ToUInt32($bytes,$directory + 14*8) -ne 0; imports = $imports }
