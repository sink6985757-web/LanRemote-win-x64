[CmdletBinding()]
param(
    [string]$OutputDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot "artifacts\LanRemote-win-x64"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $projectRoot $OutputDirectory
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("LanRemotePublish-" + [guid]::NewGuid().ToString("N"))
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

    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $stagingRoot "*") -Destination $OutputDirectory -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "PACKAGE-README.txt") `
        -Destination (Join-Path $OutputDirectory "README.txt") -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "New-TwoPcEvidence.ps1") `
        -Destination (Join-Path $OutputDirectory "New-TwoPcEvidence.ps1") -Force
    $executable = Join-Path $OutputDirectory "LanRemote.App.exe"
    $hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    ("{0} *LanRemote.App.exe" -f $hash) | Set-Content `
        -LiteralPath (Join-Path $OutputDirectory "SHA256.txt") -Encoding ascii
    $archive = ([System.IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\') + ".zip")
    Compress-Archive -Path (Join-Path $OutputDirectory "*") -DestinationPath $archive -Force
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
}
