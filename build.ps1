[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Install,
    [switch]$Clean,
    [string]$Csc
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$srcDir = Join-Path $root 'src'
$assetsDir = Join-Path $root 'assets'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'

function Find-Csc {
    # 1) 显式参数；2) 环境变量；3) 常见安装位置；4) vswhere 兜底。
    if ($Csc) {
        if (-not (Test-Path $Csc)) {
            throw "指定的 csc.exe 不存在：$Csc"
        }

        return (Get-Item $Csc).FullName
    }

    if ($env:CTRLCPULSE_CSC -and (Test-Path $env:CTRLCPULSE_CSC)) {
        return (Get-Item $env:CTRLCPULSE_CSC).FullName
    }

    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\*\MSBuild\Current\Bin\Roslyn\csc.exe'
    )

    foreach ($candidate in $candidates) {
        $hit = Get-Item $candidate -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($hit) {
            return $hit.FullName
        }
    }

    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -products * -find 'MSBuild\**\Bin\Roslyn\csc.exe' 2>$null
        if ($found) {
            return ($found | Select-Object -First 1)
        }
    }

    throw '未找到 Roslyn csc.exe。请安装 Visual Studio Build Tools（含 .NET 桌面开发组件），或用 -Csc 指定路径。'
}

function Invoke-Compile {
    param(
        [Parameter(Mandatory = $true)][string]$Csc,
        [Parameter(Mandatory = $true)][string]$Out,
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string[]]$Sources,
        [string]$Icon,
        [string]$Manifest,
        [string]$Resource
    )

    $references = @(
        (Join-Path $framework 'System.dll'),
        (Join-Path $framework 'System.Core.dll'),
        (Join-Path $framework 'System.Drawing.dll'),
        (Join-Path $framework 'System.Windows.Forms.dll')
    )

    $arguments = @(
        '/nologo',
        '/noconfig',
        "/target:$Target",
        '/platform:anycpu',
        '/optimize+',
        '/langversion:latest',
        "/out:$Out"
    )

    foreach ($reference in $references) {
        $arguments += "/r:$reference"
    }

    if ($Icon) {
        $arguments += "/win32icon:$Icon"
    }

    if ($Manifest) {
        $arguments += "/win32manifest:$Manifest"
    }

    if ($Resource) {
        $arguments += "/resource:$Resource"
    }

    $arguments += $Sources

    & $Csc @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "编译失败：$Out (exit code $LASTEXITCODE)"
    }
}

if ($Clean -and (Test-Path $dist)) {
    Remove-Item -Path $dist -Recurse -Force
}

if (-not (Test-Path $dist)) {
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
}

$csc = Find-Csc
$icon = Join-Path $assetsDir 'app.ico'
$manifest = Join-Path $assetsDir 'app.manifest'
$iconScript = Join-Path $root 'tools\make-icon.ps1'

$needIcon = -not (Test-Path $icon)
if (-not $needIcon) {
    $needIcon = (Get-Item $icon).LastWriteTime -lt (Get-Item $iconScript).LastWriteTime
}

if ($needIcon) {
    & $iconScript -Out $icon
}

$sources = @(Get-ChildItem -Path (Join-Path $srcDir '*.cs') | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) {
    throw 'src 目录下没有找到 .cs 源文件。'
}

$exe = Join-Path $dist 'CtrlCPulse.exe'

Invoke-Compile `
    -Csc $csc `
    -Out $exe `
    -Target 'winexe' `
    -Sources $sources `
    -Icon $icon `
    -Manifest $manifest `
    -Resource "$icon,CtrlCPulse.app.ico"

$sizeKb = (Get-Item $exe).Length / 1KB
Write-Host ("已生成 {0} ({1:N0} KB)" -f $exe, $sizeKb)

if ($Test) {
    $testSources = @($sources | Where-Object { $_ -notlike '*\Program.cs' })
    $testSources += (Join-Path $root 'tests\TestMain.cs')
    $testExe = Join-Path $dist 'CtrlCPulse.SelfTest.exe'

    Invoke-Compile -Csc $csc -Out $testExe -Target 'exe' -Sources $testSources

    & $testExe
    if ($LASTEXITCODE -ne 0) {
        throw "自检未通过 (exit code $LASTEXITCODE)"
    }
}

if ($Install) {
    & (Join-Path $root 'install.ps1')
}

Write-Host '构建完成。'
