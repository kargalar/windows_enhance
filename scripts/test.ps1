param(
    [string]$Configuration = "Release",
    [switch]$AltSnapSmoke
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "tests\WinForge.RegressionTests\WinForge.RegressionTests.csproj"

$testArguments = @()
if ($AltSnapSmoke) { $testArguments += "--altsnap-smoke" }
dotnet run --project $project -c $Configuration -- @testArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
