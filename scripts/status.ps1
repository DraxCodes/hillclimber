# Runs the test suite with coverage and writes the committed status artifact
# (status/coverage.json) that the /status page reads.
#
#   pwsh scripts/status.ps1
#
# The artifact is small and stable; commit it so the page renders without running
# the suite. The raw TestResults output stays gitignored. Browser-suite evidence is
# merged separately by scripts/status-ui.ps1 against a live host.

[CmdletBinding()]
param(
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$resultsDir = Join-Path $repoRoot 'TestResults/status'
$statusDir = Join-Path $repoRoot 'status'
$rubricFile = Join-Path $statusDir 'coverage-rubric.json'

if (-not (Test-Path -LiteralPath $rubricFile)) {
    throw "Missing $rubricFile; the rating needs its hot-path and edge-case list."
}

$rubric = Get-Content -Raw -LiteralPath $rubricFile | ConvertFrom-Json
if (-not $rubric.hotPaths -or @($rubric.hotPaths).Count -eq 0) {
    throw "$rubricFile lists no hot paths; refusing to write an unratable artifact."
}

if (Test-Path -LiteralPath $resultsDir) {
    Remove-Item -LiteralPath $resultsDir -Recurse -Force
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

function ConvertTo-Rate {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    $parsed = [double]::Parse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture)
    return [math]::Round($parsed, 4)
}

# Line and branch coverage from the Cobertura report coverlet emits.
$lineRate = $null
$branchRate = $null
$allClasses = @()
$cobertura = Get-ChildItem -Path $resultsDir -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
if ($cobertura) {
    [xml]$coverageXml = Get-Content -Raw $cobertura.FullName
    $lineRate = ConvertTo-Rate $coverageXml.coverage.'line-rate'
    $branchRate = ConvertTo-Rate $coverageXml.coverage.'branch-rate'
    $allClasses = @($coverageXml.coverage.packages.package.classes.class)
}

# Aggregate a hot path across its type and any nested compiler-generated types
# (async state machines, lambdas), matching on the type name and `Type/...`.
function Measure-HotPath {
    param(
        [object[]]$Classes,
        [string[]]$Prefixes
    )

    $lineCovered = 0
    $lineTotal = 0
    $branchCovered = 0
    $branchTotal = 0
    $matched = $false

    foreach ($class in $Classes) {
        $name = [string]$class.name
        $isMatch = $false
        foreach ($prefix in $Prefixes) {
            if ($name -eq $prefix -or $name.StartsWith($prefix + '/', [System.StringComparison]::Ordinal)) {
                $isMatch = $true
                break
            }
        }
        if (-not $isMatch) {
            continue
        }
        $matched = $true

        foreach ($line in @($class.lines.line)) {
            if ($null -eq $line) {
                continue
            }

            $lineTotal++
            if ([int]$line.hits -gt 0) {
                $lineCovered++
            }

            $conditions = [string]$line.'condition-coverage'
            if ($conditions -match '\((\d+)/(\d+)\)') {
                $branchCovered += [int]$Matches[1]
                $branchTotal += [int]$Matches[2]
            }
        }
    }

    if (-not $matched) {
        return $null
    }

    return [ordered]@{
        line     = if ($lineTotal -gt 0) { [math]::Round($lineCovered / $lineTotal, 4) } else { $null }
        branch   = if ($branchTotal -gt 0) { [math]::Round($branchCovered / $branchTotal, 4) } else { $null }
        lines    = $lineTotal
        branches = $branchTotal
    }
}

$hotPaths = @()
foreach ($rule in @($rubric.hotPaths)) {
    $measurement = Measure-HotPath -Classes $allClasses -Prefixes @($rule.classes)
    $hotPaths += [ordered]@{
        id       = [string]$rule.id
        line     = if ($measurement) { $measurement.line } else { $null }
        branch   = if ($measurement) { $measurement.branch } else { $null }
        lines    = if ($measurement) { $measurement.lines } else { 0 }
        branches = if ($measurement) { $measurement.branches } else { 0 }
    }
}

# Pass / fail / skip counts from the TRX log.
$passed = 0
$failed = 0
$skipped = 0
$testResults = @()
$trx = Get-ChildItem -Path $resultsDir -Recurse -Filter 'status.trx' | Select-Object -First 1
if ($trx) {
    [xml]$trxXml = Get-Content -Raw $trx.FullName
    $counters = $trxXml.TestRun.ResultSummary.Counters
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $skipped = [int]$counters.total - [int]$counters.executed

    $testResults = @($trxXml.TestRun.Results.UnitTestResult | ForEach-Object {
        [pscustomobject]@{
            Name    = [string]$_.testName
            Outcome = [string]$_.outcome
        }
    })
}

if ($testExit -ne 0 -and $trx -eq $null) {
    throw "dotnet test failed (exit $testExit) and produced no TRX log; refusing to write a misleading artifact."
}

# A named edge case counts as covered only when its test exists and every theory
# case sharing the name passed. A renamed or deleted test becomes a visible gap.
$edgeCases = @()
foreach ($edge in @($rubric.edgeCases)) {
    $prefix = [string]$edge.test
    $matches = @($testResults | Where-Object {
        $_.Name -eq $prefix -or $_.Name.StartsWith($prefix + '(', [System.StringComparison]::Ordinal)
    })

    $edgePassed = $matches.Count -gt 0
    foreach ($match in $matches) {
        if ($match.Outcome -ne 'Passed') {
            $edgePassed = $false
        }
    }

    $edgeCases += [ordered]@{
        id     = [string]$edge.id
        test   = $prefix
        passed = $edgePassed
    }
}

# Fingerprint of everything that can change the measurement. Keep this rule in
# sync with ClimberWeb/Services/CoverageInputs.cs (same version, same file set,
# same byte order) so the page can tell whether the artifact still applies.
function Get-SourceFingerprint {
    param([string]$Root)

    if (-not (Test-Path -LiteralPath $Root)) {
        return $null
    }

    $roots = @('ClimberWeb', 'ClimberWeb.Tests', 'scripts')
    $extensions = @('.cs', '.razor', '.csproj', '.json', '.jsonl', '.ps1', '.mjs', '.css')
    $files = New-Object 'System.Collections.Generic.List[string]'

    foreach ($relativeRoot in $roots) {
        $directory = Join-Path $Root $relativeRoot
        if (-not (Test-Path -LiteralPath $directory)) {
            continue
        }

        Get-ChildItem -LiteralPath $directory -Recurse -File -Force | ForEach-Object {
            if ($_.FullName -match '[\\/](bin|obj|node_modules)[\\/]') {
                return
            }
            if ($extensions -notcontains $_.Extension.ToLowerInvariant()) {
                return
            }
            $files.Add($_.FullName)
        }
    }

    $rubricPath = Join-Path $Root 'status/coverage-rubric.json'
    if (Test-Path -LiteralPath $rubricPath) {
        $files.Add($rubricPath)
    }

    if ($files.Count -eq 0) {
        return $null
    }

    $rootPrefix = $Root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $relatives = New-Object 'System.Collections.Generic.List[string]'
    foreach ($file in $files) {
        $relatives.Add(($file.Substring($rootPrefix.Length) -replace '\\', '/'))
    }

    $ordered = $relatives.ToArray()
    [Array]::Sort($ordered, [System.StringComparer]::Ordinal)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    $buffer = New-Object System.IO.MemoryStream
    try {
        foreach ($relative in $ordered) {
            $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($relative)
            $buffer.Write($pathBytes, 0, $pathBytes.Length)
            $buffer.WriteByte(10)
            $content = [System.IO.File]::ReadAllBytes((Join-Path $Root ($relative -replace '/', '\')))
            $buffer.Write($content, 0, $content.Length)
            $buffer.WriteByte(10)
        }

        $hash = $sha.ComputeHash($buffer.ToArray())
    }
    finally {
        $sha.Dispose()
        $buffer.Dispose()
    }

    return ([System.BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
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

$fingerprint = Get-SourceFingerprint -Root $repoRoot

New-Item -ItemType Directory -Path $statusDir -Force | Out-Null

# Preserve UI evidence only when an existing artifact belongs to the same code;
# otherwise the stale merge would attribute browser results to the new numbers.
$previousUi = $null
$artifactFile = Join-Path $statusDir 'coverage.json'
if (Test-Path -LiteralPath $artifactFile) {
    try {
        $previous = Get-Content -Raw -LiteralPath $artifactFile | ConvertFrom-Json
        if ($previous.fingerprint -and $previous.fingerprint.version -eq 1 -and
            $previous.fingerprint.inputs -eq $fingerprint -and $previous.ui) {
            $previousUi = $previous.ui
        }
    }
    catch {
        $previousUi = $null
    }
}

$payload = [ordered]@{
    schema      = 2
    generated   = (Get-Date).ToUniversalTime().ToString('o')
    head        = $head
    fingerprint = [ordered]@{
        version = 1
        inputs  = $fingerprint
    }
    tests       = [ordered]@{
        passed  = $passed
        failed  = $failed
        skipped = $skipped
    }
    coverage    = [ordered]@{
        line   = $lineRate
        branch = $branchRate
    }
    hotPaths    = $hotPaths
    edgeCases   = $edgeCases
}

if ($previousUi) {
    $payload.ui = $previousUi
}

$outFile = Join-Path $statusDir 'coverage.json'
$payload | ConvertTo-Json -Depth 8 | Set-Content -Path $outFile -Encoding utf8

$matchedEdges = @($edgeCases | Where-Object { $_.passed }).Count
$fingerprintLabel = if ($fingerprint) { $fingerprint.Substring(0, 12) } else { 'none' }
Write-Host "Wrote $outFile (passed=$passed failed=$failed skipped=$skipped line=$lineRate branch=$branchRate hotPaths=$($hotPaths.Count) edges=$matchedEdges/$($edgeCases.Count) fingerprint=$fingerprintLabel)"
