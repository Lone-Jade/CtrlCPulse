[CmdletBinding()]
param(
    [switch]$PurgeSettings,
    [string]$InstallDirectory
)

$ErrorActionPreference = 'Continue'

$root = $PSScriptRoot

if (-not $InstallDirectory) {
    $InstallDirectory = Join-Path (Split-Path -Parent $root) 'CtrlCPulse'
}

Get-Process -Name 'CtrlCPulse' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'CtrlCPulse' -ErrorAction SilentlyContinue

$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'CtrlCPulse.lnk'
if (Test-Path $shortcutPath) {
    Remove-Item -Path $shortcutPath -Force
}

if (Test-Path $InstallDirectory) {
    Remove-Item -Path $InstallDirectory -Recurse -Force
}

# 旧版本曾把配置放在 %APPDATA%，这里顺手清掉，避免残留。
$legacySettings = Join-Path $env:APPDATA 'CtrlCPulse'
if ($PurgeSettings -and (Test-Path $legacySettings)) {
    Remove-Item -Path $legacySettings -Recurse -Force
    Write-Host '已一并清除旧版设置与日志。'
}

Write-Host 'CtrlCPulse 已卸载。' -ForegroundColor Green
