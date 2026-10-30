param(
    [Parameter(Mandatory = $true)]
    [string]$UiDirectory
)

$ErrorActionPreference = 'Stop'
Push-Location $UiDirectory
try {
    if (-not (Test-Path 'node_modules')) {
        npm ci
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    npm run build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
