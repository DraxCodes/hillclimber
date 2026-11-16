# Runs the free browser suites against a live host and merges their results into
# status/coverage.json. Kept separate from status.ps1 because a dev host and
# `dotnet test` contend for the same build output on Windows.
#
#   # terminal 1
#   dotnet run --project ClimberWeb
#   # terminal 2
#   pwsh scripts/status-ui.ps1 -BaseUrl http://localhost:5179
#
# Only the free suites listed in status/coverage-rubric.json run. `surfaces.mjs`
# is paid and is never invoked here. Exit code 1 means at least one suite failed.

[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5179',
    [string]$Pin = '1234'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactFile = Join-Path $repoRoot 'status/coverage.json'
$rubricFile = Join-Path $repoRoot 'status/coverage-rubric.json'
$uiDir = Join-Path $PSScriptRoot 'ui'

if (-not (Test-Path -LiteralPath $artifactFile)) {
    throw "No $artifactFile. Run scripts/status.ps1 first so browser evidence has a measurement to attach to."
}

if (-not (Test-Path -LiteralPath $rubricFile)) {
    throw "Missing $rubricFile."
}

if (-not (Test-Path -LiteralPath (Join-Path $uiDir 'node_modules'))) {
    throw "Playwright is not installed. Run 'npm install' in $uiDir first."
}

$rubric = Get-Content -Raw -LiteralPath $rubricFile | ConvertFrom-Json
$suites = @()

foreach ($rule in @($rubric.uiSuites)) {
    if ($rule.paid) {
        Write-Host "Skipping $($rule.id) ($($rule.script)): paid harness, never run by the status scripts."
        continue
    }

    $scriptPath = Join-Path $uiDir ([string]$rule.script)
    if (-not (Test-Path -LiteralPath $scriptPath)) {
        Write-Host "Skipping $($rule.id): $($rule.script) is missing."
        continue
    }

    Write-Host ""
    Write-Host "Running $($rule.id) ($($rule.script))"

    $env:BASE_URL = $BaseUrl
    $env:PIN = $Pin
    Push-Location $uiDir
    try {
        $output = & node ([string]$rule.script) 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
        Remove-Item Env:BASE_URL -ErrorAction SilentlyContinue
        Remove-Item Env:PIN -ErrorAction SilentlyContinue
    }

    $output | ForEach-Object { Write-Host $_ }

    $text = ($output | Out-String)
    $failedChecks = @($output | Where-Object { "$_" -match '^\s*FAIL\b' }).Count
    $passedChecks = @($output | Where-Object { "$_" -match '^\s*ok\b' }).Count
    $checks = $passedChecks + $failedChecks
    if ($text -match 'in (\d+) checks') {
        $checks = [int]$Matches[1]
    }

    $suitePassed = ($exitCode -eq 0) -and ($failedChecks -eq 0)
    $suites += [ordered]@{
        id     = [string]$rule.id
        passed = $suitePassed
        checks = $checks
    }

    Write-Host "$($rule.id): $(if ($suitePassed) { 'passed' } else { 'FAILED' }) ($checks checks)"
}

$head = $null
try {
    $head = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) {
        $head = $null
    }
    else {
        $head = ([string]$head).Trim()
    }
}
catch {
    $head = $null
}

# Merge only the ui section; the measured numbers and their fingerprint stay as
# status.ps1 wrote them.
$artifact = Get-Content -Raw -LiteralPath $artifactFile | ConvertFrom-Json
$ui = [ordered]@{
    generated = (Get-Date).ToUniversalTime().ToString('o')
    head      = $head
    suites    = $suites
}

if ($artifact.PSObject.Properties.Name -contains 'ui') {
    $artifact.ui = $ui
}
else {
    $artifact | Add-Member -NotePropertyName ui -NotePropertyValue $ui
}

$artifact | ConvertTo-Json -Depth 8 | Set-Content -Path $artifactFile -Encoding utf8

$failedSuites = @($suites | Where-Object { -not $_.passed }).Count
Write-Host ""
Write-Host "Wrote $artifactFile (ui: $($suites.Count - $failedSuites)/$($suites.Count) suites passed)"

if ($failedSuites -gt 0) {
    exit 1
}
