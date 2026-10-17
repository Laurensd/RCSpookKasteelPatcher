# Bouwt de patcher als losse executables in dist/ (Windows x64 en Linux x64).
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
Get-ChildItem $dist -Recurse -File | Select-Object @{ n = 'Bestand'; e = { $_.FullName.Substring($dist.Length + 1) } }, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
