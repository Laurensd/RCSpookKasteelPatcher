# Bouwt beide launcher-proeven naar spikes\launcher\dist en pakt ze in één zip voor de test onder Proton.
# Gebruik: powershell -ExecutionPolicy Bypass -File spikes\launcher\build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue

foreach ($name in 'WinForms', 'Avalonia') {
    dotnet publish (Join-Path $root $name) -c Release -o (Join-Path $dist $name)
    if ($LASTEXITCODE -ne 0) { throw "publish $name mislukt" }
}
$exes = Get-ChildItem $dist -Recurse -Filter *.exe
Compress-Archive -Path ($exes.FullName + (Join-Path $root 'README.md')) -DestinationPath (Join-Path $dist 'RedCatLauncher-proef.zip')
$exes | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
