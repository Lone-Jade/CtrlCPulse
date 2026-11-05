<#
.SYNOPSIS
    程序化绘制 CtrlCPulse 图标并写出多尺寸 .ico。

.DESCRIPTION
    图标完全由本脚本绘制：圆角渐变方块 + 白色对勾，不引用任何外部素材。
    每个尺寸独立按矢量规则重绘，保证 16px 托盘尺寸依然清晰。

.EXAMPLE
    ./tools/make-icon.ps1 -Out assets/app.ico
#>
[CmdletBinding()]
param(
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $r = [Math]::Min($Radius, [Math]::Min($Width, $Height) / 2.0)
    $d = $r * 2.0

    if ($d -le 0.2) {
        $path.AddRectangle([System.Drawing.RectangleF]::new($X, $Y, $Width, $Height))
        return $path
    }

    $path.AddArc($X, $Y, $d, $d, 180.0, 90.0)
    $path.AddArc($X + $Width - $d, $Y, $d, $d, 270.0, 90.0)
    $path.AddArc($X + $Width - $d, $Y + $Height - $d, $d, $d, 0.0, 90.0)
    $path.AddArc($X, $Y + $Height - $d, $d, $d, 90.0, 90.0)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $pad = [Math]::Max(0.6, $Size * 0.035)
        $rect = [System.Drawing.RectangleF]::new($pad, $pad, $Size - 2.0 * $pad, $Size - 2.0 * $pad)
        $radius = $Size * 0.235
        $path = New-RoundedPath -X $rect.X -Y $rect.Y -Width $rect.Width -Height $rect.Height -Radius $radius

        # 蓝紫 → 青绿的对角渐变底
        $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $rect,
            [System.Drawing.Color]::FromArgb(255, 88, 102, 255),
            [System.Drawing.Color]::FromArgb(255, 40, 198, 208),
            42.0)
        $graphics.FillPath($brush, $path)
        $brush.Dispose()

        # 左上角柔光，让纯色块更有体积感
        $glow = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $glow.AddEllipse($rect.X - $rect.Width * 0.15, $rect.Y - $rect.Height * 0.35, $rect.Width * 0.95, $rect.Height * 0.95)
        $glowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(46, 255, 255, 255))
        $state = $graphics.Save()
        $graphics.SetClip($path)
        $graphics.FillPath($glowBrush, $glow)
        $graphics.Restore($state)
        $glowBrush.Dispose()
        $glow.Dispose()

        # 白色对勾：粗描边 + 圆头，16px 下仍然可辨
        $stroke = [Math]::Max(1.5, $Size * 0.115)
        $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, $stroke)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $points = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($Size * 0.295, $Size * 0.520),
            [System.Drawing.PointF]::new($Size * 0.445, $Size * 0.665),
            [System.Drawing.PointF]::new($Size * 0.715, $Size * 0.355))
        $graphics.DrawLines($pen, $points)
        $pen.Dispose()

        $path.Dispose()
    }
    finally {
        $graphics.Dispose()
    }

    return $bitmap
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = New-Object System.Collections.Generic.List[byte[]]

foreach ($size in $sizes) {
    $bitmap = New-IconBitmap -Size $size
    $stream = [System.IO.MemoryStream]::new()
    try {
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $images.Add($stream.ToArray())
    }
    finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$directory = Split-Path -Parent $Out
if ($directory -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$file = [System.IO.File]::Create($Out)
$writer = [System.IO.BinaryWriter]::new($file)

try {
    # ICONDIR
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count

    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $length = $images[$i].Length
        $dimension = if ($size -ge 256) { 0 } else { $size }

        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)      # 调色板颜色数
        $writer.Write([byte]0)      # 保留
        $writer.Write([uint16]1)    # 色彩平面
        $writer.Write([uint16]32)   # 每像素位数
        $writer.Write([uint32]$length)
        $writer.Write([uint32]$offset)
        $offset += $length
    }

    foreach ($image in $images) {
        $writer.Write([byte[]]$image)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

$info = Get-Item $Out
Write-Host ("图标已生成: {0} ({1:N0} 字节, {2} 个尺寸)" -f $info.FullName, $info.Length, $sizes.Count)
