<#
.SYNOPSIS
    提示卡片的像素级 + 窗口级视觉验收。

.DESCRIPTION
    本机模型无法直接“看”图片，因此把肉眼检查转成可重复的断言。
    几何来自 Win32 窗口矩形（独立于像素猜测），渲染来自截屏取色：
      1. 三张卡片的窗口尺寸 = 卡片尺寸 + 2×阴影边距，位置贴合工作区右下角；
      2. 窗口具备 LAYERED / NOACTIVATE / TOOLWINDOW / TOPMOST 样式（不抢焦点、不进 Alt+Tab）；
      3. 左侧类型色条颜色与宽度正确（文本蓝 / 图片紫 / 文件琥珀）；
      4. 圆角处透明、卡片内部深色半透明、图标与标题文字已绘制；
      5. 连续复制时三张卡片同时堆叠；
      6. 演示结束后卡片与色条像素全部消失；
      7. 全程前台窗口不变（提示不抢焦点）。
    截图保存在 dist\verify，便于人工复核。

.NOTES
    必须先声明 Per-Monitor V2 DPI 感知，否则 125%/150% 缩放下截屏与窗口坐标会被系统虚拟化。

.EXAMPLE
    ./tools/verify-visual.ps1
#>
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\CtrlCPulse.exe'),
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\verify')
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class Win32Probe {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
'@

[void][Win32Probe]::SetProcessDpiAwarenessContext([IntPtr](-4))

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not (Test-Path $Exe)) {
    throw "找不到 $Exe，请先运行 build.ps1"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$script:passed = 0
$script:failed = 0

function Check {
    param([string]$Name, [bool]$Ok, [string]$Detail)

    if ($Ok) {
        $script:passed++
        Write-Host ("PASS  {0}  {1}" -f $Name, $Detail)
    }
    else {
        $script:failed++
        Write-Host ("FAIL  {0}  {1}" -f $Name, $Detail) -ForegroundColor Red
    }
}

function Get-ToastWindows {
    param([int]$OwnerProcessId)

    $result = New-Object System.Collections.Generic.List[object]
    $callback = [Win32Probe+EnumWindowsProc]{
        param($hWnd, $lParam)

        $windowProcessId = 0
        [void][Win32Probe]::GetWindowThreadProcessId($hWnd, [ref]$windowProcessId)

        if ($windowProcessId -eq $OwnerProcessId -and [Win32Probe]::IsWindowVisible($hWnd)) {
            $rect = New-Object Win32Probe+RECT
            [void][Win32Probe]::GetWindowRect($hWnd, [ref]$rect)

            if (($rect.Right - $rect.Left) -gt 200) {
                $result.Add([pscustomobject]@{
                    Left = $rect.Left
                    Top = $rect.Top
                    Right = $rect.Right
                    Bottom = $rect.Bottom
                    Width = $rect.Right - $rect.Left
                    Height = $rect.Bottom - $rect.Top
                    ExStyle = [Win32Probe]::GetWindowLong($hWnd, -20)
                })
            }
        }

        return $true
    }

    [void][Win32Probe]::EnumWindows($callback, [IntPtr]::Zero)
    return @($result | Sort-Object Top)
}

function Capture-Screen {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($bounds.X, $bounds.Y, 0, 0, $bitmap.Size)
    $graphics.Dispose()

    $width = [Math]::Min(720, $bounds.Width)
    $height = [Math]::Min(660, $bounds.Height)
    $rect = New-Object System.Drawing.Rectangle(($bounds.Width - $width), ($bounds.Height - $height), $width, $height)
    $crop = $bitmap.Clone($rect, $bitmap.PixelFormat)
    $bitmap.Dispose()
    return $crop
}

function Save-Capture {
    param([System.Drawing.Bitmap]$Bitmap, [string]$Name)

    $path = Join-Path $OutDir $Name
    $Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
}

function Get-PixelBuffer {
    param([System.Drawing.Bitmap]$Bitmap)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $Bitmap.Width, $Bitmap.Height)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object 'byte[]' ($data.Stride * $data.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, [int]$bytes.Length)
    $Bitmap.UnlockBits($data)

    return @{
        Bytes = $bytes
        Stride = [int]$data.Stride
        Width = [int]$Bitmap.Width
        Height = [int]$Bitmap.Height
    }
}

function Get-Pixel {
    param($Buffer, [int]$X, [int]$Y)

    if ($X -lt 0 -or $Y -lt 0 -or $X -ge $Buffer.Width -or $Y -ge $Buffer.Height) {
        return @{ R = 0; G = 0; B = 0; A = 0 }
    }

    $offset = $Y * $Buffer.Stride + $X * 4
    return @{
        B = [int]$Buffer.Bytes[$offset]
        G = [int]$Buffer.Bytes[$offset + 1]
        R = [int]$Buffer.Bytes[$offset + 2]
        A = [int]$Buffer.Bytes[$offset + 3]
    }
}

function Get-Luma {
    param($Pixel)

    return 0.299 * $Pixel.R + 0.587 * $Pixel.G + 0.114 * $Pixel.B
}

function Find-AccentStripe {
    param($Buffer, [int]$R, [int]$G, [int]$B)

    $startX = [int]($Buffer.Width * 0.25)
    $counts = New-Object 'int[]' $Buffer.Width

    for ($y = 0; $y -lt $Buffer.Height; $y++) {
        $rowOffset = $y * $Buffer.Stride
        for ($x = $startX; $x -lt $Buffer.Width; $x++) {
            $offset = $rowOffset + $x * 4
            if ([Math]::Abs([int]$Buffer.Bytes[$offset + 2] - $R) -le 8 -and
                [Math]::Abs([int]$Buffer.Bytes[$offset + 1] - $G) -le 8 -and
                [Math]::Abs([int]$Buffer.Bytes[$offset] - $B) -le 8) {
                $counts[$x]++
            }
        }
    }

    $best = -1
    $bestCount = 0
    for ($x = $startX; $x -lt $Buffer.Width; $x++) {
        if ($counts[$x] -gt $bestCount) {
            $bestCount = $counts[$x]
            $best = $x
        }
    }

    if ($best -lt 0 -or $bestCount -lt 20) {
        return @{ Found = $false; Count = 0; StripeX0 = 0; StripeX1 = 0; Rows = @() }
    }

    $threshold = [Math]::Max(10, [int]($bestCount * 0.6))
    $x0 = $best
    while ($x0 - 1 -ge $startX -and $counts[$x0 - 1] -ge $threshold) { $x0-- }
    $x1 = $best
    while ($x1 + 1 -lt $Buffer.Width -and $counts[$x1 + 1] -ge $threshold) { $x1++ }

    $rows = New-Object System.Collections.Generic.List[int]
    for ($y = 0; $y -lt $Buffer.Height; $y++) {
        $offset = $y * $Buffer.Stride + $best * 4
        if ([Math]::Abs([int]$Buffer.Bytes[$offset + 2] - $R) -le 8 -and
            [Math]::Abs([int]$Buffer.Bytes[$offset + 1] - $G) -le 8 -and
            [Math]::Abs([int]$Buffer.Bytes[$offset] - $B) -le 8) {
            $rows.Add($y)
        }
    }

    return @{
        Found = $true
        Count = $bestCount
        StripeX0 = $x0
        StripeX1 = $x1
        Rows = $rows
    }
}

function Count-BrightPixels {
    param($Buffer, [int]$X0, [int]$X1, [int]$Y0, [int]$Y1, [int]$Threshold = 210)

    $hits = 0
    for ($y = [Math]::Max(0, $Y0); $y -le [Math]::Min($Buffer.Height - 1, $Y1); $y++) {
        $rowOffset = $y * $Buffer.Stride
        for ($x = [Math]::Max(0, $X0); $x -le [Math]::Min($Buffer.Width - 1, $X1); $x++) {
            $offset = $rowOffset + $x * 4
            if ($Buffer.Bytes[$offset] -ge $Threshold -and
                $Buffer.Bytes[$offset + 1] -ge $Threshold -and
                $Buffer.Bytes[$offset + 2] -ge $Threshold) {
                $hits++
            }
        }
    }

    return $hits
}

# ---------------- 规格（96 DPI 基准 × 当前缩放） ----------------

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$workArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$graphics = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
$scale = $graphics.DpiX / 96.0
$graphics.Dispose()

$cardWidth = [int][Math]::Round(344 * $scale)
$marginRight = [int][Math]::Round(16 * $scale)
$marginBottom = [int][Math]::Round(12 * $scale)
$shadow = [int][Math]::Round(14 * $scale)
$gap = [int][Math]::Round(8 * $scale)

$cropWidth = [Math]::Min(720, $screen.Width)
$cropHeight = [Math]::Min(660, $screen.Height)
$cropX = $screen.Width - $cropWidth
$cropY = $screen.Height - $cropHeight

Write-Host ("屏幕 {0}x{1}，工作区 {2}x{3}，缩放 {4:P0}" -f $screen.Width, $screen.Height, $workArea.Width, $workArea.Height, $scale)

function Test-Card {
    param(
        [string]$Label,
        [System.Drawing.Bitmap]$Bitmap,
        $Windows,
        [int]$AccentR,
        [int]$AccentG,
        [int]$AccentB,
        [int]$BaseHeight
    )

    $expectedCardHeight = [int][Math]::Round($BaseHeight * $scale)
    $expectedWindowWidth = $cardWidth + 2 * $shadow
    $expectedWindowHeight = $expectedCardHeight + 2 * $shadow

    $window = @($Windows | Sort-Object Bottom)[-1]
    if ($null -eq $window) {
        Check "$Label 窗口存在" $false '未找到可见窗口'
        return
    }

    Check "$Label 窗口宽度 $expectedWindowWidth" ([Math]::Abs($window.Width - $expectedWindowWidth) -le 2) ("实际 " + $window.Width)
    Check "$Label 窗口高度 $expectedWindowHeight" ([Math]::Abs($window.Height - $expectedWindowHeight) -le 2) ("实际 " + $window.Height)

    $expectedRight = $workArea.Right - $marginRight + $shadow
    Check "$Label 贴右边缘" ([Math]::Abs($window.Right - $expectedRight) -le 2) ("right " + $window.Right + " 期望 " + $expectedRight)

    $expectedBottom = $workArea.Bottom - $marginBottom + $shadow
    Check "$Label 贴工作区底部" ([Math]::Abs($window.Bottom - $expectedBottom) -le 2) ("bottom " + $window.Bottom + " 期望 " + $expectedBottom)

    $required = 0x08000000 -bor 0x00080000 -bor 0x00000080 -bor 0x00000008
    Check "$Label 分层/不抢焦点/工具窗口/置顶样式" (($window.ExStyle -band $required) -eq $required) ("ExStyle 0x{0:X8}" -f $window.ExStyle)

    $cardLeft = $window.Left + $shadow - $cropX
    $cardTop = $window.Top + $shadow - $cropY
    $cardBottom = $window.Bottom - $shadow - $cropY
    $midY = [int](($cardTop + $cardBottom) / 2)

    $buffer = Get-PixelBuffer $Bitmap
    $accent = Find-AccentStripe -Buffer $buffer -R $AccentR -G $AccentG -B $AccentB

    Check "$Label 色条存在" ($accent.Found) ("最长列匹配 " + $accent.Count)
    if (-not $accent.Found) { return }

    $stripeWidth = $accent.StripeX1 - $accent.StripeX0 + 1
    $expectedStripe = [int][Math]::Round(4 * $scale)
    Check "$Label 色条宽度 $expectedStripe px" ([Math]::Abs($stripeWidth - $expectedStripe) -le 3) ("实际 " + $stripeWidth)
    Check "$Label 色条贴合卡片左边" ([Math]::Abs($accent.StripeX0 - $cardLeft) -le 3) ("stripe " + $accent.StripeX0 + " / card " + $cardLeft)

    $rows = $accent.Rows
    $span = $rows[$rows.Count - 1] - $rows[0] + 1
    $inset = [int][Math]::Round(8 * $scale)
    Check "$Label 色条纵向跨度合理" ($span -ge ($expectedCardHeight - 2 * $inset) -and $span -le $expectedCardHeight) ("实际 " + $span + " / 卡片 " + $expectedCardHeight)

    $interiorX = $cardLeft + [int][Math]::Round(48 * $scale)
    $interior = Get-Pixel $buffer $interiorX $midY
    Check "$Label 卡片内部深色" ((Get-Luma $interior) -lt 110) ("luma " + [int](Get-Luma $interior))

    $corner = Get-Pixel $buffer ($cardLeft + 2) ($cardTop + 2)
    Check "$Label 左上圆角透明" ((Get-Luma $corner) -gt 120) ("圆角像素 luma " + [int](Get-Luma $corner))

    $checkHits = Count-BrightPixels -Buffer $buffer `
        -X0 ($cardLeft + [int](10 * $scale)) -X1 ($cardLeft + [int](55 * $scale)) `
        -Y0 ($midY - [int](22 * $scale)) -Y1 ($midY + [int](22 * $scale))
    Check "$Label 对勾图标可见" ($checkHits -gt 3) ("亮像素 " + $checkHits)

    $titleHits = Count-BrightPixels -Buffer $buffer `
        -X0 ($cardLeft + [int](70 * $scale)) -X1 ($cardLeft + [int](210 * $scale)) `
        -Y0 ($cardTop + [int](10 * $scale)) -Y1 ($cardTop + [int](36 * $scale))
    Check "$Label 标题文字可见" ($titleHits -gt 10) ("亮像素 " + $titleHits)
}

function Wait-ForCards {
    param([int]$OwnerProcessId, [int]$Count, [int]$TimeoutMs = 6000)

    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    $previous = $null
    $stable = 0

    while ((Get-Date) -lt $deadline) {
        $windows = @(Get-ToastWindows -OwnerProcessId $OwnerProcessId)

        if ($windows.Count -eq $Count) {
            $signature = ($windows | Sort-Object Top | ForEach-Object { "$($_.Top),$($_.Height)" }) -join '|'
            if ($signature -eq $previous) {
                $stable++
            }
            else {
                $stable = 0
                $previous = $signature
            }

            if ($stable -ge 2) {
                return $windows
            }
        }
        else {
            $stable = 0
            $previous = $null
        }

        Start-Sleep -Milliseconds 60
    }

    return @(Get-ToastWindows -OwnerProcessId $OwnerProcessId)
}

# ---------------- 采集 ----------------

Get-Process -Name 'CtrlCPulse' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

$foregroundBefore = [Win32Probe]::GetForegroundWindow()

$baselineBitmap = Capture-Screen
Save-Capture $baselineBitmap '00-baseline.png'
$baseline = Get-PixelBuffer $baselineBitmap

$process = Start-Process -FilePath $Exe -ArgumentList '--demo' -PassThru

$textWindows = Wait-ForCards -OwnerProcessId $process.Id -Count 1
$textBitmap = Capture-Screen
Save-Capture $textBitmap '01-text.png'

$imageWindows = Wait-ForCards -OwnerProcessId $process.Id -Count 2
$imageBitmap = Capture-Screen
Save-Capture $imageBitmap '02-image.png'

$filesWindows = Wait-ForCards -OwnerProcessId $process.Id -Count 3
$filesBitmap = Capture-Screen
Save-Capture $filesBitmap '03-files.png'

$foregroundDuring = [Win32Probe]::GetForegroundWindow()

$process.WaitForExit(15000) | Out-Null
Start-Sleep -Milliseconds 500

$goneBitmap = Capture-Screen
Save-Capture $goneBitmap '04-gone.png'
$goneBuffer = Get-PixelBuffer $goneBitmap
$goneWindows = Get-ToastWindows -OwnerProcessId $process.Id

# ---------------- 断言 ----------------

Check '前台窗口未被抢占' ($foregroundBefore -eq $foregroundDuring) ("before " + $foregroundBefore + " / during " + $foregroundDuring)

Test-Card -Label '文本卡片' -Bitmap $textBitmap -Windows $textWindows `
    -AccentR 92 -AccentG 148 -AccentB 255 -BaseHeight 84

Test-Card -Label '图片卡片' -Bitmap $imageBitmap -Windows $imageWindows `
    -AccentR 172 -AccentG 116 -AccentB 255 -BaseHeight 108

Test-Card -Label '文件卡片' -Bitmap $filesBitmap -Windows $filesWindows `
    -AccentR 245 -AccentG 176 -AccentB 74 -BaseHeight 96

Check '三张卡片同时存在' (@($filesWindows).Count -eq 3) ("可见窗口 " + @($filesWindows).Count)

$stacked = @($filesWindows | Sort-Object Top)
if ($stacked.Count -eq 3) {
    $heightSum = ($stacked | Measure-Object -Property Height -Sum).Sum
    $expectedSum = [int][Math]::Round((84 + 108 + 96) * $scale) + 6 * $shadow
    Check '三张卡片纵向堆叠' ([Math]::Abs($heightSum - $expectedSum) -le 6) ("高度合计 " + $heightSum + " 期望 " + $expectedSum)
}

$filesBuffer = Get-PixelBuffer $filesBitmap
$stackText = Find-AccentStripe -Buffer $filesBuffer -R 92 -G 148 -B 255
$stackImage = Find-AccentStripe -Buffer $filesBuffer -R 172 -G 116 -B 255
$stackFiles = Find-AccentStripe -Buffer $filesBuffer -R 245 -G 176 -B 74
Check '三种色条同屏可见' ($stackText.Found -and $stackImage.Found -and $stackFiles.Found) `
    ("文本 " + $stackText.Found + " / 图片 " + $stackImage.Found + " / 文件 " + $stackFiles.Found)

$leftover = Find-AccentStripe -Buffer $goneBuffer -R 92 -G 148 -B 255
Check '卡片消失后无残留' ((-not $leftover.Found) -and @($goneWindows).Count -eq 0) ("残留窗口 " + @($goneWindows).Count)

$textBitmap.Dispose()
$imageBitmap.Dispose()
$filesBitmap.Dispose()
$goneBitmap.Dispose()
$baselineBitmap.Dispose()

Write-Host ''
Write-Host ("视觉验收: {0} 通过, {1} 失败" -f $script:passed, $script:failed)
Write-Host ("截图目录: {0}" -f $OutDir)

if ($script:failed -gt 0) {
    exit 1
}
