# Bouwt de patcher als losse executables in dist/ (Windows x64 en Linux x64), elk met RedCatLauncher.exe ernaast.
# Gebruik: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\RedCatPatcher\RedCatPatcher.csproj'
$dist = Join-Path $root 'dist'
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue

foreach ($rid in 'win-x64', 'linux-x64') {
    dotnet publish $project -c Release -r $rid -o (Join-Path $dist $rid)
    if ($LASTEXITCODE -ne 0) { throw "publish $rid mislukt" }
}
# De launcher is altijd een Windows-programma; op Linux draait hij onder Proton naast RedCat.exe
$launcher = Join-Path $dist 'launcher'
dotnet publish (Join-Path $root 'src\RedCatLauncher\RedCatLauncher.csproj') -c Release -r win-x64 -o $launcher
if ($LASTEXITCODE -ne 0) { throw 'publish launcher mislukt' }
foreach ($rid in 'win-x64', 'linux-x64') { Copy-Item (Join-Path $launcher 'RedCatLauncher.exe') (Join-Path $dist $rid) }
Remove-Item $launcher -Recurse -Force
Get-ChildItem $dist -Recurse -File | Select-Object @{ n = 'Bestand'; e = { $_.FullName.Substring($dist.Length + 1) } }, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
