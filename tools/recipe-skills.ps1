# Makes addon/WoWCharacterTracker/RecipeSkills.lua: the skill each Forever recipe needs, by profession.
# The game doesn't say it for recipes already learned, so it comes from Wowhead's Forever profession
# lists: each recipe's "learnedat", kept only where Wowhead knows its orange level (colors[0] above 0);
# otherwise learnedat is Wowhead's guess (the yellow level) and the recipe is left out. The app reads
# the same file (Services/RecipeSkills.cs). Run it again when recipes change:
#   powershell -File tools\recipe-skills.ps1
$ErrorActionPreference = "Stop"
$headers = @{ "User-Agent" = "WoWCharacterTracker" }
$pages = [ordered]@{
    "Alchemy" = "professions/alchemy"; "Blacksmithing" = "professions/blacksmithing"; "Enchanting" = "professions/enchanting"
    "Engineering" = "professions/engineering"; "Leatherworking" = "professions/leatherworking"; "Tailoring" = "professions/tailoring"
    "Mining" = "professions/mining"; "Cooking" = "secondary-skills/cooking"; "First Aid" = "secondary-skills/first-aid"
    "Fishing" = "secondary-skills/fishing"
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('-- The skill each Forever recipe needs, by profession: [recipe (spell) id] = skill. The game doesn''t')
$lines.Add('-- say it for recipes already learned, so this comes from Wowhead''s Forever profession lists (the')
$lines.Add('-- "learnedat" of each recipe, kept only where Wowhead knows the orange level, not its guess). Made by')
$lines.Add('-- tools/recipe-skills.ps1, run it again when recipes change. The app reads this file too (RecipeSkills.cs).')
$lines.Add('local ADDON, ns = ...')
$lines.Add('')
$lines.Add('ns.RECIPE_SKILLS = {')
$total = 0
foreach ($name in ($pages.Keys | Sort-Object)) {
    $html = (Invoke-WebRequest "https://www.wowhead.com/forever/spells/$($pages[$name])" -Headers $headers -UseBasicParsing).Content
    $start = $html.IndexOf('var listviewspells = [')
    if ($start -lt 0) { throw "No recipe list on Wowhead's $name page" }
    $end = $html.IndexOf('</script>', $start)
    $skills = @{}
    foreach ($entry in ($html.Substring($start, $end - $start) -split '\{"cat":' | Select-Object -Skip 1)) {
        $id = [regex]::Match($entry, '"id":(\d+)'); $learned = [regex]::Match($entry, '"learnedat":(\d+)'); $orange = [regex]::Match($entry, '"colors":\[(\d+)')
        if ($id.Success -and $learned.Success -and $orange.Success -and [int]$orange.Groups[1].Value -gt 1) {
            $skills[[int]$id.Groups[1].Value] = [int]$learned.Groups[1].Value
        }
    }
    if ($skills.Count -eq 0) { continue }
    $lines.Add("  [""$name""] = {")
    $line = "   "
    foreach ($id in ($skills.Keys | Sort-Object)) {
        $part = " [$id]=$($skills[$id]),"
        if ($line.Length + $part.Length -gt 100) { $lines.Add($line); $line = "   " }
        $line += $part
    }
    $lines.Add($line)
    $lines.Add('  },')
    $total += $skills.Count
    Write-Host "$name $($skills.Count)"
}
$lines.Add('}')
$lines.Add('')
$lines.Add('-- The skill a recipe needs and its profession ("Blacksmithing", 125), or nil when it isn''t known.')
$lines.Add('-- Only on Classic clients: Retail reuses some of these spell ids with its own skill lines.')
$lines.Add('function ns.recipeSkill(id)')
$lines.Add('  if WOW_PROJECT_ID and WOW_PROJECT_MAINLINE and WOW_PROJECT_ID == WOW_PROJECT_MAINLINE then return nil end')
$lines.Add('  for profession, skills in pairs(ns.RECIPE_SKILLS) do')
$lines.Add('    local skill = skills[id]')
$lines.Add('    if skill then return skill, profession end')
$lines.Add('  end')
$lines.Add('  return nil')
$lines.Add('end')
$out = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\addon\WoWCharacterTracker\RecipeSkills.lua"))
[IO.File]::WriteAllText($out, ($lines -join "`n") + "`n")
Write-Host "$total recipes written to $out"
