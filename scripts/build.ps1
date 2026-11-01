param(
    [string]$Configuration = "Release",
    [switch]$Run
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$runningProcesses = @(Get-Process -Name WinForge -ErrorAction SilentlyContinue)
if ($runningProcesses.Count -gt 0) {
    Write-Host "Acik WinForge kapatiliyor..." -ForegroundColor Yellow
    $runningProcesses | Stop-Process -Force
    foreach ($process in $runningProcesses) {
        $process.WaitForExit()
    }
}

& (Join-Path $PSScriptRoot 'prepare-models.ps1')

Write-Host "WinForge derleniyor ($Configuration)..." -ForegroundColor Cyan
dotnet build (Join-Path $root "src\WinForge.App\WinForge.App.csproj") -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$output = Join-Path $root "src\WinForge.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0"
Write-Host "Hazır: $output\WinForge.exe" -ForegroundColor Green

if ($Run) {
    Start-Process -FilePath (Join-Path $output "WinForge.exe") -WorkingDirectory $output
}
