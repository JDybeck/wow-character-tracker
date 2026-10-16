# Publishes a release: builds the exe with that version, takes the notes from CHANGELOG.md,
# tags this repo, and creates the release in the releases-only repo, which is
# where the app looks for updates and where people download it. Uses your gh login.
#
#   .\release.cmd 0.8.0
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = "Stop"
$PublicRepo = "JDybeck/wow-character-tracker-releases"
$Tag = "v$Version"
Set-Location $PSScriptRoot

if (-not ($Version -match '^\d+\.\d+\.\d+$')) { throw "Use a version like 0.8.0" }
if (git status --porcelain) { throw "Commit your changes first, the release is built from what's committed." }
if (git tag --list $Tag) { throw "$Tag already exists." }

# Notes: "## v0.8.0" up to the next "## " heading.
$lines = Get-Content CHANGELOG.md
$start = [Array]::FindIndex($lines, [Predicate[string]] { param($l) $l -match "^##\s+$([regex]::Escape($Tag))\b" })
if ($start -lt 0) { throw "CHANGELOG.md has no section for $Tag. Add one first." }
$end = [Array]::FindIndex($lines, $start + 1, [Predicate[string]] { param($l) $l -match '^##\s' })
if ($end -lt 0) { $end = $lines.Count }
$notes = ($lines[($start + 1)..($end - 1)] -join "`n").Trim()
$notesFile = Join-Path $env:TEMP "wct-release-notes.md"
[IO.File]::WriteAllText($notesFile, $notes)

Write-Host "Building $Tag..."
$out = Join-Path $env:TEMP "wct-release-$Version"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish companion\WoWCharacterTracker -c Release -o $out -nologo -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host "Tagging $Tag here..."
git tag $Tag
git push origin $Tag

Write-Host "Publishing $Tag to $PublicRepo..."
gh release create $Tag "$out\WoWCharacterTracker.exe" --repo $PublicRepo --title $Tag --notes-file $notesFile
if ($LASTEXITCODE -ne 0) { throw "Creating the release failed (the tag is pushed; fix it and run gh release create by hand)." }

Write-Host "Done: https://github.com/$PublicRepo/releases/tag/$Tag"
