param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'native/WinForge.Shell'
$output = Join-Path $source "bin/x64/$Configuration"
$developerCommands = @('18', '2022') | ForEach-Object {
    Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/$_/BuildTools/Common7/Tools/VsDevCmd.bat"
} | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$developerCommands) { throw 'Visual Studio C++ Build Tools bulunamadı. Microsoft C++ araçlarını ve Windows SDK kurun.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$common = '/nologo /std:c++20 /EHsc /W4 /WX /MT /permissive- /DUNICODE /D_UNICODE /DNOMINMAX /DWIN32_LEAN_AND_MEAN'
$libraries = 'windowsapp.lib runtimeobject.lib ole32.lib oleaut32.lib user32.lib uuid.lib'
foreach ($target in @(@{ Source = 'ContextMenuTap.cpp'; Name = 'WinForge.ContextMenuTap.dll'; Mode = '/LD' }, @{ Source = 'ContextMenuFixture.cpp'; Name = 'WinForge.ContextMenuFixture.exe'; Mode = '' }, @{ Source = 'FolderDialogFixture.cpp'; Name = 'WinForge.FolderDialogFixture.exe'; Mode = '' }, @{ Source = 'FolderDialogTap.cpp'; Name = 'WinForge.FolderDialogTap.dll'; Mode = '/LD' }, @{ Source = 'ExplorerNavigationFixture.cpp'; Name = 'WinForge.ExplorerNavigationFixture.exe'; Mode = '' }, @{ Source = 'ExplorerNavigationTap.cpp'; Name = 'WinForge.ExplorerNavigationTap.dll'; Mode = '/LD' }, @{ Source = 'ShellTap.cpp'; Name = 'WinForge.ShellTap.dll'; Mode = '/LD' }, @{ Source = 'ShellHost.cpp'; Name = 'WinForge.ShellHost.exe'; Mode = '' }, @{ Source = 'ShellFixture.cpp'; Name = 'WinForge.ShellFixture.exe'; Mode = '' }, @{ Source = 'TaskbarGeometryFixture.cpp'; Name = 'WinForge.TaskbarGeometryFixture.exe'; Mode = '' }, @{ Source = 'FolderSizeHost.cpp'; Name = 'WinForge.FolderSizeHost.exe'; Mode = '' }, @{ Source = 'FolderSizeTap.cpp'; Name = 'WinForge.FolderSizeTap.dll'; Mode = '/LD' }, @{ Source = 'FolderSizeFixture.cpp'; Name = 'WinForge.FolderSizeFixture.exe'; Mode = '' })) {
    $sourceFile = Join-Path $source $target.Source
    $outputFile = Join-Path $output $target.Name
    $objectFile = Join-Path $output ([IO.Path]::ChangeExtension($target.Source, '.obj'))
    $inputs = @(Get-Item -LiteralPath $sourceFile, (Join-Path $source 'BridgeProtocol.h'))
    if ($target.Source -in @('ShellTap.cpp', 'TaskbarGeometryFixture.cpp')) { $inputs += Get-Item -LiteralPath (Join-Path $source 'TaskbarGeometry.h') }
    if ($target.Source -in @('ContextMenuTap.cpp', 'ContextMenuFixture.cpp')) { $inputs += Get-Item -LiteralPath (Join-Path $source 'ContextMenuEngine.h') }
    if ($target.Source -eq 'ShellTap.cpp') { $inputs += Get-Item -LiteralPath (Join-Path $source 'TaskbarDockAnimation.h') }
    if ($target.Source -in @('FolderSizeHost.cpp', 'FolderSizeTap.cpp')) { $inputs += Get-ChildItem -LiteralPath $source -Filter 'FolderSize*.h' }
    if ($target.Mode -eq '/LD') { $inputs += Get-Item -LiteralPath (Join-Path $source ([IO.Path]::ChangeExtension($target.Source, '.def'))) }
    if ($target.Source -in @('ShellFixture.cpp', 'FolderSizeFixture.cpp', 'ExplorerNavigationFixture.cpp')) { $inputs += Get-Item -LiteralPath (Join-Path $source 'ShellFixture.manifest') }
    if ((Test-Path -LiteralPath $outputFile) -and !($inputs | Where-Object { $_.LastWriteTimeUtc -gt (Get-Item -LiteralPath $outputFile).LastWriteTimeUtc })) { continue }
    $exports = if ($target.Mode -eq '/LD') { ' /DEF:"' + (Join-Path $source ([IO.Path]::ChangeExtension($target.Source, '.def'))) + '"' } else { '' }
    if ($target.Source -in @('ShellFixture.cpp', 'FolderSizeFixture.cpp', 'ExplorerNavigationFixture.cpp')) { $exports += ' /MANIFEST:EMBED /MANIFESTINPUT:"' + (Join-Path $source 'ShellFixture.manifest') + '"' }
    $targetLibraries = $libraries
    if ($target.Source -in @('ExplorerNavigationTap.cpp', 'ExplorerNavigationFixture.cpp')) { $targetLibraries += ' comctl32.lib gdi32.lib advapi32.lib' }
    if ($target.Source -in @('FolderDialogTap.cpp', 'FolderDialogFixture.cpp')) { $targetLibraries += ' comctl32.lib shell32.lib advapi32.lib' }
    if ($target.Source -in @('ContextMenuTap.cpp', 'ContextMenuFixture.cpp')) { $targetLibraries += ' gdi32.lib advapi32.lib shell32.lib' }
    if ($target.Source -eq 'FolderSizeHost.cpp') { $targetLibraries += ' dbghelp.lib winhttp.lib' }
    if ($target.Source -eq 'FolderSizeTap.cpp') { $targetLibraries += ' shell32.lib shlwapi.lib' }
    if ($target.Source -eq 'FolderSizeFixture.cpp') { $targetLibraries += ' shell32.lib' }
    $batchFile = Join-Path $output 'compile.cmd'
    [IO.File]::WriteAllLines($batchFile, @(
        '@echo off',
        ('call "' + $developerCommands + '" -arch=x64 -host_arch=x64 >nul'),
        'if errorlevel 1 exit /b %errorlevel%',
        ('cl ' + $common + ' ' + $target.Mode + ' "' + $sourceFile + '" /Fo"' + $objectFile + '" /Fe"' + $outputFile + '" /link ' + $targetLibraries + $exports),
        'exit /b %errorlevel%'
    ))
    & $env:ComSpec /d /c "call `"$batchFile`""
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Write-Output "WinForge native shell: $output"
