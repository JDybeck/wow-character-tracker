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
$csproj = (Select-String -Path companion\WoWCharacterTracker\WoWCharacterTracker.csproj -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
if ($csproj -ne $Version) { throw "WoWCharacterTracker.csproj says version $csproj. Set <Version>$Version</Version> to match." }

# The addon has its own version, raised only when something in addon/ changed. Players' addons only
# update when it goes up, so a change without a raise would never reach them.
$versionLine = '(?m)^##\s*Version:\s*(\S+)'
$addon = [regex]::Match((Get-Content addon\WoWCharacterTracker\WoWCharacterTracker.toc -Raw), $versionLine).Groups[1].Value
$lastTag = git describe --tags --abbrev=0 --match 'v*'
$lastAddon = [regex]::Match(((git show "${lastTag}:addon/WoWCharacterTracker/WoWCharacterTracker.toc") -join "`n"), $versionLine).Groups[1].Value
git diff --quiet $lastTag HEAD -- addon
$addonChanged = $LASTEXITCODE -ne 0
if ($addonChanged -and $addon -eq $lastAddon) { throw "addon/ changed since $lastTag but the .toc still says $addon. Raise '## Version' so players get it." }
$AddonTag = "addon-v$addon"
if ($addonChanged -and (git tag --list $AddonTag)) { throw "$AddonTag already exists. Raise '## Version' in the .toc." }

git push origin HEAD
git tag $Tag
git push origin $Tag
Write-Host "Pushed $Tag. The build takes a few minutes: https://github.com/JDybeck/wow-character-tracker/actions"
# This tag is what sends the addon to the addon sites, so an app-only release uploads nothing.
if ($addonChanged) {
    git tag $AddonTag
    git push origin $AddonTag
    Write-Host "Pushed $AddonTag, the addon changed."
} else {
    Write-Host "The addon didn't change, it stays at v$addon."
}
