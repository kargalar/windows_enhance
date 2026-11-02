$ErrorActionPreference = 'Stop'
$fixtureParent = Join-Path ([IO.Path]::GetTempPath()) 'WinForge.TestRunnerFixtures'
$fixture = Join-Path $fixtureParent ([Guid]::NewGuid().ToString('N'))
$originalPath = $env:PATH
$checks = 0
function Assert-Runner([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $script:checks++
}
function Invoke-FixtureRunner([string]$Log, [string[]]$Extra = @()) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + (Join-Path $fixture 'scripts/test.ps1') + '"')) + $Extra
    $process = Start-Process -FilePath (Get-Command powershell.exe).Source -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $fixture ($Log + '.out.log')) -RedirectStandardError (Join-Path $fixture ($Log + '.err.log'))
    try { return $process.ExitCode } finally { $process.Dispose() }
}
try {
    New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'scripts'), (Join-Path $fixture 'tools'), (Join-Path $fixture 'src/WinForge.App/ui') | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test.ps1'), (Join-Path $PSScriptRoot 'test-groups.json') -Destination (Join-Path $fixture 'scripts')
    $manifest = Get-Content -Raw (Join-Path $fixture 'scripts/test-groups.json') | ConvertFrom-Json
    foreach ($name in @($manifest.fast) + @($manifest.integration)) {
        $directory = Join-Path $fixture ('tests/' + $name)
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        Set-Content -LiteralPath (Join-Path $directory ($name + '.csproj')) -Value '<Project />'
    }
    # These tools never compile or test product code: they exercise orchestration
    # and failure reporting in a disposable, explicitly labelled fixture tree.
    @'
@echo off
echo fixture-dotnet %*>>"%WINFORGE_FIXTURE_CALLS%"
if "%WINFORGE_FIXTURE_FAIL%"=="1" exit /b 17
if "%WINFORGE_FIXTURE_FAIL%"=="interactive" (
  for %%A in (%*) do if "%%~A"=="--native-input-only" exit /b 29
)
exit /b 0
'@ | Set-Content -Encoding ASCII -LiteralPath (Join-Path $fixture 'tools/dotnet.cmd')
    @'
@echo off
echo fixture-npm %*>>"%WINFORGE_FIXTURE_CALLS%"
exit /b 0
'@ | Set-Content -Encoding ASCII -LiteralPath (Join-Path $fixture 'tools/npm.cmd')
    $env:PATH = (Join-Path $fixture 'tools') + ';' + $originalPath
    $env:WINFORGE_FIXTURE_CALLS = Join-Path $fixture 'calls.txt'
    $env:WINFORGE_FIXTURE_FAIL = '1'
    $exitCode = Invoke-FixtureRunner 'failure'
    Assert-Runner ($exitCode -eq 17) 'Runner must preserve the failing child exit code.'
    $report = Get-ChildItem -LiteralPath (Join-Path $fixture 'artifacts/test-results') | Sort-Object LastWriteTimeUtc | Select-Object -Last 1
    $data = Get-Content -Raw -LiteralPath $report.FullName | ConvertFrom-Json
    Assert-Runner (@($data.results | Where-Object status -eq 'failed').Count -eq 1) 'Runner must identify the first failure.'
    Assert-Runner ((Get-Content -LiteralPath $env:WINFORGE_FIXTURE_CALLS).Count -eq 1) 'Runner must stop before later commands after failure.'
    Assert-Runner (@($data.results | Where-Object { $_.name -in $manifest.integration -and $_.status -eq 'skipped' }).Count -eq $manifest.integration.Count) 'Fast mode must report every unselected integration group.'

    $env:WINFORGE_FIXTURE_FAIL = '0'
    $exitCode = Invoke-FixtureRunner 'success'
    Assert-Runner ($exitCode -eq 0) 'Successful fixture commands must produce a zero exit code.'
    $report = Get-ChildItem -LiteralPath (Join-Path $fixture 'artifacts/test-results') | Sort-Object LastWriteTimeUtc | Select-Object -Last 1
    $data = Get-Content -Raw -LiteralPath $report.FullName | ConvertFrom-Json
    Assert-Runner (@($data.results | Where-Object status -eq 'passed').Count -eq ($manifest.fast.Count + 2)) 'Fast mode must report all selected .NET and UI groups.'
    Assert-Runner (($data.results | Where-Object name -eq 'Shortcut native input').status -eq 'skipped') 'Fast mode must explicitly report deferred interactive input coverage.'
    Assert-Runner ([bool]((Get-Content -LiteralPath $env:WINFORGE_FIXTURE_CALLS) -match '--fast')) 'Fast mode must invoke the non-interactive shortcut profile.'

    $exitCode = Invoke-FixtureRunner 'interactive' @('-InteractiveInput')
    $report = Get-ChildItem -LiteralPath (Join-Path $fixture 'artifacts/test-results') | Sort-Object LastWriteTimeUtc | Select-Object -Last 1
    $data = Get-Content -Raw -LiteralPath $report.FullName | ConvertFrom-Json
    Assert-Runner ($exitCode -eq 0 -and ($data.results | Where-Object name -eq 'Shortcut native input').status -eq 'passed' -and !$data.integration) 'Interactive input can run independently of the broader integration suite.'

    $env:WINFORGE_FIXTURE_FAIL = 'interactive'
    $exitCode = Invoke-FixtureRunner 'interactive-failure' @('-InteractiveInput')
    $report = Get-ChildItem -LiteralPath (Join-Path $fixture 'artifacts/test-results') | Sort-Object LastWriteTimeUtc | Select-Object -Last 1
    $data = Get-Content -Raw -LiteralPath $report.FullName | ConvertFrom-Json
    Assert-Runner ($exitCode -eq 29 -and ($data.results | Where-Object name -eq 'Shortcut native input').status -eq 'failed') 'An interactive input failure retains its exact exit code and cannot become a successful Fast result.'
    $env:WINFORGE_FIXTURE_FAIL = '0'

    ('Write-Output ''PASS-GROUP:' + $manifest.integration[0] + '''' + [Environment]::NewLine + 'exit 23') | Set-Content -LiteralPath (Join-Path $fixture 'scripts/test-integration.ps1')
    $exitCode = Invoke-FixtureRunner 'partial' @('-Group', 'Integration')
    Assert-Runner ($exitCode -eq 23) 'Integration failures must preserve the native exit code.'
    $report = Get-ChildItem -LiteralPath (Join-Path $fixture 'artifacts/test-results') | Sort-Object LastWriteTimeUtc | Select-Object -Last 1
    $data = Get-Content -Raw -LiteralPath $report.FullName | ConvertFrom-Json
    Assert-Runner (($data.results | Where-Object name -eq $manifest.integration[0]).status -eq 'passed') 'Completed integration groups must survive a later failure.'
    Assert-Runner (@($data.results | Where-Object { $_.name -in $manifest.integration -and $_.status -eq 'not-run' }).Count -eq ($manifest.integration.Count - 1)) 'Incomplete integration groups must not be reported as passing.'

    New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'tests/UnlistedTests') | Out-Null
    Set-Content -LiteralPath (Join-Path $fixture 'tests/UnlistedTests/UnlistedTests.csproj') -Value '<Project />'
    $exitCode = Invoke-FixtureRunner 'inventory' @('-List')
    Assert-Runner ($exitCode -ne 0) 'An unlisted test project must fail inventory validation.'
    Write-Output ("PASS: $checks test-runner contract checks (stub tools; no product tests run by this fixture).")
} finally {
    $env:PATH = $originalPath
    Remove-Item Env:\WINFORGE_FIXTURE_FAIL, Env:\WINFORGE_FIXTURE_CALLS -ErrorAction SilentlyContinue
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolvedFixture) -eq [IO.Path]::GetFullPath($fixtureParent) -and (Test-Path -LiteralPath $resolvedFixture)) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
exit 0
