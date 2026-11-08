# 构建 YBH 虚拟声卡驱动（需要 WDK + MSVC）
#
# 用法：
#   .\build.ps1                  # Release / x64
#   .\build.ps1 -Configuration Debug -Platform x64
#
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

function Require-Path([string]$Path, [string]$Name) {
    if (-not (Test-Path $Path)) {
        throw "找不到 $Name：$Path"
    }
}

# 1. 检查 WDK
$kits = "C:\Program Files (x86)\Windows Kits\10"
Require-Path "$kits\Include" "Windows Kits 10"

$kmHeaders = Get-ChildItem "$kits\Include" -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName "km") } |
    Select-Object -First 1
if (-not $kmHeaders) {
    throw "Windows Kits 10 里没有 km 头文件，请安装 Windows Driver Kit (WDK)。"
}
Write-Host "WDK headers: $($kmHeaders.FullName)"

# 2. 检查 MSVC 工具链
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    throw "找不到 vswhere，请安装 Visual Studio 2022。"
}
$vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsPath) {
    throw "Visual Studio 未安装「使用 C++ 的桌面开发」工作负载。"
}
Write-Host "Visual Studio: $vsPath"

# 3. 定位 MSBuild
$msbuild = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
Require-Path $msbuild "MSBuild"

# 4. 编译
$project = Join-Path $PSScriptRoot "YbhVirtualAudio\YbhVirtualAudio.vcxproj"
if (-not (Test-Path $project)) {
    Write-Host ""
    Write-Host "还没有 vcxproj。" -ForegroundColor Yellow
    Write-Host "请先按 driver/README.md 的「实现路线」，从 MSVAD / SYSVAD 示例复制工程，"
    Write-Host "把 ringbuffer.h 与端点名替换进去，再用 Visual Studio 生成驱动工程。"
    exit 2
}

& $msbuild $project /p:Configuration=$Configuration /p:Platform=$Platform /m
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码 $LASTEXITCODE"
}

Write-Host ""
Write-Host "编译完成。产物在本目录的 $Platform\$Configuration 下。" -ForegroundColor Green
Write-Host "下一步：以管理员身份运行 .\install-test.ps1"
