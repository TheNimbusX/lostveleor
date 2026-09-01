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

# ---------------------------------------------------------------------------
# Сборка представления вне Unity.
#
# Тесты покрывают только Game.Sim, а правится чаще всего Game.View. Без этого
# шага опечатка в ArenaView живёт до следующего открытия редактора — и находит
# её обычно владелец, а не автор правки. Здесь она находится за секунды.
#
# Проверка КОМПИЛЯЦИИ, не поведения: она ничего не говорит о том, как игра
# выглядит. Но неверный код до картинки и не доходит.
# ---------------------------------------------------------------------------
$viewCheck = Join-Path $repoRoot 'tools\viewcheck\viewcheck.csproj'
if (Test-Path -LiteralPath $viewCheck) {
    Write-Host ''
    Write-Host 'Сборка Game.View и Editor вне Unity...'
    dotnet build $viewCheck --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "Game.View/Editor не собираются (код $LASTEXITCODE)."
    }
    Write-Host 'Game.View и Editor: собираются.'
}
else {
    Write-Warning "Обвязки viewcheck нет: $viewCheck. Представление не проверено."
}

