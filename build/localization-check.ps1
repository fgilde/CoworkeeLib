param([Parameter(Mandatory)][string]$Lang)

# Checks one language against the German files of the four text sources: keys (case-sensitive), {0} placeholders, mail expressions and tags.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sources = [ordered]@{
    'library UI'   = 'src/Coworkee.Localization/Localization'
    'account pages' = 'src/Coworkee.AuthServer/Texts'
    'template app' = 'templates/Coworkee.Templates/content/CoworkeeApp/src/MyApp.Contracts/Localization'
}
$problems = 0

function Read-Texts([string]$path) {
    [System.Text.Json.JsonSerializer]::Deserialize[System.Collections.Generic.Dictionary[string, string]]([IO.File]::ReadAllText($path))
}

function Tokens([string]$pattern, [string]$text) {
    ([regex]::Matches($text, $pattern, 'Singleline') | ForEach-Object { ($_.Value -replace '\s+', ' ') -replace '\blang="[^"]*"', 'lang=""' } | Sort-Object { $_ } -CaseSensitive) -join "`n"
}

function Report([string]$source, [string]$message) {
    Write-Host "[$source] $message"
    $script:problems++
}

foreach ($name in $sources.Keys) {
    $folder = Join-Path $root $sources[$name]
    $file = Join-Path $folder "$Lang.json"
    if (-not (Test-Path $file)) { Report $name "$file is missing"; continue }

    $german = Read-Texts (Join-Path $folder 'de.json')
    $texts = Read-Texts $file
    foreach ($key in $german.Keys | Where-Object { -not $texts.ContainsKey($_) }) { Report $name "missing key: $key" }
    foreach ($key in $texts.Keys | Where-Object { -not $german.ContainsKey($_) }) { Report $name "extra key: $key" }
    foreach ($key in $texts.Keys | Where-Object { $german.ContainsKey($_) }) {
        # keys that are no English sentence (MudBlazor ones) take their placeholders from the German text
        $source = if ($key.Contains(' ')) { $key } else { $german[$key] }
        if ((Tokens '\{\d[^{}]*\}' $source) -ne (Tokens '\{\d[^{}]*\}' $texts[$key])) { Report $name "placeholders differ: $key" }
    }
}

$mails = Join-Path $root 'src/Coworkee.Mailing/Templates'
foreach ($de in Get-ChildItem $mails -Filter '*.de.*.scriban') {
    $file = Join-Path $mails $de.Name.Replace('.de.', ".$Lang.")
    if (-not (Test-Path $file)) { Report 'mails' "$(Split-Path -Leaf $file) is missing"; continue }
    $markup = '\{\{.*?\}\}|</?[A-Za-z][^>]*>'
    if ((Tokens $markup ([IO.File]::ReadAllText($de.FullName))) -ne (Tokens $markup ([IO.File]::ReadAllText($file)))) {
        Report 'mails' "$(Split-Path -Leaf $file): expressions or tags differ from $($de.Name)"
    }
}

if ($problems -eq 0) { Write-Host "$Lang is complete." } else { Write-Host "$problems problem(s) in $Lang."; exit 1 }
