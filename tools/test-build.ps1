# Builds the app from this checkout into test-build\ and sets it up to run next to the real one:
# its own data folder (test mode: own single-instance name, no autostart, no updates), a copy of your
# settings, and copies of your saved game data in fake game folders, so it never writes to the real
# game and never posts on its own. Test buttons still post to your webhooks.
# -FreshData starts over from your current settings and saved data.
param([switch]$FreshData)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'test-build'
$app = Join-Path $out 'app'
$data = Join-Path $out 'data'
$game = Join-Path $out 'game'

dotnet publish (Join-Path $root 'companion\WoWCharacterTracker') -c Release -o $app -nologo -v q
if ($LASTEXITCODE) { throw 'Build failed.' }

if ($FreshData -and (Test-Path $out)) {
    foreach ($dir in $data, $game) { if (Test-Path $dir) { Remove-Item -Recurse -Force $dir } }
}

if (-not (Test-Path (Join-Path $data 'config.json'))) {
    New-Item -ItemType Directory -Force $data | Out-Null
    $real = Join-Path $env:LOCALAPPDATA 'WoWCharacterTracker\config.json'
    $json = Get-Content $real -Raw
    $cfg = $json | ConvertFrom-Json
    foreach ($p in $cfg.Flavors.PSObject.Properties) {
        $src = $p.Value.Path
        if (-not $src -or -not (Test-Path $src)) { continue }
        $dst = Join-Path $game (Split-Path $src -Leaf)
        New-Item -ItemType Directory -Force (Join-Path $dst 'WTF'), (Join-Path $dst 'Interface\AddOns') | Out-Null
        # The addon's saved files (per account) and the installed addon, so characters and "installed" show.
        Get-ChildItem (Join-Path $src 'WTF\Account') -Recurse -Filter 'WoWCharacterTracker.lua' -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'SavedVariables' } | ForEach-Object {
                $to = Join-Path $dst $_.FullName.Substring($src.Length + 1)
                New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
                Copy-Item $_.FullName $to
            }
        $addon = Join-Path $src 'Interface\AddOns\WoWCharacterTracker'
        if (Test-Path $addon) { Copy-Item -Recurse -Force $addon (Join-Path $dst 'Interface\AddOns\') }
        $build = Join-Path (Split-Path $src -Parent) '.build.info'
        if (Test-Path $build) { Copy-Item $build (Join-Path $game '.build.info') -Force }
        # Swapped as text so the rest of the file stays exactly as the app wrote it.
        $json = $json.Replace(($src | ConvertTo-Json), ($dst | ConvertTo-Json))
    }
    # The overlay's local web server would clash with the real app's.
    $json = $json -replace '"StreamEnabled"\s*:\s*true', '"StreamEnabled": false'
    [IO.File]::WriteAllText((Join-Path $data 'config.json'), $json, (New-Object Text.UTF8Encoding $false))
}

$run = @"
@echo off
rem Runs the test build next to the real app, with its own settings in data\.
set CHARACTERTRACKER_DATA_DIR=%~dp0data
start "" "%~dp0app\WoWCharacterTracker.exe"
"@
[IO.File]::WriteAllText((Join-Path $out 'Run test app.cmd'), $run, [Text.Encoding]::ASCII)
Write-Host "Test app ready. Start it with: $out\Run test app.cmd"
