param(
    [string]$Configuration = 'Release',
    [ValidateSet('Fast', 'Integration')][string]$Group = 'Fast',
    [switch]$NativeSmoke,
    [switch]$FolderSizesLive,
    [switch]$ExtendedFileTools,
    [switch]$LiveShell,
    [switch]$LiveBrowser,
    [switch]$LiveAudio,
    [switch]$LiveNotifications,
    [switch]$ExplorerNavigationLive,
    [switch]$Performance,
    [switch]$InteractiveInput,
    [switch]$List
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -Raw (Join-Path $PSScriptRoot 'test-groups.json') | ConvertFrom-Json
$known = @($manifest.fast) + @($manifest.integration)
$discovered = @(Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.csproj' -Recurse | ForEach-Object BaseName)
$differences = @(Compare-Object ($known | Sort-Object) ($discovered | Sort-Object))
if ($differences.Count) { throw ('Test inventory differs from projects: ' + ($differences | Out-String)) }
$integration = $Group -eq 'Integration' -or $NativeSmoke -or $FolderSizesLive -or $ExtendedFileTools -or $LiveShell -or $LiveBrowser -or $LiveAudio -or $LiveNotifications -or $ExplorerNavigationLive
$interactive = $InteractiveInput -or $integration
foreach ($entry in $manifest.interactive) {
    if ($entry.project -notin $known -or $entry.name -in $known) { throw 'Invalid interactive test group in manifest.' }
}
$results = [Collections.Generic.List[object]]::new()
foreach ($name in $known) {
    $selected = $name -in $manifest.fast -or $integration
    $results.Add([pscustomobject]@{ name = $name; status = $(if ($selected) { 'pending' } else { 'skipped' }); reason = $(if (!$selected) { 'Requires -Group Integration; may use native engines, UI, registry fixtures and local models.' } else { '' }); seconds = 0 })
}
$results.Add([pscustomobject]@{ name = 'UI'; status = 'pending'; reason = ''; seconds = 0 })
$results.Add([pscustomobject]@{ name = 'UI build'; status = 'pending'; reason = ''; seconds = 0 })
foreach ($entry in $manifest.interactive) {
    $results.Add([pscustomobject]@{ name = $entry.name; status = $(if ($interactive) { 'pending' } else { 'skipped' }); reason = $(if (!$interactive) { 'Requires -InteractiveInput or Integration. ' + $entry.requires } else { '' }); seconds = 0 })
}
foreach ($name in @('Performance build', 'History performance', 'Dashboard bridge', 'Dashboard interface')) {
    $results.Add([pscustomobject]@{ name = $name; status = $(if ($Performance) { 'pending' } else { 'skipped' }); reason = $(if (!$Performance) { 'Select -Performance for synthetic archive and real WebView benchmarks.' } else { '' }); seconds = 0 })
}
if ($List) { $results | Format-Table -AutoSize; return }
$reportDirectory = Join-Path $root 'artifacts/test-results'
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
$report = Join-Path $reportDirectory ('run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '.json')
$failure = $null
$script:failureExitCode = 1
function Invoke-Check([string]$Name, [scriptblock]$Command) {
    $entry = $results | Where-Object name -eq $Name
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Write-Host ('RUN: ' + $Name)
    try {
        & $Command
        if ($LASTEXITCODE -ne 0) { $script:failureExitCode = $LASTEXITCODE; throw ($Name + ' exited with ' + $LASTEXITCODE) }
        $entry.status = 'passed'
    } catch { $entry.status = 'failed'; $entry.reason = $_.Exception.Message; throw }
    finally { $entry.seconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3) }
}
try {
    foreach ($name in $manifest.fast) {
        $project = Join-Path $root ('tests/' + $name + '/' + $name + '.csproj')
        $testArguments = @($manifest.fastArguments.PSObject.Properties[$name].Value | Where-Object { $_ })
        Invoke-Check $name {
            if ($testArguments.Count) { dotnet run --project $project -c $Configuration -p:Platform=x64 -- @testArguments }
            else { dotnet run --project $project -c $Configuration -p:Platform=x64 }
        }
    }
    Push-Location (Join-Path $root 'src/WinForge.App/ui')
    try {
        # npm ci checks the lock file even when an older node_modules directory exists.
        npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { $script:failureExitCode = $LASTEXITCODE; throw 'npm ci failed.' }
        Invoke-Check 'UI' { npm test }
        Invoke-Check 'UI build' { npm run build }
    } finally { Pop-Location }
    if ($interactive) {
        foreach ($entry in $manifest.interactive) {
            $project = Join-Path $root ('tests/' + $entry.project + '/' + $entry.project + '.csproj')
            $testArguments = @($entry.arguments)
            Invoke-Check $entry.name { dotnet run --project $project -c $Configuration -p:Platform=x64 -- @testArguments }
        }
    }
    if ($Performance) {
        $performanceBuild = Join-Path $root 'artifacts/optimization-build'
        Invoke-Check 'Performance build' {
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'prepare-models.ps1')
            if ($LASTEXITCODE -ne 0) { throw 'Performance model preparation failed.' }
            dotnet build (Join-Path $root 'tests/WinForge.CaptureTests') -c $Configuration -p:Platform=x64 --artifacts-path $performanceBuild -nologo
            if ($LASTEXITCODE -ne 0) { throw 'History benchmark build failed.' }
            dotnet build (Join-Path $root 'tests/WinForge.RegressionTests') -c $Configuration -p:Platform=x64 --artifacts-path $performanceBuild -nologo
        }
        $pivot = $Configuration.ToLowerInvariant()
        Invoke-Check 'History performance' {
            & (Join-Path $performanceBuild "bin/WinForge.CaptureTests/$pivot/WinForge.CaptureTests.exe") --history-benchmark (Join-Path $root 'artifacts/performance/history-baseline.json')
        }
        Push-Location $root
        try {
            Invoke-Check 'Dashboard bridge' {
                & (Join-Path $performanceBuild "bin/WinForge.RegressionTests/$pivot/WinForge.RegressionTests.exe") --bridge-only
            }
            Invoke-Check 'Dashboard interface' {
                & (Join-Path $performanceBuild "bin/WinForge.RegressionTests/$pivot/WinForge.RegressionTests.exe") --dashboard-interface-only
            }
        } finally { Pop-Location }
    }
    if ($integration) {
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'test-integration.ps1'), '-Configuration', $Configuration)
        foreach ($option in @('NativeSmoke','FolderSizesLive','ExtendedFileTools','LiveShell','LiveBrowser','LiveAudio','LiveNotifications','ExplorerNavigationLive')) {
            if (Get-Variable -Name $option -ValueOnly) { $arguments += ('-' + $option) }
        }
        # Isolate legacy exit statements and retain the first native failure code.
        & powershell.exe @arguments | Tee-Object -Variable integrationOutput
        $integrationExit = $LASTEXITCODE
        foreach ($line in $integrationOutput) {
            if ($line -match '^PASS-GROUP:(.+)$') {
                $entry = $results | Where-Object name -eq $Matches[1]
                if ($entry) { $entry.status = 'passed'; $entry.seconds = $null }
            }
        }
        if ($integrationExit -ne 0) { $script:failureExitCode = $integrationExit; throw ('Integration suite exited with ' + $integrationExit + '; see the last completed group and command output.') }
        $missing = @($results | Where-Object { $_.name -in $manifest.integration -and $_.status -ne 'passed' })
        if ($missing.Count) { throw ('Integration exited without reporting all groups: ' + (($missing | ForEach-Object name) -join ', ')) }
    }
} catch { $failure = $_; Write-Warning $_.Exception.Message }
finally {
    foreach ($entry in $results | Where-Object status -eq 'pending') {
        $entry.status = 'not-run'
        $entry.reason = 'Not completed; stopped after an earlier failure.'
    }
    [pscustomobject]@{ createdUtc = [DateTime]::UtcNow.ToString('o'); configuration = $Configuration; integration = [bool]$integration; interactiveInput = [bool]$interactive; performance = [bool]$Performance; results = @($results.ToArray()); error = $(if ($failure) { $failure.Exception.Message } else { $null }) } | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath $report
    $results | Format-Table -AutoSize
    Write-Host ('Report: ' + $report)
}
if ($failure) { exit $script:failureExitCode }
