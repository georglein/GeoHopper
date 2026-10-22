<#
.SYNOPSIS
Restores the pinned Bouncy Castle package and verifies its SHA-256 digest.
.DESCRIPTION
Uses the local cache when available. Does not install anything globally.
Retains the original third-party license beside the extracted assembly.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$libraryDir = Join-Path $projectRoot 'lib'
New-Item -ItemType Directory -Force -Path $libraryDir | Out-Null
$package = Join-Path $libraryDir 'bouncycastle.cryptography.2.6.2.nupkg'
$expectedHash = '623936FB1FD171579C706390390711282898CF2C759A5167852E5AB82208F8CB'
if (!(Test-Path -LiteralPath $package)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/bouncycastle.cryptography/2.6.2/bouncycastle.cryptography.2.6.2.nupkg' -OutFile $package
}
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Bouncy Castle package checksum mismatch. Dependency was not extracted.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    [IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry('lib/net461/BouncyCastle.Cryptography.dll'), (Join-Path $libraryDir 'BouncyCastle.Cryptography.dll'), $true)
    [IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry('LICENSE.md'), (Join-Path $libraryDir 'BouncyCastle.LICENSE.md'), $true)
} finally { $archive.Dispose() }
Write-Output 'Verified BouncyCastle.Cryptography 2.6.2.'
