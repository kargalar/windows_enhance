param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\WinForge.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0\WinForge.exe"

& (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
