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
$linkOptions = ''
if ($Configuration -eq 'Release') {
    $common += ' /O2 /DNDEBUG'
    $linkOptions = ' /OPT:REF /OPT:ICF'
} elseif ($Configuration -eq 'Debug') {
    $common = $common.Replace(' /MT ', ' /MTd ')
    $common += ' /Od /Z7 /D_DEBUG'
    $linkOptions = ' /DEBUG'
} else { throw 'Configuration must be Release or Debug.' }
# Include the recipe and toolchain, not only source timestamps.
$visualStudioRoot = Split-Path (Split-Path (Split-Path $developerCommands -Parent) -Parent) -Parent
$toolchainFiles = @(Get-Item -LiteralPath $developerCommands, $PSCommandPath)
$toolchainFiles += @(Get-ChildItem -Path (Join-Path $visualStudioRoot 'VC/Auxiliary/Build/*.txt') -File -ErrorAction SilentlyContinue)
$toolchainFiles += @(Get-ChildItem -Path (Join-Path $visualStudioRoot 'VC/Tools/MSVC/*/bin/Hostx64/x64/cl.exe') -File -ErrorAction SilentlyContinue)
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$toolchainFiles += @(Get-ChildItem -Path (Join-Path $sdkRoot 'bin/*/x64/rc.exe'), (Join-Path $sdkRoot 'Include/*/um/Windows.h') -File -ErrorAction SilentlyContinue)
$toolchainIdentity = ($toolchainFiles | Sort-Object FullName | ForEach-Object {
    $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}) -join '|'
$libraries = 'windowsapp.lib runtimeobject.lib ole32.lib oleaut32.lib user32.lib uuid.lib'
foreach ($target in @(@{ Source = 'NotificationsPlacementTap.cpp'; Name = 'WinForge.NotificationsPlacementTap.dll'; Mode = '/LD' }, @{ Source = 'NotificationsPlacementFixture.cpp'; Name = 'WinForge.NotificationsPlacementFixture.exe'; Mode = '' }, @{ Source = 'ContextMenuTap.cpp'; Name = 'WinForge.ContextMenuTap.dll'; Mode = '/LD' }, @{ Source = 'ContextMenuFixture.cpp'; Name = 'WinForge.ContextMenuFixture.exe'; Mode = '' }, @{ Source = 'FolderDialogFixture.cpp'; Name = 'WinForge.FolderDialogFixture.exe'; Mode = '' }, @{ Source = 'FolderDialogTap.cpp'; Name = 'WinForge.FolderDialogTap.dll'; Mode = '/LD' }, @{ Source = 'ExplorerNavigationFixture.cpp'; Name = 'WinForge.ExplorerNavigationFixture.exe'; Mode = '' }, @{ Source = 'ExplorerNavigationTap.cpp'; Name = 'WinForge.ExplorerNavigationTap.dll'; Mode = '/LD' }, @{ Source = 'ShellTap.cpp'; Name = 'WinForge.ShellTap.dll'; Mode = '/LD' }, @{ Source = 'ShellHost.cpp'; Name = 'WinForge.ShellHost.exe'; Mode = '' }, @{ Source = 'ShellFixture.cpp'; Name = 'WinForge.ShellFixture.exe'; Mode = '' }, @{ Source = 'TaskbarGeometryFixture.cpp'; Name = 'WinForge.TaskbarGeometryFixture.exe'; Mode = '' }, @{ Source = 'FolderSizeHost.cpp'; Name = 'WinForge.FolderSizeHost.exe'; Mode = '' }, @{ Source = 'FolderSizeTap.cpp'; Name = 'WinForge.FolderSizeTap.dll'; Mode = '/LD' }, @{ Source = 'FolderSizeFixture.cpp'; Name = 'WinForge.FolderSizeFixture.exe'; Mode = '' })) {
    $sourceFile = Join-Path $source $target.Source
    $outputFile = Join-Path $output $target.Name
    $objectFile = Join-Path $output ([IO.Path]::ChangeExtension($target.Source, '.obj'))
    $inputs = @(Get-Item -LiteralPath $sourceFile)
    $inputs += @(Get-ChildItem -LiteralPath $source -Filter '*.h' -File)
    if ($target.Source -in @('ShellTap.cpp', 'TaskbarGeometryFixture.cpp')) { $inputs += Get-Item -LiteralPath (Join-Path $source 'TaskbarGeometry.h') }
    if ($target.Source -in @('ContextMenuTap.cpp', 'ContextMenuFixture.cpp')) { $inputs += Get-Item -LiteralPath (Join-Path $source 'ContextMenuEngine.h') }
    if ($target.Source -eq 'ShellTap.cpp') { $inputs += Get-Item -LiteralPath (Join-Path $source 'TaskbarDockAnimation.h'), (Join-Path $source 'ExplorerCommandBar.h'), (Join-Path $source 'ExplorerCommandBarLayout.h') }
    if ($target.Source -in @('FolderSizeHost.cpp', 'FolderSizeTap.cpp')) { $inputs += Get-ChildItem -LiteralPath $source -Filter 'FolderSize*.h' }
    if ($target.Mode -eq '/LD') { $inputs += Get-Item -LiteralPath (Join-Path $source ([IO.Path]::ChangeExtension($target.Source, '.def'))) }
    if ($target.Source -in @('ShellFixture.cpp', 'FolderSizeFixture.cpp', 'ExplorerNavigationFixture.cpp', 'NotificationsPlacementFixture.cpp')) { $inputs += Get-Item -LiteralPath (Join-Path $source 'ShellFixture.manifest') }
    $recipe = $toolchainIdentity + '|' + $Configuration + '|' + $common + '|' + $linkOptions + '|' + (($inputs | Sort-Object FullName -Unique | ForEach-Object {
        $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }) -join '|')
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $fingerprint = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($recipe))).Replace('-', '') } finally { $hasher.Dispose() }
    $stampFile = $outputFile + '.buildhash'
    if ((Test-Path -LiteralPath $outputFile) -and (Test-Path -LiteralPath $stampFile) -and (Get-Content -Raw -LiteralPath $stampFile).Trim() -eq $fingerprint) { continue }
    $exports = if ($target.Mode -eq '/LD') { ' /DEF:"' + (Join-Path $source ([IO.Path]::ChangeExtension($target.Source, '.def'))) + '"' } else { '' }
    if ($target.Source -in @('ShellFixture.cpp', 'FolderSizeFixture.cpp', 'ExplorerNavigationFixture.cpp', 'NotificationsPlacementFixture.cpp')) { $exports += ' /MANIFEST:EMBED /MANIFESTINPUT:"' + (Join-Path $source 'ShellFixture.manifest') + '"' }
    $targetLibraries = $libraries
    if ($target.Source -eq 'ShellTap.cpp') { $targetLibraries += ' comctl32.lib' }
    if ($target.Source -in @('ExplorerNavigationTap.cpp', 'ExplorerNavigationFixture.cpp', 'NotificationsPlacementTap.cpp', 'NotificationsPlacementFixture.cpp')) { $targetLibraries += ' comctl32.lib gdi32.lib advapi32.lib' }
    if ($target.Source -in @('FolderDialogTap.cpp', 'FolderDialogFixture.cpp')) { $targetLibraries += ' comctl32.lib shell32.lib advapi32.lib' }
    if ($target.Source -in @('ContextMenuTap.cpp', 'ContextMenuFixture.cpp')) { $targetLibraries += ' gdi32.lib advapi32.lib shell32.lib comctl32.lib msimg32.lib' }
    if ($target.Source -eq 'FolderSizeHost.cpp') { $targetLibraries += ' dbghelp.lib winhttp.lib' }
    if ($target.Source -eq 'FolderSizeTap.cpp') { $targetLibraries += ' shell32.lib shlwapi.lib propsys.lib' }
    if ($target.Source -eq 'FolderSizeFixture.cpp') { $targetLibraries += ' shell32.lib' }
    $batchFile = Join-Path $output 'compile.cmd'
    [IO.File]::WriteAllLines($batchFile, @(
        '@echo off',
        ('call "' + $developerCommands + '" -arch=x64 -host_arch=x64 >nul'),
        'if errorlevel 1 exit /b %errorlevel%',
        ('cl ' + $common + ' ' + $target.Mode + ' "' + $sourceFile + '" /Fo"' + $objectFile + '" /Fe"' + $outputFile + '" /link ' + $targetLibraries + $exports + $linkOptions),
        'exit /b %errorlevel%'
    ))
    & $env:ComSpec /d /c "call `"$batchFile`""
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Set-Content -LiteralPath $stampFile -Value $fingerprint -Encoding ASCII
}
Write-Output "WinForge native shell: $output"
