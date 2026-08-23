[CmdletBinding()]
param(
    [string]$OutputDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot "artifacts\LanRemote-v4.1-win-x64"
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

New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
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

        Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
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
    $executable = Join-Path $OutputDirectory "LanRemote.App.exe"
    $hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    $hashTargets = @(
        "LanRemote.App.exe",
        "LanRemote.App.dll",
        "LanRemote.Core.dll",
        "LanRemote.Protocol.dll",
        "LanRemote.Windows.dll",
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
    [pscustomobject]@{
        OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
        Archive         = $archive
        Executable      = $executable
        Sha256          = $hash
    }
}
finally {
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
