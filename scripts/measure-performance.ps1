param(
    [Parameter(Mandatory = $true)][int]$RootProcessId,
    [ValidateRange(1,28800)][int]$DurationSeconds = 60,
    [ValidateRange(100,10000)][int]$IntervalMilliseconds = 1000,
    [string]$Profile = 'current-session',
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$OutputPath) { $OutputPath = Join-Path $root ('artifacts/performance/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '.json') }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath -Parent) | Out-Null
$target = Get-Process -Id $RootProcessId
$targetStart = $target.StartTime.ToUniversalTime().ToString('o')
$settingsPath = Join-Path (Split-Path $target.Path -Parent) 'userdata/settings.json'
$enabledModules = @()
if (Test-Path -LiteralPath $settingsPath) { $enabledModules = @((Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json).Enabled) }
$os = Get-CimInstance Win32_OperatingSystem
$processors = @(Get-CimInstance Win32_Processor)
Add-Type -AssemblyName System.Windows.Forms
$displays = @([Windows.Forms.Screen]::AllScreens | ForEach-Object { [pscustomobject]@{ device = $_.DeviceName; primary = $_.Primary; bounds = $_.Bounds.ToString() } })
$samples = [Collections.Generic.List[object]]::new()
$previous = @{}
$watch = [Diagnostics.Stopwatch]::StartNew()
$lastTime = 0.0
$ended = 'completed'
try {
    while ($watch.Elapsed.TotalSeconds -lt $DurationSeconds) {
        $processTree = @(Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name)
        $ids = [Collections.Generic.HashSet[int]]::new()
        [void]$ids.Add($RootProcessId)
        do {
            $added = $false
            foreach ($process in $processTree) {
                if ($ids.Contains([int]$process.ParentProcessId) -and $ids.Add([int]$process.ProcessId)) { $added = $true }
            }
        } while ($added)
        $now = $watch.Elapsed.TotalSeconds
        $elapsed = $now - $lastTime
        $cpuSeconds = 0.0
        $processSamples = @()
        foreach ($processIdValue in $ids) {
            $process = Get-Process -Id $processIdValue -ErrorAction SilentlyContinue
            if (!$process) { continue }
            try {
                $identity = $process.Id.ToString() + ':' + $process.StartTime.ToUniversalTime().Ticks
                $cpu = $process.TotalProcessorTime.TotalSeconds
                if ($previous.ContainsKey($identity)) { $cpuSeconds += [Math]::Max(0, $cpu - $previous[$identity]) }
                $previous[$identity] = $cpu
                $processSamples += [pscustomobject]@{ id = $process.Id; name = $process.ProcessName; cpuSeconds = $cpu; privateBytes = $process.PrivateMemorySize64; workingSetBytes = $process.WorkingSet64; handles = $process.HandleCount; threads = $process.Threads.Count }
            } finally { $process.Dispose() }
        }
        $samples.Add([pscustomobject]@{ seconds = $now; intervalSeconds = $elapsed; cpuSeconds = $cpuSeconds; cpuPercentNormalized = $(if ($samples.Count -gt 0 -and $elapsed -gt 0) { 100 * $cpuSeconds / $elapsed / [Environment]::ProcessorCount } else { $null }); processes = $processSamples })
        $lastTime = $now
        $current = Get-Process -Id $RootProcessId -ErrorAction SilentlyContinue
        if (!$current -or $current.StartTime.ToUniversalTime().ToString('o') -ne $targetStart) { $ended = 'root-exited'; break }
        $current.Dispose()
        Start-Sleep -Milliseconds $IntervalMilliseconds
    }
} catch { $ended = 'failed'; throw }
finally {
    $usable = @($samples.ToArray() | Select-Object -Skip 1)
    $totalElapsed = ($usable | Measure-Object intervalSeconds -Sum).Sum
    $totalCpu = ($usable | Measure-Object cpuSeconds -Sum).Sum
    [pscustomobject]@{
        createdUtc = [DateTime]::UtcNow.ToString('o'); profile = $Profile; status = $ended
        rootProcessId = $RootProcessId; processStartUtc = $targetStart
        enabledModules = $enabledModules; windows = $os.Caption; build = $os.BuildNumber
        logicalProcessors = [Environment]::ProcessorCount; cpu = @($processors | ForEach-Object Name)
        physicalMemoryBytes = [long]$os.TotalVisibleMemorySize * 1024; displays = $displays
        requestedSeconds = $DurationSeconds; observedSeconds = $watch.Elapsed.TotalSeconds
        normalizedCpuPercent = $(if ($totalElapsed -gt 0) { 100 * $totalCpu / $totalElapsed / [Environment]::ProcessorCount } else { $null })
        caveats = @('Read-only attach; records the existing session, not a controlled idle or startup run.', 'Root and live descendants are included. Processes that start and exit between samples can be missed.', 'Working sets include shared pages; private bytes are the memory comparison metric.', 'Explorer is outside this process tree and must be measured separately. Display DPI and WebView2 runtime version must be recorded in the run notes.')
        samples = @($samples.ToArray())
    } | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -LiteralPath $OutputPath
    $target.Dispose()
    Write-Output ('Performance report: ' + $OutputPath)
}
