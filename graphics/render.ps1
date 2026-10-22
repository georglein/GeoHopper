<#
.SYNOPSIS
Validates and exports all GeoHopper component icons from their SVG originals.
.DESCRIPTION
Creates transparent PNGs using Windows System.Drawing.
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

Write-Output 'Validated 15 SVGs and 45 PNGs: category palette, dimensions, non-empty artwork and transparent background.'
