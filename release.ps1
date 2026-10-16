# Starts a release: checks that CHANGELOG.md has notes for the version, then tags and pushes.
# The GitHub workflow (.github/workflows/release.yml) builds the exe and creates the release.
#
#   .\release.cmd 0.9.0
param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = "Stop"
$Tag = "v$Version"
Set-Location $PSScriptRoot

if (-not ($Version -match '^\d+\.\d+\.\d+$')) { throw "Use a version like 0.9.0" }
if (git status --porcelain) { throw "Commit your changes first, the release is built from what's committed." }
if (git tag --list $Tag) { throw "$Tag already exists." }
if (-not (Select-String -Path CHANGELOG.md -Pattern "^##\s+$([regex]::Escape($Tag))\b" -Quiet)) { throw "CHANGELOG.md has no section for $Tag. Add one first." }
# The app and the addon go out with the same version number.
$toc = (Select-String -Path addon\WoWCharacterTracker\WoWCharacterTracker.toc -Pattern '^##\s*Version:\s*(\S+)').Matches[0].Groups[1].Value
if ($toc -ne $Version) { throw "The addon's .toc says version $toc. Set '## Version: $Version' so the app and the addon match." }
$csproj = (Select-String -Path companion\WoWCharacterTracker\WoWCharacterTracker.csproj -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
if ($csproj -ne $Version) { throw "WoWCharacterTracker.csproj says version $csproj. Set <Version>$Version</Version> to match." }

git push origin HEAD
git tag $Tag
git push origin $Tag
Write-Host "Pushed $Tag. The build takes a few minutes: https://github.com/JDybeck/wow-character-tracker/actions"
