# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
# Resolve FluentAvalonia's semantic aliases into the app palette, including control states.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$aliasFiles = Get-ChildItem (Join-Path $projectRoot 'libs\FluentAvalonia\src\FluentAvalonia\Styling') -Recurse -Filter '*.axaml'
$aliases = foreach ($file in $aliasFiles) {
    $xml = [xml](Get-Content -LiteralPath $file.FullName -Raw)
    foreach ($entry in $xml.SelectNodes('//*[local-name()="StaticResource"]')) {
        $key = $entry.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')
        if ($key -and $entry.ResourceKey) { [PSCustomObject]@{Key=$key; Source=[string]$entry.ResourceKey} }
    }
}
foreach ($mode in @('Dark','Light')) {
    $dark = $mode -eq 'Dark'
    $page = if($dark){'#211C18'}else{'#FAF6F0'}
    $chrome = if($dark){'#171412'}else{'#F0E7DB'}
    $card = if($dark){'#2C2520'}else{'#FFFCF8'}
    $hover = if($dark){'#352C25'}else{'#F0E5D8'}
    $pressed = if($dark){'#40332A'}else{'#E8D9C9'}
    $border = if($dark){'#493B30'}else{'#DBCBBB'}
    $text = if($dark){'#F6EDE3'}else{'#30231B'}
    $secondary = if($dark){'#CDBBAA'}else{'#685445'}
    $tertiary = if($dark){'#BAA48F'}else{'#786150'}
    $accent = if($dark){'#D7AD84'}else{'#754A2D'}
    $accentHover = if($dark){'#E6C09A'}else{'#643C24'}
    $accentPressed = if($dark){'#C59A72'}else{'#59321D'}
    $onAccent = if($dark){'#211811'}else{'#FFF8EF'}
    $colors = [ordered]@{
        TextFillColorPrimary=$text; TextFillColorSecondary=$secondary; TextFillColorTertiary=$tertiary
        TextFillColorDisabled= $(if($dark){'#89786A'}else{'#A18E7B'})
        TextFillColorInverse=$onAccent
        TextOnAccentFillColorPrimary=$onAccent; TextOnAccentFillColorSecondary=$onAccent
        TextOnAccentFillColorDisabled=$secondary; TextOnAccentFillColorSelectedText=$onAccent
        ControlFillColorDefault=$card; ControlFillColorSecondary=$hover; ControlFillColorTertiary=$pressed
        ControlFillColorInputActive=$page; ControlFillColorDisabled=$chrome
        ControlFillColorTransparent='#00000000'; ControlSolidFillColorDefault=$card
        ControlAltFillColorSecondary=$chrome; ControlAltFillColorTertiary=$hover; ControlAltFillColorQuarternary=$pressed
        ControlStrongFillColorDefault=$tertiary
        SubtleFillColorTransparent='#00000000'; SubtleFillColorSecondary=$hover; SubtleFillColorTertiary=$pressed
        ControlStrokeColorDefault=$border; ControlStrokeColorSecondary=$border
        ControlStrongStrokeColorDefault=$tertiary; ControlStrokeColorOnAccentDefault=$accent
        CardStrokeColorDefault=$border; CardStrokeColorDefaultSolid=$border
        SurfaceStrokeColorDefault=$border; SurfaceStrokeColorFlyout=$border; DividerStrokeColorDefault=$border
        CardBackgroundFillColorDefault=$card; CardBackgroundFillColorSecondary=$chrome
        LayerFillColorDefault=$page; LayerFillColorAlt=$card; SolidBackgroundFillColorBase=$page
        SolidBackgroundFillColorSecondary=$chrome; SolidBackgroundFillColorTertiary=$card
        SolidBackgroundFillColorQuarternary=$hover
        AccentFillColorDefault=$accent; AccentFillColorSecondary=$accentHover; AccentFillColorTertiary=$accentPressed
        AccentFillColorDisabled=$border; AccentTextFillColorPrimary=$accent; AccentTextFillColorSecondary=$accentHover
        AccentTextFillColorTertiary=$accentPressed; FocusStrokeColorOuter=$accent; FocusStrokeColorInner=$page
    }
    $brushes = [ordered]@{
        ApplicationBackgroundColor=$page; NormalDarkAndLightBackground=$page; CoffeeChromeBrush=$chrome
        CoffeeGroupBrush=$(if($dark){'#251F1B'}else{'#F2E9DF'})
        CoffeeNavigationHighlightBrush=$(if($dark){'#574334'}else{'#E2CBB5'})
        CoffeePageBrush=$page; NewTextEditorBackground=$page; NewTextEditorForeground=$text
        NewTextEditorLink=$accent; CompletionListBackgroundBrush=$card; NotificationBackgroundColor=$card
        ControlElevationBorderBrush=$border; AccentControlElevationBorderBrush=$accent
        ToolTipBackgroundBrush=$card; ToolTipForegroundBrush=$text
        ComboBoxDropDownBackground=$card
    }
    foreach($key in $colors.Keys){$brushes[$key+'Brush']=$colors[$key]}
    # Semantic aliases are StaticResource in FluentAvalonia. Resolve each chain so a
    # newly selected palette also updates buttons, popups, navigation and editors.
    for($pass=0;$pass -lt 8;$pass++) {
        foreach($alias in $aliases) {
            if($brushes.Contains($alias.Source)){$brushes[$alias.Key]=$brushes[$alias.Source]}
        }
    }
    # A cream iOS-style thumb needs contrast against both track states.
    $switchOn = if($dark){'#946A44'}else{'#754A2D'}
    $switchHover = if($dark){'#A6784E'}else{'#643C24'}
    $switchPressed = if($dark){'#835933'}else{'#59321D'}
    foreach($key in @('ToggleSwitchFillOn','ToggleSwitchStrokeOn')){$brushes[$key]=$switchOn}
    foreach($key in @('ToggleSwitchFillOnPointerOver','ToggleSwitchStrokeOnPointerOver')){$brushes[$key]=$switchHover}
    foreach($key in @('ToggleSwitchFillOnPressed','ToggleSwitchStrokeOnPressed')){$brushes[$key]=$switchPressed}
    $brushes['ToggleSwitchFillOff'] = if($dark){'#1B1714'}else{'#998575'}
    $brushes['ToggleSwitchFillOffPointerOver'] = if($dark){'#2B241F'}else{'#998575'}
    $brushes['ToggleSwitchFillOffPressed'] = if($dark){'#352C25'}else{'#8D7968'}
    $out = [Collections.Generic.List[string]]::new()
    $out.Add('<!-- SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors')
    $out.Add('     SPDX-License-Identifier: MPL-2.0 -->')
    $out.Add('<!-- Generated by tools/Build-Theme.ps1. Palette and FluentAvalonia semantic control states. -->')
    $out.Add('<ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
    foreach($key in $colors.Keys){$out.Add("    <Color x:Key=`"$key`">$($colors[$key])</Color>")}
    foreach($key in $brushes.Keys){$out.Add("    <SolidColorBrush x:Key=`"$key`" Color=`"$($brushes[$key])`" />")}
    $out.Add('</ResourceDictionary>')
    [IO.File]::WriteAllLines((Join-Path $projectRoot "Froststrap\UI\AppThemes\ResourceDictionarys\$mode.axaml"),$out)
}
Write-Output 'CoffeeStrap light and dark resource dictionaries generated.'
