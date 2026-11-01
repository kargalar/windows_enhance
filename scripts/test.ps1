param(
    [string]$Configuration = "Release",
    [switch]$NativeSmoke,
    [switch]$FolderSizesLive,
    [switch]$ExtendedFileTools,
    [switch]$LiveShell,
    [switch]$LiveBrowser,
    [switch]$LiveAudio,
    [switch]$LiveNotifications,
    [switch]$ExplorerNavigationLive
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
dotnet run --project (Join-Path $root 'tests\WinForge.NavigationTests\WinForge.NavigationTests.csproj') -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($ExplorerNavigationLive) {
    dotnet run --project (Join-Path $root 'tests\WinForge.NavigationTests\WinForge.NavigationTests.csproj') -c $Configuration -p:Platform=x64 -- --live
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
& (Join-Path $PSScriptRoot 'prepare-models.ps1')
dotnet run --project (Join-Path $root 'tests\WinForge.CaptureTests\WinForge.CaptureTests.csproj') -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$project = Join-Path $root "tests\WinForge.RegressionTests\WinForge.RegressionTests.csproj"

$testArguments = @()
if ($NativeSmoke) { $testArguments += "--native-smoke" }
if ($NativeSmoke) {
    & (Join-Path $PSScriptRoot 'build-native.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $folderDialogNative = Join-Path $root "native\WinForge.Shell\bin\x64\$Configuration"
    & (Join-Path $folderDialogNative 'WinForge.FolderDialogFixture.exe') (Join-Path $folderDialogNative 'WinForge.FolderDialogTap.dll')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    dotnet run --project (Join-Path $root 'tests\WinForge.NavigationTests\WinForge.NavigationTests.csproj') -c $Configuration -p:Platform=x64 -- --folder-dialog
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
dotnet build $project -c $Configuration -p:Platform=x64 -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$executable = Join-Path $root "tests\WinForge.RegressionTests\bin\x64\$Configuration\net10.0-windows10.0.19041.0\WinForge.RegressionTests.exe"
$fileToolsProject = Join-Path $root 'tests\WinForge.FileToolsTests\WinForge.FileToolsTests.csproj'
dotnet build $fileToolsProject -c $Configuration -p:Platform=x64 -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$fileToolsExecutable = Join-Path $root "tests\WinForge.FileToolsTests\bin\x64\$Configuration\net10.0-windows10.0.19041.0\WinForge.FileToolsTests.exe"
& $fileToolsExecutable
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $fileToolsExecutable --clipboard-only
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $fileToolsExecutable --ocr-only
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($NativeSmoke) {
    & $fileToolsExecutable --native-only --uia
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $fileToolsExecutable --video-only
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if ($ExtendedFileTools) {
    $env:NASHI_VERIFY_DOWNLOAD_TOOLS = '1'
    $env:NASHI_VERIFY_TOOLS_DIRECTORY = Join-Path $root 'artifacts\WinForge-filetools-test\tools'
    try {
        & $fileToolsExecutable
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } finally {
        Remove-Item Env:\NASHI_VERIFY_DOWNLOAD_TOOLS, Env:\NASHI_VERIFY_TOOLS_DIRECTORY -ErrorAction SilentlyContinue
    }
    $env:NASHI_VERIFY_OFFICE_DIRECTORY = Join-Path $root 'artifacts\WinForge-filetools-test\office-tools'
    try {
        & $fileToolsExecutable --office-only
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } finally {
        Remove-Item Env:\NASHI_VERIFY_OFFICE_DIRECTORY -ErrorAction SilentlyContinue
    }
}
& $executable @testArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$folderSizeProject = Join-Path $root 'tests\WinForge.FolderSizeTests\WinForge.FolderSizeTests.csproj'
dotnet build $folderSizeProject -c $Configuration -p:Platform=x64 -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$folderSizeExecutable = Join-Path $root "tests\WinForge.FolderSizeTests\bin\x64\$Configuration\net10.0-windows10.0.19041.0\WinForge.FolderSizeTests.exe"
$folderSizeNative = Join-Path $root "native\WinForge.Shell\bin\x64\$Configuration"
& $folderSizeExecutable "--native=$folderSizeNative"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($FolderSizesLive) {
    & $folderSizeExecutable "--native=$folderSizeNative" '--fixture' '--coexist'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $folderSizeExecutable "--native=$folderSizeNative" '--fixture' '--early-sort'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $extrasModule = Join-Path $root "src\WinForge.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0\modules\WinForge.Modules.Extras.dll"
    & $folderSizeExecutable "--native=$folderSizeNative" "--module=$extrasModule"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if ($NativeSmoke) {
    & $executable --shell-only
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & (Join-Path $folderSizeNative 'WinForge.ExplorerNavigationFixture.exe') (Join-Path $folderSizeNative 'WinForge.ExplorerNavigationTap.dll')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & (Join-Path $root "native\WinForge.Shell\bin\x64\$Configuration\WinForge.TaskbarGeometryFixture.exe")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
foreach ($test in @(@{ Enabled = $LiveShell; Flag = '--shell-live' }, @{ Enabled = $LiveBrowser; Flag = '--browser-live' }, @{ Enabled = $LiveAudio; Flag = '--audio-live' }, @{ Enabled = $LiveNotifications; Flag = '--notification-live' })) {
    if (!$test.Enabled) { continue }
    & $executable $test.Flag
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
