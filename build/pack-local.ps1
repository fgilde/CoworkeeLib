$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$suffix = 'dev.' + (Get-Date -Format 'yyyyMMddHHmmss')
dotnet pack (Join-Path $root 'Coworkee.slnx') -c Release -o (Join-Path $root 'artifacts/nuget') --version-suffix $suffix
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Packed Coworkee 0.1.0-$suffix"
