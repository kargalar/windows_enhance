param(
    [string]$Configuration = "Release",
    [switch]$NativeSmoke,
    [switch]$LiveShell,
    [switch]$LiveBrowser,
    [switch]$LiveAudio,
    [switch]$LiveNotifications
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "tests\WinForge.RegressionTests\WinForge.RegressionTests.csproj"

$testArguments = @()
if ($NativeSmoke) { $testArguments += "--native-smoke" }
dotnet build $project -c $Configuration -p:Platform=x64 -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$executable = Join-Path $root "tests\WinForge.RegressionTests\bin\x64\$Configuration\net10.0-windows10.0.19041.0\WinForge.RegressionTests.exe"
& $executable @testArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($NativeSmoke) {
    & $executable --shell-only
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & (Join-Path $root "native\WinForge.Shell\bin\x64\$Configuration\WinForge.TaskbarGeometryFixture.exe")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
foreach ($test in @(@{ Enabled = $LiveShell; Flag = '--shell-live' }, @{ Enabled = $LiveBrowser; Flag = '--browser-live' }, @{ Enabled = $LiveAudio; Flag = '--audio-live' }, @{ Enabled = $LiveNotifications; Flag = '--notification-live' })) {
    if (!$test.Enabled) { continue }
    & $executable $test.Flag
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
