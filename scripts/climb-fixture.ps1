# Materialise the simple-climb fixture as a throwaway git repository.
#
#   pwsh scripts/climb-fixture.ps1 [-Destination <path>]
#
# The dashboard's climb loop reverts losing iterations with `git checkout -- .`, so the
# target must be its own git repository with a clean initial commit. This copies
# fixtures/simple-climb/ to a temp directory (or -Destination), initialises git, commits
# the half-finished state, and prints the absolute path to paste into the dashboard's
# "target folder" field.
#
# This repo's own history is never touched. Re-running with the default destination wipes
# and recreates the temp copy.

[CmdletBinding()]
param(
    [string]$Destination
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'fixtures/simple-climb'

if (-not (Test-Path (Join-Path $source 'SimpleClimb.Tests.csproj'))) {
    throw "Fixture source not found at $source"
}

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path ([System.IO.Path]::GetTempPath()) 'climber-fixture'
}

if (Test-Path $Destination) {
    Remove-Item $Destination -Recurse -Force
}

New-Item -ItemType Directory -Path $Destination -Force | Out-Null

# Copy the fixture source, minus anything already-built or tracked.
Get-ChildItem -Path $source -Force | Where-Object { $_.Name -notin @('bin', 'obj', '.git') } |
    ForEach-Object { Copy-Item $_.FullName -Destination $Destination -Recurse -Force }

# Give the throwaway repo the same agents the dashboard runs with, so a run or climb here
# exercises the real (orchestrated) workflow instead of failing on an unknown agent.
$agentsSource = Join-Path $repoRoot '.opencode/agents'
if (Test-Path $agentsSource) {
    $agentsTarget = Join-Path $Destination '.opencode/agents'
    New-Item -ItemType Directory -Path $agentsTarget -Force | Out-Null
    Copy-Item (Join-Path $agentsSource '*') -Destination $agentsTarget -Recurse -Force
}

Push-Location $Destination
try {
    git init --quiet
    git add -A
    # A stable identity so the script works on a machine with no global git config.
    git -c user.name='climber' -c user.email='climber@example.invalid' commit --quiet -m 'Initial half-finished calculator'
    if ($LASTEXITCODE -ne 0) {
        throw "git commit failed in $Destination"
    }
}
finally {
    Pop-Location
}

Write-Host "Fixture ready: $Destination"
Write-Host "Target folder: $Destination"
Write-Host 'Goal prompt:  Implement the unimplemented methods in Calculator.cs so all tests pass.'
