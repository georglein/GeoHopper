<#
.SYNOPSIS
Promotes a tested GeoHopper candidate to dist and backs up the previous release.
.DESCRIPTION
Rejects modified or untested candidates and refuses deployment while Rhino runs.
Never closes applications. -ValidateOnly checks without publishing any files.
#>
[CmdletBinding()]
param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$stage = Join-Path $projectRoot 'artifacts\staging'
$dist = Join-Path $projectRoot 'dist'
$manifestPath = Join-Path $stage 'build-manifest.json'
if (!(Test-Path -LiteralPath $manifestPath)) { throw 'No tested candidate found. Run .\build.ps1 first.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.testsPassed -ne $true) { throw 'The candidate has not passed tests.' }
$allowed = @('GeoHopper.gha', 'GeoHopper.xml', 'BouncyCastle.Cryptography.dll', 'BouncyCastle.LICENSE.md')
if (@($manifest.files).Count -ne $allowed.Count) { throw 'Invalid release manifest.' }
foreach ($name in $allowed) {
    $entry = @($manifest.files | Where-Object { $_.name -eq $name })
    if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash -ne $entry[0].sha256) {
        throw "Candidate changed or is incomplete: $name. Rebuild before publishing."
    }
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs')
if ($sources.Count -ne @($manifest.sources).Count) { throw 'Source files changed. Rebuild before publishing.' }
foreach ($source in $sources) {
    $entry = @($manifest.sources | Where-Object { $_.name -eq $source.Name })
    if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash -ne $entry[0].sha256) {
        throw "Source changed: $($source.Name). Rebuild before publishing."
    }
}
if (@($manifest.assets).Count -lt 2) { throw 'Candidate has no icon asset hashes. Rebuild before publishing.' }
foreach ($asset in $manifest.assets) {
    if ($asset.name -notmatch '^graphics/(manifest\.json|png/24/[a-z0-9-]+\.png)$') { throw 'Invalid candidate asset path.' }
    if ((Get-FileHash -LiteralPath (Join-Path $projectRoot $asset.name) -Algorithm SHA256).Hash -ne $asset.sha256) {
        throw "Icon asset changed: $($asset.name). Rebuild before publishing."
    }
}
if ($ValidateOnly) { Write-Output "Candidate $($manifest.version) is valid. No files were published."; return }
if (Get-Process -Name Rhino -ErrorAction SilentlyContinue) {
    throw 'Rhino is running. Save and close Rhino, then run .\publish.ps1 again. dist was not changed.'
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$backup = Join-Path $projectRoot ('artifacts\backups\' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $backup | Out-Null
$deploymentNames = $allowed + @('build-manifest.json', 'PlaxisDirectTest.gha')
foreach ($name in $deploymentNames) {
    $oldFile = Join-Path $dist $name
    if (Test-Path -LiteralPath $oldFile) {
        Copy-Item -LiteralPath $oldFile -Destination (Join-Path $backup $name)
        if ((Get-FileHash -LiteralPath $oldFile).Hash -ne (Get-FileHash -LiteralPath (Join-Path $backup $name)).Hash) {
            throw "Could not verify backup: $name. dist was not changed."
        }
    }
}
try {
    foreach ($name in $allowed) { Copy-Item -LiteralPath (Join-Path $stage $name) -Destination (Join-Path $dist $name) -Force }
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $dist 'build-manifest.json') -Force
    # The old GHA shares our component GUID; keeping it would register duplicates.
    $legacyFile = Join-Path $dist 'PlaxisDirectTest.gha'
    if (Test-Path -LiteralPath $legacyFile) { Remove-Item -LiteralPath $legacyFile }
} catch {
    # Roll back individual deployment files to the verified pre-update snapshot.
    foreach ($name in $deploymentNames) {
        $saved = Join-Path $backup $name
        $target = Join-Path $dist $name
        if (Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination $target -Force }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
    }
    throw
}
Write-Output "Published GeoHopper $($manifest.version) to $dist"
Write-Output "Previous release saved outside plugin search paths: $backup"
Write-Output 'Restart Rhino and search for GeoHopper Connection.'
