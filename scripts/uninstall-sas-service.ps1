[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$serviceName = "LanRemoteSas"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "請用滑鼠右鍵選擇『以 PowerShell 系統管理員身分執行』後，再執行這個腳本。"
}

$existing = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
if ($null -eq $existing) {
    Write-Host "找不到 LanRemote SAS 服務；不需要移除。"
    exit 0
}

if ($existing.State -ne "Stopped") {
    & sc.exe stop $serviceName
    if ($LASTEXITCODE -notin 0, 1062) {
        throw "停止 LanRemote SAS 服務失敗，sc.exe exit code：$LASTEXITCODE"
    }
}

& sc.exe delete $serviceName
if ($LASTEXITCODE -ne 0) {
    throw "移除 LanRemote SAS 服務失敗，sc.exe exit code：$LASTEXITCODE"
}

Write-Host "LanRemote SAS 服務已從 Windows 服務清單移除。" -ForegroundColor Green
Write-Host "程式檔案與 Windows 安全性原則沒有被刪除或修改。"
