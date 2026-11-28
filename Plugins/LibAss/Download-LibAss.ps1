$ErrorActionPreference = 'Stop'
$release = 'https://github.com/AmusementClub/assrender/releases/download/libass-0.17.5/libass-0.17.5-windows-x64-2e66df8-20260717T155846Z.zip'
$checksum = '2C942CD2610530EECFE3A528E238775D6EDA3EFD0E6C52F5326DA7D23FFDE363'
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ('flyleaf-libass-' + [Guid]::NewGuid().ToString('N'))
$destination = Join-Path $PSScriptRoot 'native/win-x64'

New-Item -Path $stage -ItemType Directory | Out-Null
$archive = Join-Path $stage 'libass.zip'
$files = Join-Path $stage 'files'
Invoke-WebRequest -Uri $release -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $checksum) {
    throw 'The libass download does not match the pinned SHA-256 checksum.'
}
Expand-Archive -LiteralPath $archive -DestinationPath $files
New-Item -Path $destination -ItemType Directory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $files 'bin/ass.dll'), (Join-Path $files 'VERSIONS.txt') -Destination $destination -Force
Copy-Item -LiteralPath (Join-Path $files 'LICENSES') -Destination $destination -Recurse -Force
Write-Output "libass staged in $destination"
