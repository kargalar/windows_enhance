param()

# Keep the geometry in sync with ui/public/favicon.svg (32 x 32 viewBox).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\WinForge.App\Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size * 4, $size * 4)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform($size / 8.0, $size / 8.0)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    foreach ($arc in @(@(0,0,180), @(12,0,270), @(12,12,0), @(0,12,90))) {
        $path.AddArc($arc[0], $arc[1], 20, 20, $arc[2], 90)
    }
    $path.CloseFigure()
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#171719'))
    $graphics.FillPath($background, $path)
    $pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#f5f5f5'), 1.8)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $top = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(16,6), [System.Drawing.PointF]::new(26,11.5),
        [System.Drawing.PointF]::new(16,17), [System.Drawing.PointF]::new(6,11.5))
    $graphics.DrawPolygon($pen, $top)
    foreach ($y in @(16,20.5)) {
        $points = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new(6,$y), [System.Drawing.PointF]::new(16,$y+5.5),
            [System.Drawing.PointF]::new(26,$y))
        $graphics.DrawLines($pen, $points)
    }
    $small = [System.Drawing.Bitmap]::new($size,$size)
    $resizer = [System.Drawing.Graphics]::FromImage($small)
    $resizer.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $resizer.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $resizer.DrawImage($bitmap,0,0,$size,$size)
    $stream = [System.IO.MemoryStream]::new()
    # Use a 32-bit DIB plus the legacy AND mask for Windows icon compatibility.
    $small.Save($stream, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp = $stream.ToArray()
    $maskLength = [int]([Math]::Ceiling($size / 32.0) * 4 * $size)
    $dib = [byte[]]::new($bmp.Length - 14 + $maskLength)
    [Array]::Copy($bmp, 14, $dib, 0, $bmp.Length - 14)
    [Array]::Copy([BitConverter]::GetBytes([int]($size * 2)), 0, $dib, 8, 4)
    $frames += ,$dib
    $stream.Dispose()
    $resizer.Dispose()
    $small.Dispose()
    $pen.Dispose()
    $background.Dispose()
    $path.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
$file = [System.IO.File]::Create((Join-Path $assetDirectory 'WinForge.ico'))
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
}
finally { $writer.Dispose() }
Write-Host 'Generated Assets\WinForge.ico (16-256 px).'
