# Makes the addon's minimap icon, addon/WoWCharacterTracker/Icon.tga, from the app's logo
# (companion/WoWCharacterTracker/Assets/logo.png): cropped to the drawing, made square, resized to
# 64x64 and written as a 32-bit uncompressed TGA with alpha, which every WoW client loads. Run it
# again when the logo changes:
#   powershell -File tools\make-addon-icon.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root "companion\WoWCharacterTracker\Assets\logo.png"
$target = Join-Path $root "addon\WoWCharacterTracker\Icon.tga"
$size = 64

$logo = [System.Drawing.Bitmap]::new($source)
try {
    # The drawing's bounds (pixels that aren't see-through), so the icon isn't mostly empty margin.
    $minX = $logo.Width; $minY = $logo.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $logo.Height; $y++) {
        for ($x = 0; $x -lt $logo.Width; $x++) {
            if ($logo.GetPixel($x, $y).A -gt 8) {
                if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { throw "The logo is empty." }
    $side = [Math]::Max($maxX - $minX + 1, $maxY - $minY + 1)
    $cx = ($minX + $maxX + 1) / 2; $cy = ($minY + $maxY + 1) / 2
    $crop = [System.Drawing.RectangleF]::new($cx - $side / 2, $cy - $side / 2, $side, $side)

    $icon = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $g = [System.Drawing.Graphics]::FromImage($icon)
        try {
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $g.DrawImage($logo, [System.Drawing.RectangleF]::new(0, 0, $size, $size), $crop, [System.Drawing.GraphicsUnit]::Pixel)
        }
        finally { $g.Dispose() }

        # TGA: 18 byte header (type 2 = uncompressed true color, 32 bits, 8 alpha bits, rows from the
        # bottom up), then BGRA pixels.
        $bytes = [byte[]]::new(18 + $size * $size * 4)
        $bytes[2] = 2
        $bytes[12] = $size -band 0xFF; $bytes[13] = $size -shr 8
        $bytes[14] = $size -band 0xFF; $bytes[15] = $size -shr 8
        $bytes[16] = 32
        $bytes[17] = 8
        $i = 18
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $icon.GetPixel($x, $y)
                $bytes[$i] = $c.B; $bytes[$i + 1] = $c.G; $bytes[$i + 2] = $c.R; $bytes[$i + 3] = $c.A
                $i += 4
            }
        }
        [System.IO.File]::WriteAllBytes($target, $bytes)
        Write-Host "Wrote $target"
    }
    finally { $icon.Dispose() }
}
finally { $logo.Dispose() }
