[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("Windows11-controls-Windows10", "Windows10-controls-Windows11")]
    [string]$Direction,

    [Parameter(Mandatory)]
    [ValidateSet("PASS", "FAIL")]
    [string]$Result,

    [Parameter(Mandatory)]
    [string]$PeerComputer,

    [Parameter(Mandatory)]
    [int]$DurationSeconds,

    [Parameter(Mandatory)]
    [double]$ObservedFps,

    [Parameter(Mandatory)]
    [double]$P95InteractionLatencyMs,

    [Parameter(Mandatory)]
    [bool]$PairingCodesMatched,

    [Parameter(Mandatory)]
    [bool]$MousePassed,

    [Parameter(Mandatory)]
    [bool]$KeyboardPassed,

    [Parameter(Mandatory)]
    [bool]$ImmediateDisconnectPassed,

    [Parameter(Mandatory)]
    [bool]$DragDropUploadPassed,

    [Parameter(Mandatory)]
    [bool]$ToolbarUploadPassed,

    [Parameter(Mandatory)]
    [bool]$ToolbarDownloadPassed,

    [Parameter(Mandatory)]
    [bool]$FileClipboardPassed,

    [Parameter(Mandatory)]
    [bool]$ResumePassed,

    [Parameter(Mandatory)]
    [bool]$StatusModesPassed,

    [Parameter(Mandatory)]
    [bool]$CursorPassed,

    [string]$Notes = "",
    [string]$PackageDirectory = "",
    [string]$OutputDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot "LanRemote.App.exe") -PathType Leaf) {
        $PackageDirectory = $PSScriptRoot
    }
    else {
        $PackageDirectory = Join-Path $projectRoot "artifacts\LanRemote-win-x64"
    }
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    if ([System.IO.Path]::GetFullPath($PackageDirectory) -eq [System.IO.Path]::GetFullPath($PSScriptRoot)) {
        $OutputDirectory = Join-Path $PSScriptRoot "evidence"
    }
    else {
        $OutputDirectory = Join-Path $projectRoot "readygate\evidence-inbox"
    }
}

$executable = Join-Path $PackageDirectory "LanRemote.App.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "LanRemote.App.exe was not found at: $executable"
}

$operatingSystem = Get-CimInstance -ClassName Win32_OperatingSystem
$network = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike "127.*" } |
    Select-Object InterfaceAlias, IPAddress, PrefixLength
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$safeComputerName = ($env:COMPUTERNAME -replace "[^A-Za-z0-9_-]", "_")
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputFile = Join-Path $OutputDirectory ("{0}-{1}-{2}.json" -f $timestamp, $safeComputerName, $Direction)

$evidence = [ordered]@{
    schemaVersion = 2
    recordedAt = (Get-Date).ToString("o")
    computerName = $env:COMPUTERNAME
    peerComputer = $PeerComputer
    direction = $Direction
    result = $Result
    operatingSystem = [ordered]@{
        caption = $operatingSystem.Caption
        version = $operatingSystem.Version
        buildNumber = $operatingSystem.BuildNumber
        architecture = $operatingSystem.OSArchitecture
    }
    package = [ordered]@{
        executable = $executable
        sha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    }
    network = @($network)
    observations = [ordered]@{
        durationSeconds = $DurationSeconds
        observedFps = $ObservedFps
        p95InteractionLatencyMs = $P95InteractionLatencyMs
        pairingCodesMatched = $PairingCodesMatched
        mousePassed = $MousePassed
        keyboardPassed = $KeyboardPassed
        immediateDisconnectPassed = $ImmediateDisconnectPassed
        dragDropUploadPassed = $DragDropUploadPassed
        toolbarUploadPassed = $ToolbarUploadPassed
        toolbarDownloadPassed = $ToolbarDownloadPassed
        fileClipboardPassed = $FileClipboardPassed
        resumePassed = $ResumePassed
        statusModesPassed = $StatusModesPassed
        cursorPassed = $CursorPassed
        notes = $Notes
    }
}

$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputFile -Encoding utf8
Write-Output $outputFile
