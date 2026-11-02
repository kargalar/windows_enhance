param(
    [string]$Directory,
    [string]$BuildDirectory,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Directory) { $Directory = Join-Path $root 'artifacts/publish-audit' }
if (!$BuildDirectory) { $BuildDirectory = Join-Path $root 'artifacts/publish-build/bin/WinForge.App/release' }
if (!$OutputPath) { $OutputPath = Join-Path $root 'artifacts/test-results/publish-audit.json' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null
try {
$Directory = (Resolve-Path -LiteralPath $Directory).Path.TrimEnd('\','/')
$BuildDirectory = (Resolve-Path -LiteralPath $BuildDirectory).Path.TrimEnd('\','/')
$required = @('WinForge.exe','WinForge.dll','WinForge.Core.dll','WinForge.History.dll','WinForge.runtimeconfig.json','WinForge.deps.json','wwwroot/index.html','dictation/cpu/WinForge.Whisper.dll')
$required += @('ShellHost.exe','ShellTap.dll','ContextMenuTap.dll','ExplorerNavigationTap.dll','FolderDialogTap.dll','FolderSizeHost.exe','FolderSizeTap.dll') | ForEach-Object { 'native/WinForge.' + $_ }
$required += @('Taskbar','AlwaysOnTop','TextGrabber','NetSpeed','Tiling','Extras','WindowControl','FileTools','Capture') | ForEach-Object { 'modules/WinForge.Modules.' + $_ + '.dll' }
$html = Get-Content -LiteralPath (Join-Path $Directory 'wwwroot/index.html') -Raw -Encoding UTF8
$assets = @([regex]::Matches($html, '(?:src|href)="(?:\./)?(assets/[^"?]+)"') | ForEach-Object { 'wwwroot/' + $_.Groups[1].Value })
if (!$assets.Count) { throw 'Published HTML does not reference an initial UI bundle.' }
$required += $assets
$sourceUi = Join-Path $root 'src/WinForge.App/wwwroot'
$sourceUiFiles = @(Get-ChildItem -LiteralPath $sourceUi -File -Recurse | ForEach-Object { $_.FullName.Substring($sourceUi.Length + 1).Replace('\','/') } | Sort-Object)
$packageUiRoot = Join-Path $Directory 'wwwroot'
$packageUiFiles = @(Get-ChildItem -LiteralPath $packageUiRoot -File -Recurse | ForEach-Object { $_.FullName.Substring($packageUiRoot.Length + 1).Replace('\','/') } | Sort-Object)
if (@(Compare-Object $sourceUiFiles $packageUiFiles).Count) { throw 'Published UI contains missing or stale files relative to the current production bundle.' }
$required += $sourceUiFiles | ForEach-Object { 'wwwroot/' + $_ }
$required += @(Get-ChildItem -LiteralPath (Join-Path $Directory 'wwwroot/assets') -File | ForEach-Object { 'wwwroot/assets/' + $_.Name })
$files = @($required | Sort-Object -Unique | ForEach-Object {
    $relative = $_
    $path = [IO.Path]::GetFullPath((Join-Path $Directory $relative))
    if (!$path.StartsWith($Directory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish path escapes the package.' }
    $file = Get-Item -LiteralPath $path
    if (!$file.Length) { throw "Empty required package file: $relative" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $built = Join-Path $BuildDirectory $relative
    if (!(Test-Path -LiteralPath $built) -or (Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash -ne $hash) { throw "Published file differs from its built artifact: $relative" }
    [pscustomobject]@{ path = $relative; bytes = $file.Length; sha256 = $hash }
})
foreach ($relative in @('WinForge.exe','dictation/cpu/WinForge.Whisper.dll') + @($required | Where-Object { $_ -like 'native/*' })) {
    $stream = [IO.File]::OpenRead((Join-Path $Directory $relative))
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid executable header: $relative" }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Expected x64 PE binary: $relative" }
    } finally { $reader.Dispose() }
}
# userdata contains distributable offline models, never the developer's live settings/history.
$sourceResources = Join-Path $root 'src/WinForge.Modules.FileTools/Resources'
$modelFiles = @(Get-ChildItem -LiteralPath (Join-Path $Directory 'userdata') -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring((Join-Path $Directory 'userdata').Length + 1).Replace('\','/')
    if ($relative -eq 'models/search-model-config.json') { $source = Join-Path $sourceResources 'search-model-config.json' }
    elseif ($relative -like 'models/*') { $source = Join-Path $sourceResources $relative }
    elseif ($relative -match '^tessdata/(eng|tur)\.traineddata\.gz$') { $source = Join-Path $sourceResources ('tessdata/archives/' + [IO.Path]::GetFileName($relative)) }
    else { throw "Unexpected user data in distributable package: $relative" }
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    if (!(Test-Path -LiteralPath $source) -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $hash) { throw "Offline resource does not match source: $relative" }
    [pscustomobject]@{ path = 'userdata/' + $relative; bytes = $_.Length; sha256 = $hash }
})
foreach ($name in @('models/mobileclip-s2-image.onnx','models/mobileclip-s2-text.onnx','models/clip-vocab.json','models/clip-merges.txt','models/model-source.json','models/LICENSE-Apple-code','models/LICENSE-MobileCLIP-S2','models/ACKNOWLEDGEMENTS-MobileCLIP','models/search-model-config.json','tessdata/eng.traineddata.gz','tessdata/tur.traineddata.gz')) {
    if ('userdata/' + $name -notin $modelFiles.path) { throw "Missing offline model/license: $name" }
}
$privateFiles = @(Get-ChildItem -LiteralPath $Directory -Recurse -File | Where-Object { $_.Name -match '^(settings|appearance|diagnostics)\.json$|\.(db|sqlite|log)$' })
if ($privateFiles.Count) { throw 'Package contains live settings, diagnostics, logs or databases.' }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null
[pscustomobject]@{ utc = [DateTime]::UtcNow.ToString('o'); status = 'passed'; directory = $Directory; files = $files; offlineResources = $modelFiles; checks = @('Required managed/native/UI files match build hashes','Native entry points are x64 PE files','Offline model and license files match source','Live settings/history/logs are absent'); scope = 'Artifact content verification on this build machine; does not prove clean-machine startup, CUDA execution or rollback.' } | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output "PASS: Published package content, x64 entry points and $($modelFiles.Count) offline resources verified. Report: $OutputPath"
} catch {
    [pscustomobject]@{ utc = [DateTime]::UtcNow.ToString('o'); status = 'failed'; directory = $Directory; error = $_.Exception.Message } |
        ConvertTo-Json | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    throw
}
