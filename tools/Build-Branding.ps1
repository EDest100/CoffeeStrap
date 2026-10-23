# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
# Reproducible vector-derived assets. No external image tools are required.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path $PSScriptRoot -Parent
$assetRoot = Join-Path $projectRoot 'branding'
[IO.Directory]::CreateDirectory($assetRoot) | Out-Null

function New-CoffeeMark([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.ScaleTransform($size / 512.0, $size / 512.0)
    $g.Clear([Drawing.Color]::Transparent)
    $g.TranslateTransform(248, 244)
    $g.RotateTransform(15)
    $ring = [Drawing.Drawing2D.GraphicsPath]::new([Drawing.Drawing2D.FillMode]::Alternate)
    $ring.AddRectangle([Drawing.RectangleF]::new(-163,-163,326,326))
    $ring.AddRectangle([Drawing.RectangleF]::new(-53,-53,106,106))
    $gradient = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Point]::new(-160,-163), [Drawing.Point]::new(160,163), [Drawing.ColorTranslator]::FromHtml('#E5C5A3'), [Drawing.ColorTranslator]::FromHtml('#825234'))
    $g.FillPath($gradient, $ring)
    $g.ResetTransform()
    $g.ScaleTransform($size / 512.0, $size / 512.0)
    $badge = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#2C211B'))
    $cream = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#FFF3E3'))
    $edge = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#D7B491'), 5)
    $ink = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#FFF3E3'), 9)
    $ink.StartCap = $ink.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $g.FillEllipse($badge, 326,326,148,148)
    $g.DrawEllipse($edge, 326,326,148,148)
    $g.DrawArc($ink, 410,377,29,31,-95,235)
    $cup = [Drawing.Drawing2D.GraphicsPath]::new()
    $cup.AddLine(365,377,418,377)
    $cup.AddLine(418,377,415,409)
    $cup.AddBezier(415,409,413,432,370,432,368,409)
    $cup.CloseFigure()
    $g.FillPath($cream,$cup)
    $g.DrawLine($ink,360,437,426,437)
    $g.DrawBezier($ink,385,363,376,354,395,351,387,342)
    $g.DrawBezier($ink,405,363,397,354,415,351,407,342)
    $g.Dispose(); $ring.Dispose(); $gradient.Dispose(); $badge.Dispose(); $cream.Dispose(); $edge.Dispose(); $ink.Dispose(); $cup.Dispose()
    return $bitmap
}

$sizes = @(16,24,32,48,64,128,256)
$imageBytes = @()
foreach ($size in $sizes) {
    $bitmap = New-CoffeeMark $size
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $imageBytes += ,$stream.ToArray()
    $bitmap.Save((Join-Path $assetRoot "CoffeeStrap-$size.png"),[Drawing.Imaging.ImageFormat]::Png)
    $stream.Dispose(); $bitmap.Dispose()
}
$large = New-CoffeeMark 512
$large.Save((Join-Path $projectRoot 'Froststrap\CoffeeStrap.png'),[Drawing.Imaging.ImageFormat]::Png)
$large.Save((Join-Path $assetRoot 'CoffeeStrap.png'),[Drawing.Imaging.ImageFormat]::Png)
$large.Dispose()
$icoStream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($icoStream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -eq 256) {0} else {$sizes[$i]}
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$imageBytes[$i].Length); $writer.Write([uint32]$offset)
    $offset += $imageBytes[$i].Length
}
foreach ($bytes in $imageBytes) { $writer.Write([byte[]]$bytes) }
$writer.Flush()
foreach ($path in @('Froststrap\CoffeeStrap.ico','Froststrap\Resources\IconCoffeeStrap.ico','branding\CoffeeStrap.ico')) {
    [IO.File]::WriteAllBytes((Join-Path $projectRoot $path),$icoStream.ToArray())
}
$writer.Dispose(); $icoStream.Dispose()
Write-Output 'CoffeeStrap PNG and multi-resolution ICO assets generated.'
