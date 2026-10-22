<#
.SYNOPSIS
Validates and exports all GeoHopper component icons from their SVG originals.
.DESCRIPTION
Creates transparent PNGs and a grouped review sheet using Windows System.Drawing.
Only graphics inside this folder are written; no plugin binaries are published.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'rasterizer.ps1')
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$ink = $manifest.ink
$categories = @('Analysis','Connection','Geometry','Loads','Material','Model','Results')
if ($manifest.icons.Count -ne 15) { throw 'Expected 15 component icons.' }
if (@($manifest.icons.id | Sort-Object -Unique).Count -ne 15) { throw 'Duplicate icon ID.' }
foreach ($size in @(24,48,96)) {
    New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot "png/$size") -Force | Out-Null
}
foreach ($item in $manifest.icons) {
    if ($categories -notcontains $item.category) { throw "Unknown category: $($item.category)" }
    $svg = Get-Content -LiteralPath (Join-Path $PSScriptRoot $item.svg) -Raw
    $xml = [xml]$svg
    if ($xml.DocumentElement.GetAttribute('viewBox') -ne '0 0 24 24') { throw "Invalid viewBox: $($item.id)" }
    $palette = $manifest.palette.($item.category)
    $allowed = @($ink, '#FFFFFF', $palette.accent, $palette.tint)
    foreach ($colour in [regex]::Matches($svg,'#[0-9A-Fa-f]{6}').Value) {
        if ($allowed -notcontains $colour) { throw "Unexpected category colour $colour in $($item.id)" }
    }
    if (!$svg.Contains($palette.accent)) { throw "Missing category accent: $($item.id)" }
    foreach ($size in @(24,48,96)) {
        $destination = Join-Path $PSScriptRoot $item.("png$size")
        Export-Icon $svg $size $destination
        $check = [Drawing.Bitmap]::FromFile($destination)
        try {
            if ($check.Width -ne $size -or $check.Height -ne $size) { throw "Invalid PNG dimensions: $destination" }
            if ($check.GetPixel(0,0).A -ne 0) { throw "PNG background is not transparent: $destination" }
            $visible = $false
            for ($x=0; $x -lt $size; $x++) {
                for ($y=0; $y -lt $size; $y++) { if ($check.GetPixel($x,$y).A -gt 0) { $visible = $true; break } }
                if ($visible) { break }
            }
            if (!$visible) { throw "Empty PNG: $destination" }
        } finally { $check.Dispose() }
    }
}

# Draw category rows with an enlarged icon and unscaled 24-pixel samples.
$sheet = [Drawing.Bitmap]::new(1240,1500)
$canvas = [Drawing.Graphics]::FromImage($sheet)
$canvas.Clear([Drawing.ColorTranslator]::FromHtml('#F4F6F5'))
$canvas.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$brushes = @{}
$fonts = @{}

# Cache fonts and brushes for the sheet, disposing them when export is complete.
function Draw-Label([string]$Text, [single]$X, [single]$Y, [single]$Size, [string]$Colour = '#263D46', [bool]$Bold = $false) {
    if (!$brushes.ContainsKey($Colour)) { $brushes[$Colour] = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($Colour)) }
    $key = "$Size-$Bold"
    if (!$fonts.ContainsKey($key)) {
        $style = if ($Bold) { [Drawing.FontStyle]::Bold } else { [Drawing.FontStyle]::Regular }
        $fonts[$key] = [Drawing.Font]::new('Segoe UI', $Size, $style, [Drawing.GraphicsUnit]::Pixel)
    }
    $canvas.DrawString($Text, $fonts[$key], $brushes[$Colour], $X, $Y)
}

# Place each bitmap without resampling so the 24-pixel samples remain exact.
function Draw-Sample([string]$Id, [int]$Size, [int]$X, [int]$Y) {
    $bitmap = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot "png/$Size/$Id.png"))
    try { $canvas.DrawImageUnscaled($bitmap, $X, $Y) } finally { $bitmap.Dispose() }
}
Draw-Label 'GEOHOPPER / COMPONENT GRAPHICS' 40 25 13 '#168579' $true
Draw-Label 'One category. One colour.' 38 50 34 '#263D46' $true
Draw-Label '15 component icons / 7 categories / SVG originals + transparent PNGs' 40 101 16 '#64767B'
$white = [Drawing.SolidBrush]::new([Drawing.Color]::White)
$grey = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#D9DEDC'))
for ($row=0; $row -lt $categories.Count; $row++) {
    $category = $categories[$row]
    $accent = $manifest.palette.$category.accent
    $y = 155 + $row*184
    Draw-Label $category 40 ($y+14) 22 $accent $true
    Draw-Label $accent 40 ($y+48) 13 '#64767B'
    $items = @($manifest.icons | Where-Object category -eq $category)
    Draw-Label ("{0} component{1}" -f $items.Count, $(if ($items.Count -eq 1) { '' } else { 's' })) 40 ($y+73) 13 '#64767B'
    for ($col=0; $col -lt $items.Count; $col++) {
        $item = $items[$col]
        $x = 220+$col*244
        $canvas.FillRectangle($white,$x,$y,232,170)
        Draw-Sample $item.id 96 ($x+12) ($y+10)
        Draw-Sample $item.id 24 ($x+133) ($y+35)
        $canvas.FillRectangle($grey,($x+176),($y+27),40,40)
        Draw-Sample $item.id 24 ($x+184) ($y+35)
        Draw-Label '24 px' ($x+130) ($y+75) 11 '#64767B'
        Draw-Label 'Canvas' ($x+178) ($y+75) 11 '#64767B'
        $label = $item.title -replace '^Material - ', ''
        Draw-Label $label ($x+14) ($y+120) 16 '#263D46' $true
        Draw-Label $item.id ($x+14) ($y+146) 11 '#64767B'
    }
}
Draw-Label '96 px enlarged / 24 px actual-size samples on white and grey / all exports have transparent backgrounds' 40 1460 14 '#64767B'
$sheet.Save((Join-Path $PSScriptRoot 'overview.png'),[Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
$sheet.Dispose()
$white.Dispose()
$grey.Dispose()
foreach ($value in $brushes.Values) { $value.Dispose() }
foreach ($value in $fonts.Values) { $value.Dispose() }
Write-Output 'Validated 15 SVGs and 45 PNGs: category palette, dimensions, non-empty artwork and transparent background.'
Write-Output "Overview: $PSScriptRoot\overview.png"
