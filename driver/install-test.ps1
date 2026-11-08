# 以测试签名方式安装 YBH 虚拟声卡（仅用于开发机）
#
# ⚠️ 这会修改系统启动配置并需要重启：
#    bcdedit /set testsigning on
#    部分反作弊（EAC / BattlEye）会因此拒绝启动游戏。
#    如果只是想让软件先跑起来，不需要执行本脚本 —— 引擎会自动回退到系统已有的虚拟声卡。
#
# 用法：管理员 PowerShell 里执行
#   .\install-test.ps1
#
param(
    [switch]$SkipTestSigning
)

$ErrorActionPreference = "Stop"

# 1. 管理员检查
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "请以管理员身份运行 PowerShell。"
}

$here = $PSScriptRoot
$inf = Join-Path $here "YbhVirtualAudio\YbhVirtualAudio.inf"
$sys = Get-ChildItem $here -Recurse -Filter "YbhVirtualAudio.sys" -ErrorAction SilentlyContinue |
    Select-Object -First 1

if (-not (Test-Path $inf)) {
    throw "找不到 $inf。请先按 driver/README.md 完成 INF 并编译驱动。"
}
if (-not $sys) {
    throw "找不到 YbhVirtualAudio.sys，请先运行 .\build.ps1。"
}

# 2. 生成 / 复用测试证书
$certName = "YBH Virtual Audio Test"
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -like "*$certName*" } | Select-Object -First 1
if (-not $cert) {
    Write-Host "创建测试证书：$certName"
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=$certName" `
        -CertStoreLocation "Cert:\LocalMachine\My" `
        -KeyUsage DigitalSignature `
        -NotAfter (Get-Date).AddYears(3)
}
$cerPath = Join-Path $here "YbhTest.cer"
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
Write-Host "证书已导入受信任根：$cerPath"

# 3. 签名 sys + 生成 cat（cat 由 inf2cat 生成，这里只签 sys）
$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "x64" } | Select-Object -First 1
if (-not $signtool) {
    throw "找不到 signtool.exe，请安装 WDK。"
}
& $signtool.FullName sign /fd SHA256 /a /s My /n $certName /t http://timestamp.digicert.com $sys.FullName
if ($LASTEXITCODE -ne 0) {
    throw "签名失败。"
}

# 4. 测试签名模式
if (-not $SkipTestSigning) {
    Write-Host "开启测试签名模式（需要重启才会生效）"
    bcdedit /set testsigning on | Out-Null
}

# 5. 安装
Write-Host "安装驱动：$inf"
pnputil /add-driver $inf /install
if ($LASTEXITCODE -ne 0) {
    throw "pnputil 安装失败，退出码 $LASTEXITCODE"
}

Write-Host ""
Write-Host "完成。请重启电脑，然后在声音设置里检查：" -ForegroundColor Green
Write-Host "  播放设备：YBH Virtual Speaker"
Write-Host "  录音设备：YBH Virtual Mic"
Write-Host ""
Write-Host "卸载：pnputil /delete-driver <oemXX.inf> /uninstall ; bcdedit /set testsigning off"
