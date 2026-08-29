[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot 'tools\Game.Sim.Tests\Game.Sim.Tests.csproj'
$resultsDirectory = Join-Path $repoRoot 'artifacts\test-results'
$resultFile = Join-Path $resultsDirectory 'game-sim-tests.xml'

New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
if (Test-Path -LiteralPath $resultFile) {
    Remove-Item -LiteralPath $resultFile -Force
}

dotnet test $project `
    --nologo `
    --configuration Release `
    --logger "trx;LogFileName=game-sim-tests.xml" `
    --results-directory $resultsDirectory

if ($LASTEXITCODE -ne 0) {
    throw "Game.Sim tests failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $resultFile)) {
    throw "The test runner succeeded but did not create $resultFile."
}

Write-Host "Test XML: $resultFile"

