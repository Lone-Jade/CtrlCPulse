[CmdletBinding()]
param(
    [switch]$NoStart,
    [string]$InstallDirectory
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$source = Join-Path $root 'dist\CtrlCPulse.exe'

# 默认装在仓库同级目录（本机即 E:\XIWEI++\CtrlCPulse），不占用系统盘。
if (-not $InstallDirectory) {
    $InstallDirectory = Join-Path (Split-Path -Parent $root) 'CtrlCPulse'
}

if (-not (Test-Path $source)) {
    Write-Host '未找到构建产物，先执行 build.ps1 ...'
    & (Join-Path $root 'build.ps1')
}

if (-not (Test-Path $source)) {
    throw '构建失败：dist\CtrlCPulse.exe 不存在'
}

$targetDirectory = $InstallDirectory
New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
$target = Join-Path $targetDirectory 'CtrlCPulse.exe'

Get-Process -Name 'CtrlCPulse' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

Copy-Item -Path $source -Destination $target -Force

# 开机自启：优先写当前用户 Run 项；失败则回退到启动文件夹快捷方式。
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$autostartMode = $null

try {
    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -Path $runKey -Name 'CtrlCPulse' `
        -Value ('"' + $target + '" --tray') -PropertyType String -Force | Out-Null
    $autostartMode = '注册表 Run 项（当前用户）'
}
catch {
    $startupDirectory = [Environment]::GetFolderPath('Startup')
    $shortcutPath = Join-Path $startupDirectory 'CtrlCPulse.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $target
    $shortcut.Arguments = '--tray'
    $shortcut.WorkingDirectory = $targetDirectory
    $shortcut.Description = 'CtrlCPulse 复制提示'
    $shortcut.Save()
    $autostartMode = '启动文件夹快捷方式（注册表写入失败时的回退）'
}

$started = '未启动（-NoStart）'
if (-not $NoStart) {
    Start-Process -FilePath $target -ArgumentList '--tray'
    Start-Sleep -Milliseconds 800
    $running = Get-Process -Name 'CtrlCPulse' -ErrorAction SilentlyContinue
    $started = if ($running) { '已启动，托盘区可见' } else { '启动失败，请检查 error.log' }
}

Write-Host ''
Write-Host 'CtrlCPulse 安装完成' -ForegroundColor Green
Write-Host ('  程序位置 : {0}' -f $target)
Write-Host ('  开机自启 : {0}' -f $autostartMode)
Write-Host ('  运行状态 : {0}' -f $started)
Write-Host ''
Write-Host '如果任务栏右下角没看到图标：点击 ^ 展开隐藏图标，把 CtrlCPulse 拖到常驻区域。'
Write-Host '卸载：powershell -ExecutionPolicy Bypass -File .\uninstall.ps1'
