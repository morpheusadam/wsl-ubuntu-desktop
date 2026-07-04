param([string]$OutPath = 'C:\MyApp\icon.ico')

Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Ubuntu orange disc
    $orange = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 233, 84, 32))
    $g.FillEllipse($orange, 0, 0, $size - 1, $size - 1)

    # white terminal prompt >_
    $fontSize = [Math]::Max(4, [int]($size * 0.42))
    $font = New-Object System.Drawing.Font('Consolas', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $white = [System.Drawing.Brushes]::White
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'
    $fmt.LineAlignment = 'Center'
    $rect = New-Object System.Drawing.RectangleF(0, ($size * 0.02), $size, $size)
    $g.DrawString('>_', $font, $white, $rect, $fmt)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) { $pngs += , (New-IconPng $s) }

# Build ICO: ICONDIR + ICONDIRENTRY per image + PNG payloads (Vista+ PNG-in-ICO)
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($ms)
$w.Write([uint16]0)              # reserved
$w.Write([uint16]1)              # type: icon
$w.Write([uint16]$sizes.Count)   # image count

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $len = ([byte[]]$pngs[$i]).Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim)         # width
    $w.Write([byte]$dim)         # height
    $w.Write([byte]0)            # palette
    $w.Write([byte]0)            # reserved
    $w.Write([uint16]1)          # planes
    $w.Write([uint16]32)         # bpp
    $w.Write([uint32]$len)       # data size
    $w.Write([uint32]$offset)    # data offset
    $offset += $len
}
foreach ($p in $pngs) { $w.Write([byte[]]$p) }
$w.Flush()

[System.IO.File]::WriteAllBytes($OutPath, $ms.ToArray())
$w.Dispose()
"ICO written: $OutPath ($([math]::Round((Get-Item $OutPath).Length/1KB, 1)) KB, sizes: $($sizes -join ','))"
