<#
.SYNOPSIS
Compiles and tests GeoHopper in artifacts/staging, outside plugin search paths.
.DESCRIPTION
Default: validate a candidate without changing dist. Use -Publish after closing Rhino.
Python is only a development test dependency, never a plugin runtime dependency.
#>
[CmdletBinding()]
param(
    [switch]$Publish,
    [string]$RhinoPath = 'C:\Program Files\Rhino 7',
    [string]$PythonPath = 'C:\ProgramData\Seequent\PLAXIS Python Distribution V3\python\python.exe'
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$stage = Join-Path $projectRoot 'artifacts\staging'
$manifestPath = Join-Path $stage 'build-manifest.json'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
# Invalidate the previous candidate so a failed rebuild cannot be published.
if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath }
foreach ($required in @($compiler, $PythonPath, (Join-Path $RhinoPath 'System\RhinoCommon.dll'))) {
    if (!(Test-Path -LiteralPath $required)) { throw "Required build dependency not found: $required" }
}
& (Join-Path $projectRoot 'scripts\restore-dependencies.ps1')
$crypto = Join-Path $projectRoot 'lib\BouncyCastle.Cryptography.dll'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Sort-Object Name)
# Embed the approved 24-pixel assets by component class; no runtime graphics folder is needed.
$graphicsRoot = Join-Path $projectRoot 'graphics'
$iconManifest = Join-Path $graphicsRoot 'manifest.json'
$icons = @((Get-Content -LiteralPath $iconManifest -Raw | ConvertFrom-Json).icons)
$iconResources = @()
$assetPaths = @('graphics/manifest.json')
$iconClasses = @{}
Add-Type -AssemblyName System.Drawing
foreach ($icon in $icons) {
    if ($icon.componentClass -notmatch '^[A-Za-z][A-Za-z0-9]*Component$' -or $iconClasses.ContainsKey($icon.componentClass)) {
        throw 'Invalid or duplicate component class in icon manifest.'
    }
    if ($icon.png24 -notmatch '^png/24/[a-z0-9-]+\.png$') { throw 'Invalid icon path in manifest.' }
    $iconClasses[$icon.componentClass] = $true
    $iconPath = Join-Path $graphicsRoot $icon.png24
    $bitmap = [System.Drawing.Bitmap]::FromFile($iconPath)
    try { if ($bitmap.Width -ne 24 -or $bitmap.Height -ne 24) { throw "Icon must be 24 by 24 pixels: $iconPath" } }
    finally { $bitmap.Dispose() }
    $iconResources += '/resource:' + $iconPath + ',GeoHopper.Icons.' + $icon.componentClass + '.png'
    $assetPaths += 'graphics/' + $icon.png24
}
$argsForPlugin = @('/nologo', '/target:library', '/optimize+', '/warn:4', '/warnaserror+',
    '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll',
    '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    ('/reference:' + $crypto),
    ('/reference:' + (Join-Path $RhinoPath 'System\RhinoCommon.dll')),
    ('/reference:' + (Join-Path $RhinoPath 'Plug-ins\Grasshopper\Grasshopper.dll')),
    ('/reference:' + (Join-Path $RhinoPath 'Plug-ins\Grasshopper\GH_IO.dll')),
    ('/out:' + (Join-Path $stage 'GeoHopper.gha')),
    ('/doc:' + (Join-Path $stage 'GeoHopper.xml'))) + @($sources.FullName) + $iconResources
& $compiler @argsForPlugin
if ($LASTEXITCODE -ne 0) { throw 'Plugin compilation failed. dist was not changed.' }
$embedded = [Reflection.Assembly]::LoadFile((Join-Path $stage 'GeoHopper.gha')).GetManifestResourceNames()
foreach ($icon in $icons) {
    if ($embedded -notcontains ('GeoHopper.Icons.' + $icon.componentClass + '.png')) { throw 'A component icon was not embedded.' }
}
Write-Output "Verified $($icons.Count) embedded component icons."
$argsForProbe = @('/nologo', '/target:exe', '/optimize+', '/warn:4', '/warnaserror+',
    '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll',
    ('/reference:' + $crypto), ('/out:' + (Join-Path $stage 'ReadProbe.exe')),
    (Join-Path $projectRoot 'src\PlaxisClient.cs'), (Join-Path $projectRoot 'tests\ReadProbe.cs'))
& $compiler @argsForProbe
if ($LASTEXITCODE -ne 0) { throw 'Protocol probe compilation failed. dist was not changed.' }
Copy-Item -LiteralPath $crypto -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'lib\BouncyCastle.LICENSE.md') -Destination $stage -Force
$argsForModel = @('/nologo', '/target:exe', '/optimize+', '/warn:4', '/warnaserror+',
    '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll',
    ('/reference:' + $crypto), ('/out:' + (Join-Path $stage 'ModelProbe.exe'))) +
    @('PlaxisClient.cs', 'ModelData.cs', 'PlateData.cs', 'AnchorData.cs', 'InitialStateRules.cs', 'Geometry2D.cs', 'ModelBuilder.cs', 'AnalysisData.cs', 'AnalysisRunner.cs', 'ResultsData.cs', 'ResultsReader.cs' | ForEach-Object { Join-Path $projectRoot ('src\' + $_) }) +
    @((Join-Path $projectRoot 'tests\ModelProbe.cs'))
& $compiler @argsForModel
if ($LASTEXITCODE -ne 0) { throw 'Model probe compilation failed. dist was not changed.' }
& (Join-Path $stage 'ModelProbe.exe') | Tee-Object -FilePath (Join-Path $stage 'model-test-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Model core tests failed. dist was not changed.' }
$argsForAnalysis = @('/nologo', '/target:exe', '/optimize+', '/warn:4', '/warnaserror+',
    '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll',
    ('/reference:' + $crypto), ('/out:' + (Join-Path $stage 'AnalysisProbe.exe'))) +
    @('PlaxisClient.cs', 'ModelData.cs', 'PlateData.cs', 'AnchorData.cs', 'InitialStateRules.cs', 'Geometry2D.cs', 'ModelBuilder.cs', 'AnalysisData.cs', 'AnalysisRunner.cs', 'ResultsData.cs', 'ResultsReader.cs' | ForEach-Object { Join-Path $projectRoot ('src\' + $_) }) +
    @((Join-Path $projectRoot 'tests\AnalysisProbe.cs'))
& $compiler @argsForAnalysis
if ($LASTEXITCODE -ne 0) { throw 'Analysis probe compilation failed. dist was not changed.' }
& (Join-Path $stage 'AnalysisProbe.exe') | Tee-Object -FilePath (Join-Path $stage 'analysis-core-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Analysis core tests failed. dist was not changed.' }
$argsForResults = @('/nologo', '/target:exe', '/optimize+', '/warn:4', '/warnaserror+', '/main:ResultsProbe',
    '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll',
    ('/reference:' + $crypto), ('/out:' + (Join-Path $stage 'ResultsProbe.exe'))) +
    @('PlaxisClient.cs', 'ModelData.cs', 'PlateData.cs', 'AnchorData.cs', 'InitialStateRules.cs', 'Geometry2D.cs', 'ModelBuilder.cs', 'AnalysisData.cs', 'AnalysisRunner.cs', 'ResultsData.cs', 'ResultsReader.cs' | ForEach-Object { Join-Path $projectRoot ('src\' + $_) }) +
    @((Join-Path $projectRoot 'tests\AnalysisProbe.cs'), (Join-Path $projectRoot 'tests\ResultsProbe.cs'))
& $compiler @argsForResults
if ($LASTEXITCODE -ne 0) { throw 'Results probe compilation failed. dist was not changed.' }
& (Join-Path $stage 'ResultsProbe.exe') | Tee-Object -FilePath (Join-Path $stage 'results-core-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Results core tests failed. dist was not changed.' }
$previousProbePath = $env:GEOHOPPER_TEST_PROBE
$previousModelProbePath = $env:GEOHOPPER_MODEL_PROBE
$previousAnalysisProbePath = $env:GEOHOPPER_ANALYSIS_PROBE
try {
    $env:GEOHOPPER_TEST_PROBE = Join-Path $stage 'ReadProbe.exe'
    & $PythonPath (Join-Path $projectRoot 'tests\test_protocol.py') 2>&1 |
        Tee-Object -FilePath (Join-Path $stage 'test-results.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Protocol tests failed. dist was not changed.' }
    $env:GEOHOPPER_MODEL_PROBE = Join-Path $stage 'ModelProbe.exe'
    & $PythonPath (Join-Path $projectRoot 'tests\test_build.py') 2>&1 |
        Tee-Object -FilePath (Join-Path $stage 'build-test-results.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Model transfer tests failed. dist was not changed.' }
    $env:GEOHOPPER_ANALYSIS_PROBE = Join-Path $stage 'AnalysisProbe.exe'
    & $PythonPath (Join-Path $projectRoot 'tests\test_analysis.py') 2>&1 |
        Tee-Object -FilePath (Join-Path $stage 'analysis-transfer-results.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Analysis transfer tests failed. dist was not changed.' }
    $env:GEOHOPPER_ANALYSIS_PROBE = Join-Path $stage 'ResultsProbe.exe'
    & $PythonPath (Join-Path $projectRoot 'tests\test_results.py') 2>&1 |
        Tee-Object -FilePath (Join-Path $stage 'results-transfer-results.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Results transfer tests failed. dist was not changed.' }
} finally {
    $env:GEOHOPPER_TEST_PROBE = $previousProbePath
    $env:GEOHOPPER_MODEL_PROBE = $previousModelProbePath
    $env:GEOHOPPER_ANALYSIS_PROBE = $previousAnalysisProbePath
}
$names = @('GeoHopper.gha', 'GeoHopper.xml', 'BouncyCastle.Cryptography.dll', 'BouncyCastle.LICENSE.md')
$manifest = [ordered]@{
    builtUtc = [DateTime]::UtcNow.ToString('o')
    version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $stage 'GeoHopper.gha')).Version.ToString()
    testsPassed = $true
    files = @($names | ForEach-Object { @{ name = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $stage $_) -Algorithm SHA256).Hash } })
    sources = @($sources | ForEach-Object { @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
    assets = @($assetPaths | ForEach-Object { @{ name = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $projectRoot $_) -Algorithm SHA256).Hash } })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output "Build and tests passed. Candidate: $stage"
if ($Publish) { & (Join-Path $projectRoot 'publish.ps1') }
else { Write-Output 'Close Rhino, then run .\publish.ps1 to update the loaded plugin.' }
