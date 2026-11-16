Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repoRoot 'src\VocabularyCoach.App\Resources\app.ico'
$dir = Split-Path $out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

$sizes = @(16, 32, 48, 64, 128, 256)
$pngs = @()

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $radius = [float]($size * 0.22)
    $rect = New-Object System.Drawing.RectangleF(0.5, 0.5, ($size - 1), ($size - 1))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(255, 59, 130, 246), [System.Drawing.Color]::FromArgb(255, 29, 78, 216), 45)
    $g.FillPath($brush, $path)

    $fontSize = [float]($size * 0.52)
    $font = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF(0, (-$size * 0.03), $size, $size)
    $g.DrawString('V', $font, [System.Drawing.Brushes]::White, $textRect, $format)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,$ms.ToArray()

    $g.Dispose(); $bmp.Dispose(); $font.Dispose(); $brush.Dispose(); $path.Dispose()
}

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $dim = if ($size -ge 256) { 0 } else { $size }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}

foreach ($png in $pngs) { $bw.Write($png) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Write-Output "icon written: $out ($((Get-Item $out).Length) bytes)"
