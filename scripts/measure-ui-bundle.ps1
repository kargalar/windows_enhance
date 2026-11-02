param(
    [string]$Directory,
    [string]$BaselineDirectory,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$Directory) { $Directory = Join-Path $root 'src/WinForge.App/wwwroot' }
if (!$OutputPath) { $OutputPath = Join-Path $root 'artifacts/performance/ui-bundle.json' }
function Read-Bundle([string]$BundleDirectory) {
    $resolved = [IO.Path]::GetFullPath($BundleDirectory).TrimEnd('\','/')
    $html = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $resolved 'index.html')
    $assets = @([regex]::Matches($html, '(?:src|href)="(?:\./)?(assets/[^"?]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    if (!$assets.Count) { throw 'No initial bundle assets found.' }
    $initial = @($assets | ForEach-Object {
        $path = [IO.Path]::GetFullPath((Join-Path $resolved $_))
        if (!$path.StartsWith($resolved + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Asset escapes bundle directory.' }
        $file = Get-Item -LiteralPath $path
        [pscustomobject]@{ path = $_; bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    })
    [pscustomobject]@{
        directory = $resolved
        initialJsBytes = ($initial | Where-Object path -Like '*.js' | Measure-Object bytes -Sum).Sum
        initialCssBytes = ($initial | Where-Object path -Like '*.css' | Measure-Object bytes -Sum).Sum
        initialAssets = $initial
        deferredAssets = @(Get-ChildItem -LiteralPath (Join-Path $resolved 'assets') -File | Where-Object { ('assets/' + $_.Name) -notin $assets } | ForEach-Object { [pscustomobject]@{ path = 'assets/' + $_.Name; bytes = $_.Length } })
    }
}
$current = Read-Bundle $Directory
$baseline = $null
$jsReduction = $null
$cssReduction = $null
if ($BaselineDirectory) {
    $baseline = Read-Bundle $BaselineDirectory
    if ($baseline.initialJsBytes) { $jsReduction = 100 * (1 - $current.initialJsBytes / $baseline.initialJsBytes) }
    if ($baseline.initialCssBytes) { $cssReduction = 100 * (1 - $current.initialCssBytes / $baseline.initialCssBytes) }
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null
[pscustomobject]@{ inspectedUtc = [DateTime]::UtcNow.ToString('o'); current = $current; baseline = $baseline; jsReductionPercent = $jsReduction; cssReductionPercent = $cssReduction; note = 'Initial asset byte counts, not startup time or memory measurements.' } |
    ConvertTo-Json -Depth 7 | Set-Content -Encoding UTF8 -LiteralPath $OutputPath
Write-Output ("Bundle report: $OutputPath; JS reduction: $jsReduction%; CSS reduction: $cssReduction%")
