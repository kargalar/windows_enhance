param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration -Run
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
