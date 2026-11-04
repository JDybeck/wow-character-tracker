# Makes the enchant maps in companion/WoWCharacterTracker/Assets: enchant id -> the enchanting spell
# that puts it on, from the game data on wago.tools (SpellEffect effect 53 = enchant item, its misc
# value is the enchant id). The app uses them to show Wowhead's tooltip for an enchant (the game only
# gives its id and, on Classic clients, its stats). Each game has its own (Flavor.EnchantSpells):
# Forever renumbered some enchants, Retail kept Classic Era's ids. Run both again when enchants change:
#   powershell -File tools\enchant-spells.ps1                  (Classic Era -> enchant-spells.json)
#   powershell -File tools\enchant-spells.ps1 -Product wowf    (Forever -> enchant-spells-forever.json)
param([string]$Product = "wow_classic_era", [string]$Build = "")

$ErrorActionPreference = "Stop"
$headers = @{ "User-Agent" = "WoWCharacterTracker" }
$files = @{ "wow_classic_era" = "enchant-spells.json"; "wowf" = "enchant-spells-forever.json" }
if (-not $files.ContainsKey($Product)) { throw "Unknown product $Product, use one of: $($files.Keys -join ', ')" }
if (-not $Build) {
    $builds = Invoke-RestMethod "https://wago.tools/api/builds" -Headers $headers
    $Build = $builds.$Product[0].version
}
Write-Host "$Product build $Build"

function Table($name) {
    $csv = Invoke-WebRequest "https://wago.tools/db2/$name/csv?build=$Build" -Headers $headers -UseBasicParsing
    $csv.Content | ConvertFrom-Csv
}

$names = @{}
foreach ($row in Table "SpellName") { $names[[int]$row.ID] = $row.Name_lang }

# Per enchant, the spell named "Enchant <slot> - ..." (the enchanter's recipe) if there is one, else the lowest id.
$map = [ordered]@{}
Table "SpellEffect" |
    Where-Object { $_.Effect -eq "53" -and [int]$_.EffectMiscValue_0 -gt 0 } |
    Group-Object { [int]$_.EffectMiscValue_0 } |
    Sort-Object { [int]$_.Name } |
    ForEach-Object {
        $spells = $_.Group | ForEach-Object { [int]$_.SpellID } | Sort-Object -Unique
        $recipe = $spells | Where-Object { "$($names[$_])" -like "Enchant *" } | Select-Object -First 1
        $map["$($_.Name)"] = if ($recipe) { $recipe } else { $spells[0] }
    }

$out = Join-Path $PSScriptRoot "..\companion\WoWCharacterTracker\Assets\$($files[$Product])"
[IO.File]::WriteAllText([IO.Path]::GetFullPath($out), ($map | ConvertTo-Json -Compress))
Write-Host "$($map.Count) enchants written to $out"
