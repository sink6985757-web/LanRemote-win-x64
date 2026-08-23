[CmdletBinding()]
param(
    [string]$OutputDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot "artifacts\LanRemote-v4.4.1-win-x64"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot $OutputDirectory
}

$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts"))
$artifactsPrefix = $artifactsRoot.TrimEnd('\') + [System.IO.Path]::DirectorySeparatorChar
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $OutputDirectory.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Package output must stay inside the project's artifacts directory."
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("LanRemotePublish-" + [guid]::NewGuid().ToString("N"))
$archiveStaging = Join-Path ([System.IO.Path]::GetTempPath()) ("LanRemoteArchive-" + [guid]::NewGuid().ToString("N") + ".zip")
$stagingRoot = [System.IO.Path]::GetFullPath($stagingRoot)
$expectedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
if (-not $stagingRoot.StartsWith($expectedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish staging path escaped the Windows temp directory."
}

function Remove-PlainDirectoryWithRetry {
    param(
        [Parameter(Mandatory)]
        [string]$LiteralPath,
        [int]$MaximumAttempts = 8
    )

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try {
            Remove-Item -LiteralPath $LiteralPath -Recurse -Force -ErrorAction Stop
        }
        catch {
            if ($attempt -ge $MaximumAttempts) {
                throw
            }
        }

        if (-not (Test-Path -LiteralPath $LiteralPath)) {
            return
        }

        if ($attempt -lt $MaximumAttempts) {
            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }

    throw "Unable to replace package output after $MaximumAttempts attempts: $LiteralPath"
}

New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
$previousOutputDirectory = $null
$packageSucceeded = $false
try {
    & dotnet test (Join-Path $projectRoot "LanRemote.sln") `
        --configuration Release `
        --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed; package was not produced."
    }

    & dotnet publish (Join-Path $projectRoot "src\LanRemote.App\LanRemote.App.csproj") `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $stagingRoot `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "Self-contained win-x64 publish failed."
    }

    $serviceStaging = Join-Path $stagingRoot "service"
    & dotnet publish (Join-Path $projectRoot "src\LanRemote.SasService\LanRemote.SasService.csproj") `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $serviceStaging `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "SAS service self-contained win-x64 publish failed."
    }

    if (Test-Path -LiteralPath $OutputDirectory) {
        $outputItem = Get-Item -LiteralPath $OutputDirectory -Force
        if (-not $outputItem.PSIsContainer -or
            $outputItem.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint) -or
            (Get-ChildItem -LiteralPath $OutputDirectory -Recurse -Force -Attributes ReparsePoint |
                Select-Object -First 1)) {
            throw "Refusing to replace a package output that is not a plain directory tree."
        }

        $previousOutputDirectory = $OutputDirectory + ".previous-" + [guid]::NewGuid().ToString("N")
        $previousOutputDirectory = [System.IO.Path]::GetFullPath($previousOutputDirectory)
        if (-not $previousOutputDirectory.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Previous package backup escaped the artifacts directory."
        }

        Move-Item -LiteralPath $OutputDirectory -Destination $previousOutputDirectory
    }

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $stagingRoot "*") -Destination $OutputDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "PACKAGE-README.txt") `
        -Destination (Join-Path $OutputDirectory "README.txt") -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "New-TwoPcEvidence.ps1") `
        -Destination (Join-Path $OutputDirectory "New-TwoPcEvidence.ps1") -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install-sas-service.ps1") `
        -Destination (Join-Path $OutputDirectory "install-sas-service.ps1") -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall-sas-service.ps1") `
        -Destination (Join-Path $OutputDirectory "uninstall-sas-service.ps1") -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "src\LanRemote.App\Assets\LanRemote.App.png") `
        -Destination (Join-Path $OutputDirectory "LanRemote.App.png") -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "src\LanRemote.App\Assets\LanRemote.App.ico") `
        -Destination (Join-Path $OutputDirectory "LanRemote.App.ico") -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot "src\LanRemote.App\Assets\PROVENANCE.md") `
        -Destination (Join-Path $OutputDirectory "ICON-PROVENANCE.md") -Force
    $executable = Join-Path $OutputDirectory "LanRemote.App.exe"
    $hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    $hashTargets = @(
        "LanRemote.App.exe",
        "LanRemote.App.dll",
        "LanRemote.Core.dll",
        "LanRemote.Protocol.dll",
        "LanRemote.Windows.dll",
        "LanRemote.App.png",
        "LanRemote.App.ico",
        "ICON-PROVENANCE.md",
        "service\LanRemote.SasService.exe",
        "service\LanRemote.SasService.dll",
        "install-sas-service.ps1",
        "uninstall-sas-service.ps1"
    )
    $hashLines = foreach ($relativePath in $hashTargets) {
        $targetHash = (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $relativePath) -Algorithm SHA256).Hash
        "{0} *{1}" -f $targetHash, $relativePath
    }
    $hashLines | Set-Content -LiteralPath (Join-Path $OutputDirectory "SHA256.txt") -Encoding ascii
    $archive = ([System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\') + ".zip")
    Compress-Archive -Path (Join-Path $OutputDirectory "*") -DestinationPath $archiveStaging
    Copy-Item -LiteralPath $archiveStaging -Destination $archive -Force
    $packageSucceeded = $true
    [pscustomobject]@{
        OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
        Archive         = $archive
        Executable      = $executable
        Sha256          = $hash
    }
}
finally {
    if ($packageSucceeded -and
        -not [string]::IsNullOrWhiteSpace($previousOutputDirectory) -and
        (Test-Path -LiteralPath $previousOutputDirectory)) {
        try {
            Remove-PlainDirectoryWithRetry -LiteralPath $previousOutputDirectory
        }
        catch {
            Write-Warning "New package is complete, but Google Drive kept the recoverable previous directory: $previousOutputDirectory"
        }
    }

    if (Test-Path -LiteralPath $stagingRoot) {
        $resolvedStaging = [System.IO.Path]::GetFullPath($stagingRoot)
        if ($resolvedStaging.StartsWith($expectedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
        }
    }
    if (Test-Path -LiteralPath $archiveStaging) {
        $resolvedArchiveStaging = [System.IO.Path]::GetFullPath($archiveStaging)
        if ($resolvedArchiveStaging.StartsWith($expectedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedArchiveStaging -Force
        }
    }
}
