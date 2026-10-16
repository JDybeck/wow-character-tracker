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

git push origin HEAD
git tag $Tag
git push origin $Tag
Write-Host "Pushed $Tag. The build takes a few minutes: https://github.com/JDybeck/wow-character-tracker/actions"
