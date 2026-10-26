param(
    [string]$Configuration = "Release",
    [switch]$Run
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

Write-Host "WinForge derleniyor ($Configuration)..." -ForegroundColor Cyan
dotnet build (Join-Path $root "WinForge.slnx") -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$output = Join-Path $root "src\WinForge.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0"
Write-Host "Hazır: $output\WinForge.exe" -ForegroundColor Green

if ($Run) {
    Stop-Process -Name WinForge -ErrorAction SilentlyContinue
    Start-Process -FilePath (Join-Path $output "WinForge.exe") -WorkingDirectory $output
}
