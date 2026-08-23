[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$serviceName = "LanRemoteSas"
$serviceExecutable = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "service\LanRemote.SasService.exe"))

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "請用滑鼠右鍵選擇『以 PowerShell 系統管理員身分執行』後，再執行這個腳本。"
}

if (-not (Test-Path -LiteralPath $serviceExecutable -PathType Leaf)) {
    throw "找不到 SAS 服務：$serviceExecutable。請保留完整解壓縮資料夾。"
}

$existing = Get-CimInstance Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    $registeredPath = ([string]$existing.PathName).Trim().Trim('"')
    if (-not [string]::Equals(
        [System.IO.Path]::GetFullPath($registeredPath),
        $serviceExecutable,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "已存在同名服務，但路徑不同：$registeredPath。為避免覆寫其他安裝，本腳本已停止。"
    }
}
else {
    & sc.exe create $serviceName `
        binPath= ('"{0}"' -f $serviceExecutable) `
        start= auto `
        obj= LocalSystem `
        DisplayName= "LanRemote Secure Attention Service"
    if ($LASTEXITCODE -ne 0) {
        throw "建立 LanRemote SAS 服務失敗，sc.exe exit code：$LASTEXITCODE"
    }

    & sc.exe description $serviceName "Fixed-purpose Ctrl+Alt+Delete helper for LanRemote."
    if ($LASTEXITCODE -ne 0) {
        throw "設定 LanRemote SAS 服務說明失敗，sc.exe exit code：$LASTEXITCODE"
    }
}

& sc.exe config $serviceName start= auto
if ($LASTEXITCODE -ne 0) {
    throw "設定 LanRemote SAS 服務為自動啟動失敗，sc.exe exit code：$LASTEXITCODE"
}

& sc.exe start $serviceName
if ($LASTEXITCODE -notin 0, 1056) {
    throw "啟動 LanRemote SAS 服務失敗，sc.exe exit code：$LASTEXITCODE"
}

Write-Host "LanRemote SAS 服務已安裝並啟動。" -ForegroundColor Green
Write-Host "本腳本沒有修改 Windows 安全性原則。"
Write-Host "若 Ctrl+Alt+Delete 沒有切換安全桌面，請由你本人開啟本機群組原則："
Write-Host "Computer Configuration > Administrative Templates > Windows Components > Windows Logon Options"
Write-Host "將 'Disable or enable software Secure Attention Sequence' 設為允許 Services。"
