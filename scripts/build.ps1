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
$artifacts = Join-Path $root "work\build"
$output = Join-Path $artifacts "bin\WinForge.App\$($Configuration.ToLowerInvariant())"
$buildArguments = @('-c', $Configuration, '-p:Platform=x64', '--artifacts-path', $artifacts)
if ($Configuration -eq 'Release') {
    $output = Join-Path $root 'dist'
    $buildArguments += "-p:WinForgeAppOutputDirectory=$output"
}
dotnet build (Join-Path $root "src\WinForge.App\WinForge.App.csproj") @buildArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Hazır: $output\WinForge.exe" -ForegroundColor Green

if ($Run) {
    Start-Process -FilePath (Join-Path $output "WinForge.exe") -WorkingDirectory $output
}
