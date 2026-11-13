# Runs the test suite with coverage and writes the committed status artifact
# (status/coverage.json) that the /status page reads.
#
#   pwsh scripts/status.ps1
#
# The artifact is small and stable; commit it so the page renders without running
# the suite. The raw TestResults output stays gitignored.

[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$resultsDir = Join-Path $repoRoot 'TestResults/status'
$statusDir = Join-Path $repoRoot 'status'

if (Test-Path $resultsDir) {
    Remove-Item $resultsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null

$solution = Get-ChildItem -Path $repoRoot -Filter '*.slnx' | Select-Object -First 1
$target = if ($solution) { $solution.FullName } else { $repoRoot }

Write-Host "Running tests with coverage for $target"
dotnet test $target `
    --nologo `
    --configuration $Configuration `
    --collect:"XPlat Code Coverage" `
    --logger "trx;LogFileName=status.trx" `
    --results-directory $resultsDir

$testExit = $LASTEXITCODE

# Line coverage from the Cobertura report coverlet emits.
$lineRate = $null
$cobertura = Get-ChildItem -Path $resultsDir -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
if ($cobertura) {
    [xml]$coverageXml = Get-Content -Raw $cobertura.FullName
    $lineRate = [double]$coverageXml.coverage.'line-rate'
}

# Pass / fail / skip counts from the TRX log.
$passed = 0
$failed = 0
$skipped = 0
$trx = Get-ChildItem -Path $resultsDir -Recurse -Filter 'status.trx' | Select-Object -First 1
if ($trx) {
    [xml]$trxXml = Get-Content -Raw $trx.FullName
    $counters = $trxXml.TestRun.ResultSummary.Counters
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $skipped = [int]$counters.total - [int]$counters.executed
}

if ($testExit -ne 0 -and $trx -eq $null) {
    throw "dotnet test failed (exit $testExit) and produced no TRX log; refusing to write a misleading artifact."
}

New-Item -ItemType Directory -Path $statusDir -Force | Out-Null

$payload = [ordered]@{
    generated = (Get-Date).ToUniversalTime().ToString('o')
    tests     = [ordered]@{
        passed  = $passed
        failed  = $failed
        skipped = $skipped
    }
    coverage  = [ordered]@{
        line = $lineRate
    }
}

$outFile = Join-Path $statusDir 'coverage.json'
$payload | ConvertTo-Json -Depth 5 | Set-Content -Path $outFile -Encoding utf8

Write-Host "Wrote $outFile (passed=$passed failed=$failed skipped=$skipped line=$lineRate)"
