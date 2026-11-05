<#
.SYNOPSIS
    端到端功能验收：真实剪贴板 → 监听 → 提示卡片 → 单实例 → 空闲资源占用。

.DESCRIPTION
    必须用 STA 运行（Clipboard API 要求）：
        pwsh -STA -NoProfile -File tools\verify-clipboard.ps1

    覆盖场景：
      1. 首次运行弹出欢迎卡片；
      2. 复制文字 → 文本卡片出现并自动消失；
      3. 复制图片 → 图片卡片（带缩略图，卡片更高）；
      4. 复制文件 → 文件卡片；
      5. 带“剪贴板查看器忽略”标记的内容 → 不提示；
      6. 重复启动 → 新进程立即退出，已有实例提示“已在运行”；
      7. 空闲 CPU / 内存占用在预算内。
    测试结束后恢复原有剪贴板内容。
#>
[CmdletBinding()]
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\CtrlCPulse.exe'),
    [int]$TimeoutMs = 8000
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class Win32ClipProbe {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
'@

[void][Win32ClipProbe]::SetProcessDpiAwarenessContext([IntPtr](-4))

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

if (-not (Test-Path $Exe)) {
    throw "找不到 $Exe，请先运行 build.ps1"
}

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
    $callback = [Win32ClipProbe+EnumWindowsProc]{
        param($hWnd, $lParam)

        $windowProcessId = 0
        [void][Win32ClipProbe]::GetWindowThreadProcessId($hWnd, [ref]$windowProcessId)

        if ($windowProcessId -eq $OwnerProcessId -and [Win32ClipProbe]::IsWindowVisible($hWnd)) {
            $rect = New-Object Win32ClipProbe+RECT
            [void][Win32ClipProbe]::GetWindowRect($hWnd, [ref]$rect)

            if (($rect.Right - $rect.Left) -gt 200) {
                $result.Add([pscustomobject]@{
                    Left = $rect.Left
                    Top = $rect.Top
                    Right = $rect.Right
                    Bottom = $rect.Bottom
                    Width = $rect.Right - $rect.Left
                    Height = $rect.Bottom - $rect.Top
                })
            }
        }

        return $true
    }

    [void][Win32ClipProbe]::EnumWindows($callback, [IntPtr]::Zero)
    return @($result | Sort-Object Top)
}

function Wait-ToastCount {
    param([int]$OwnerProcessId, [int]$Count, [int]$WaitMs = 4000)

    $deadline = (Get-Date).AddMilliseconds($WaitMs)
    while ((Get-Date) -lt $deadline) {
        $windows = @(Get-ToastWindows -OwnerProcessId $OwnerProcessId)
        if ($windows.Count -eq $Count) {
            return $windows
        }
        Start-Sleep -Milliseconds 80
    }

    return @(Get-ToastWindows -OwnerProcessId $OwnerProcessId)
}

function Wait-NoToast {
    param([int]$OwnerProcessId, [int]$WaitMs = 6000)

    $deadline = (Get-Date).AddMilliseconds($WaitMs)
    while ((Get-Date) -lt $deadline) {
        if (@(Get-ToastWindows -OwnerProcessId $OwnerProcessId).Count -eq 0) {
            return $true
        }
        Start-Sleep -Milliseconds 80
    }

    return $false
}

$graphics = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
$scale = $graphics.DpiX / 96.0
$graphics.Dispose()

$shadow = [int][Math]::Round(14 * $scale)
$textHeight = [int][Math]::Round(84 * $scale) + 2 * $shadow
$imageHeight = [int][Math]::Round(108 * $scale) + 2 * $shadow
$filesHeight = [int][Math]::Round(96 * $scale) + 2 * $shadow

Write-Host ("屏幕缩放 {0:P0}，预期窗口高度：文本 {1} / 图片 {2} / 文件 {3}" -f $scale, $textHeight, $imageHeight, $filesHeight)

# ---- 备份剪贴板 ----
$originalText = $null
$originalImage = $null
$originalFiles = $null
try {
    if ([System.Windows.Forms.Clipboard]::ContainsText()) { $originalText = [System.Windows.Forms.Clipboard]::GetText() }
    elseif ([System.Windows.Forms.Clipboard]::ContainsImage()) { $originalImage = [System.Windows.Forms.Clipboard]::GetImage() }
    elseif ([System.Windows.Forms.Clipboard]::ContainsFileDropList()) { $originalFiles = [System.Windows.Forms.Clipboard]::GetFileDropList() }
}
catch {
    Write-Host '（无法读取当前剪贴板，跳过备份）'
}

$tempDirectory = Join-Path $env:TEMP ('CtrlCPulseClip_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
$appProcess = $null

try {
    Get-Process -Name 'CtrlCPulse' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400

    # 清空配置（便携模式：settings.ini 在 exe 同目录），验证首次运行欢迎卡片
    $settingsFile = Join-Path (Split-Path -Parent $Exe) 'settings.ini'
    if (Test-Path $settingsFile) { Remove-Item $settingsFile -Force }

    $appProcess = Start-Process -FilePath $Exe -ArgumentList '--tray' -PassThru
    $appId = $appProcess.Id

    $welcome = Wait-ToastCount -OwnerProcessId $appId -Count 1 -WaitMs $TimeoutMs
    Check '首次运行欢迎卡片' (@($welcome).Count -eq 1 -and $welcome[0].Height -eq $textHeight) `
        ("窗口 " + @($welcome).Count + "，高度 " + $(if (@($welcome).Count -eq 1) { $welcome[0].Height } else { 'n/a' }))

    Check '欢迎卡片自动消失' (Wait-NoToast -OwnerProcessId $appId -WaitMs $TimeoutMs) ''

    # ---- 文字 ----
    [System.Windows.Forms.Clipboard]::SetText('CtrlCPulse 功能测试：这是一段用于验证文字提示的示例内容。')
    $textToast = Wait-ToastCount -OwnerProcessId $appId -Count 1
    Check '复制文字触发提示' (@($textToast).Count -eq 1 -and $textToast[0].Height -eq $textHeight) `
        ("高度 " + $(if (@($textToast).Count -eq 1) { $textToast[0].Height } else { 'n/a' }) + " 期望 " + $textHeight)
    Check '文字提示自动消失' (Wait-NoToast -OwnerProcessId $appId) ''

    # ---- 图片 ----
    $bitmap = New-Object System.Drawing.Bitmap 320, 180
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::FromArgb(255, 70, 120, 240))
    $graphics.Dispose()
    [System.Windows.Forms.Clipboard]::SetImage($bitmap)
    $bitmap.Dispose()

    $imageToast = Wait-ToastCount -OwnerProcessId $appId -Count 1
    Check '复制图片触发提示' (@($imageToast).Count -eq 1 -and $imageToast[0].Height -eq $imageHeight) `
        ("高度 " + $(if (@($imageToast).Count -eq 1) { $imageToast[0].Height } else { 'n/a' }) + " 期望 " + $imageHeight)
    Check '图片提示自动消失' (Wait-NoToast -OwnerProcessId $appId) ''

    # ---- 文件 ----
    $fileA = Join-Path $tempDirectory '验收报告.pdf'
    $fileB = Join-Path $tempDirectory '验收截图.png'
    Set-Content -Path $fileA -Value 'report' -Encoding UTF8
    Set-Content -Path $fileB -Value 'image' -Encoding UTF8

    $fileList = New-Object System.Collections.Specialized.StringCollection
    $fileList.Add($fileA) | Out-Null
    $fileList.Add($fileB) | Out-Null
    [System.Windows.Forms.Clipboard]::SetFileDropList($fileList)

    $filesToast = Wait-ToastCount -OwnerProcessId $appId -Count 1
    Check '复制文件触发提示' (@($filesToast).Count -eq 1 -and $filesToast[0].Height -eq $filesHeight) `
        ("高度 " + $(if (@($filesToast).Count -eq 1) { $filesToast[0].Height } else { 'n/a' }) + " 期望 " + $filesHeight)
    Check '文件提示自动消失' (Wait-NoToast -OwnerProcessId $appId) ''

    # ---- 私密标记 ----
    $secret = New-Object System.Windows.Forms.DataObject
    $secret.SetData([System.Windows.Forms.DataFormats]::UnicodeText, 'password-token-should-not-toast')
    $secret.SetData('Clipboard Viewer Ignore', $true)
    [System.Windows.Forms.Clipboard]::SetDataObject($secret, $true)

    Start-Sleep -Milliseconds 1200
    $secretWindows = @(Get-ToastWindows -OwnerProcessId $appId)
    Check '带忽略标记的剪贴板不提示' ($secretWindows.Count -eq 0) ("窗口 " + $secretWindows.Count)

    # ---- 单实例 ----
    $second = Start-Process -FilePath $Exe -ArgumentList '--tray' -PassThru
    $exited = $second.WaitForExit(5000)
    Check '重复启动的进程自动退出' ($exited -and $second.ExitCode -eq 0) ("exited=" + $exited + " code=" + $(if ($exited) { $second.ExitCode } else { 'n/a' }))

    $duplicateToast = Wait-ToastCount -OwnerProcessId $appId -Count 1
    Check '已有实例提示“已在运行”' (@($duplicateToast).Count -eq 1 -and $duplicateToast[0].Height -eq $textHeight) `
        ("高度 " + $(if (@($duplicateToast).Count -eq 1) { $duplicateToast[0].Height } else { 'n/a' }))

    Wait-NoToast -OwnerProcessId $appId | Out-Null

    # ---- 空闲资源 ----
    $process = Get-Process -Id $appId
    $cpuStart = $process.TotalProcessorTime.TotalMilliseconds
    Start-Sleep -Seconds 2
    $process.Refresh()
    $cpuEnd = $process.TotalProcessorTime.TotalMilliseconds
    $cpuPercent = ($cpuEnd - $cpuStart) / (2000.0 * [Environment]::ProcessorCount) * 100
    $memoryMb = $process.WorkingSet64 / 1MB

    Check '空闲 CPU 低于 5%' ($cpuPercent -lt 5) ("{0:N2}%" -f $cpuPercent)
    Check '常驻内存低于 120 MB' ($memoryMb -lt 120) ("{0:N1} MB" -f $memoryMb)
}
finally {
    if ($appProcess -and -not $appProcess.HasExited) {
        Stop-Process -Id $appProcess.Id -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path $tempDirectory) { Remove-Item $tempDirectory -Recurse -Force -ErrorAction SilentlyContinue }

    try {
        if ($originalText) { [System.Windows.Forms.Clipboard]::SetText($originalText) }
        elseif ($originalImage) { [System.Windows.Forms.Clipboard]::SetImage($originalImage) }
        elseif ($originalFiles) { [System.Windows.Forms.Clipboard]::SetFileDropList($originalFiles) }
    }
    catch {
    }
}

Write-Host ''
Write-Host ("功能验收: {0} 通过, {1} 失败" -f $script:passed, $script:failed)

if ($script:failed -gt 0) {
    exit 1
}

