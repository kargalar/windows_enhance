param([string]$Archive)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$config = Get-Content (Join-Path $root 'src/WinForge.Modules.FileTools/Resources/search-model-config.json') -Raw | ConvertFrom-Json
$destination = Join-Path $root 'src/WinForge.Modules.FileTools/Resources/models'
New-Item -ItemType Directory -Force $destination | Out-Null
function Test-Models {
    foreach ($entry in $config.files.PSObject.Properties) {
        $file = Join-Path $destination $entry.Name
        if (!(Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -ne $entry.Value.bytes -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value.sha256) { return $false }
    }
    return $true
}
if (Test-Models) { Write-Host 'MobileCLIP models verified.'; return }
if (!$Archive) {
    $cache = Join-Path $root 'artifacts/model-downloads'
    New-Item -ItemType Directory -Force $cache | Out-Null
    $Archive = Join-Path $cache 'MobileCLIP-S2.zip'
    if (!(Test-Path -LiteralPath $Archive)) {
        & curl.exe --fail --location --proto '=https' --proto-redir '=https' --retry 3 --output $Archive $config.archive.url
        if ($LASTEXITCODE -ne 0) { throw 'Model download failed.' }
    }
}
if ((Get-Item -LiteralPath $Archive).Length -ne $config.archive.bytes -or (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $config.archive.sha256) { throw 'Model archive hash or size differs.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bundle = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Archive).Path)
try {
    if ($bundle.Entries.Count -ne @($config.files.PSObject.Properties).Count) { throw 'Unexpected model archive entries.' }
    $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $bundle.Entries) {
        if ($entry.FullName -ne $entry.Name -or $null -eq $config.files.PSObject.Properties[$entry.Name] -or !$names.Add($entry.Name)) { throw 'Unexpected model path.' }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination $entry.Name), $true)
    }
} finally { $bundle.Dispose() }
if (!(Test-Models)) { throw 'Extracted model verification failed.' }
