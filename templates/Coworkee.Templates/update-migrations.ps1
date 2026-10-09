# Regenerates the initial migration of the template (with and without samples) against the current Coworkee sources.
# Run it whenever a Coworkee module changes its model; needs the dotnet-ef tool.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$content = Join-Path $PSScriptRoot 'content/CoworkeeApp'
$work = Join-Path ([IO.Path]::GetTempPath()) ('coworkee-migrations-' + [Guid]::NewGuid().ToString('N'))
$feed = Join-Path $work 'feed'
$version = '0.0.0-migrations'

# the version never changes, so restore would take earlier packs from the global cache
$cache = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
Get-ChildItem $cache -Directory -Filter 'coworkee.*' -ErrorAction SilentlyContinue |
    ForEach-Object { Join-Path $_.FullName $version } | Where-Object { Test-Path $_ } | Remove-Item -Recurse -Force

dotnet pack (Join-Path $root 'Coworkee.slnx') -c Release -o $feed -p:Version=$version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$target = Join-Path $content 'src/MyApp.Infrastructure/Migrations'
Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
dotnet new install $content --debug:custom-hive (Join-Path $work 'hive') | Out-Null

$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$variants = @{}
foreach ($samples in 'true', 'false') {
    $app = Join-Path $work "app-$samples"
    dotnet new coworkee -n MyApp -o $app --samples $samples --tests false --debug:custom-hive (Join-Path $work 'hive') | Out-Null
    (Get-Content "$app/Directory.Packages.props" -Raw).Replace('COWORKEE_VERSION', $version) | Set-Content "$app/Directory.Packages.props"
    dotnet nuget add source $feed --name local --configfile "$app/nuget.config" | Out-Null
    dotnet restore "$app/MyApp.slnx"
    dotnet ef migrations add Initial --project "$app/src/MyApp.Infrastructure" --startup-project "$app/src/MyApp.Infrastructure"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $variants[$samples] = Join-Path $app 'src/MyApp.Infrastructure/Migrations'
}

New-Item -ItemType Directory $target | Out-Null
function Read-Migration($folder, $suffix) {
    $file = Get-ChildItem $folder -Filter "*$suffix" | Select-Object -First 1
    ((Get-Content $file.FullName -Raw) -replace '\d{14}_Initial', "${stamp}_Initial").TrimEnd() + "`n"
}
foreach ($suffix in '_Initial.cs', '_Initial.Designer.cs', 'MyAppDbContextModelSnapshot.cs') {
    $name = if ($suffix -like '_*') { "$stamp$suffix" } else { $suffix }
    "#if (samples)`n" + (Read-Migration $variants['true'] $suffix) + "#else`n" + (Read-Migration $variants['false'] $suffix) + "#endif`n" |
        Set-Content (Join-Path $target $name) -NoNewline
}
Remove-Item $work -Recurse -Force
Write-Host "Template migration regenerated: $target"
