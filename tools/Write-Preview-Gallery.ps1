# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root '.build\verification'
$coverage = Get-Content -LiteralPath (Join-Path $output 'coverage.json') -Raw | ConvertFrom-Json
$cards = foreach ($item in $coverage) {
    $file = [Net.WebUtility]::HtmlEncode($item.fileName)
    $label = [Net.WebUtility]::HtmlEncode(($item.fileName -replace '\.png$','' -replace '-',' '))
    '<figure><a href="'+$file+'"><img loading="lazy" src="'+$file+'" alt="'+$label+'"></a><figcaption>'+$label+'</figcaption></figure>'
}
$html = @"
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>CoffeeStrap - application captures</title>
<style>body{background:#211c18;color:#f6ede3;font:15px/1.5 "Segoe UI",sans-serif;margin:32px}h1{font-size:28px;margin:0 0 8px}p{color:#cdbbaa;max-width:900px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,500px),1fr));gap:24px}figure{margin:0;padding:10px;border:1px solid #59483a;border-radius:12px;background:#2b241f}img{width:100%;height:340px;object-fit:contain;background:#1b1714;border-radius:6px}figcaption{padding:8px 4px 0;text-transform:capitalize}a{color:inherit}</style>
<h1>CoffeeStrap 0.8.10</h1><p>Production Avalonia views rendered with isolated fixture data. The animations below use a controlled clock and actual intermediate rendered frames. They verify clipping, reflow and reversal behavior, not native Windows frame rate. Roblox installation, account login, native window effects and live services were not exercised. Click an image for its full resolution.</p><main>
"@
foreach ($animation in @('group-collapse','sidebar-reflow','hover-reversal','page-transition','artwork-hover','launcher-glow')) {
    if (Test-Path -LiteralPath (Join-Path $output ($animation+'.apng'))) {
        $label = $animation -replace '-',' '
        $html += '<figure><a href="'+$animation+'.apng"><img src="'+$animation+'.apng" alt="'+$label+'"></a><figcaption>'+$label+' - controlled-time rendered sequence</figcaption></figure>'
    }
}
$html += ($cards -join "`n") + '</main></html>'
[IO.File]::WriteAllText((Join-Path $output 'index.html'),$html)
Write-Output ('Gallery created for '+$coverage.Count+' rendered views.')
