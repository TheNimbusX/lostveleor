$ErrorActionPreference = 'Stop'
$taskRoot = 'C:\Users\d.grab\Desktop\the-game'
$taskOut = Join-Path $taskRoot 'ART\characters\pelag\demo-polish-2026-09-30\colour'
$taskBudgetRunner = Join-Path $taskRoot 'artifacts\pelag-colour-checks\capture-budget.ps1'
$taskCaptureSource = [IO.File]::ReadAllText((Join-Path $taskRoot 'capture.ps1'))
$taskRootLine = '$root    = Split-Path -Parent $MyInvocation.MyCommand.Path'
$taskDeadlineLine = '$deadline = [int][Math]::Ceiling([Math]::Max($budget.Maximum, $captureEnd)) + 90'
if (-not $taskCaptureSource.Contains($taskRootLine) -or -not $taskCaptureSource.Contains($taskDeadlineLine)) {
    throw 'Capture wrapper changed; review the isolated budget override before running.'
}
$taskCaptureSource = $taskCaptureSource.Replace($taskRootLine, ('$root = ''' + $taskRoot + ''''))
$taskCaptureSource = $taskCaptureSource.Replace($taskDeadlineLine, '$deadline = 480')
[IO.File]::WriteAllText($taskBudgetRunner, $taskCaptureSource, [Text.UTF8Encoding]::new($false))
$taskCommon = @{ WorkspaceName='pelag-colour'; NoRebuild=$true; Seed=42; Width=960; Height=540;
    CameraSize=2.5; CameraYaw=35; CameraPitch=38; Video=$true; SilentVideo=$true;
    VideoStart=.05; VideoDuration=2.2; VideoFps=60; Times='0.2,0.35,0.5,0.8,1.2,2.0'; LiveSkill=$true; Enemies=1 }
$taskCases = @(
    @{Name='basic-attack-handoff-r02'; Category='game-verified'; Args=@{Skill='anchor-slam'; SlamCase='autoattack'}},
    @{Name='slam-after'; Category='game-verified'; Args=@{Skill='anchor-slam'}},
    @{Name='roll-after'; Category='game-verified'; Args=@{Skill='anchor-slam'; SlamCase='roll'}},
    @{Name='death-after'; Category='game-verified'; Args=@{Skill='anchor-slam'; DeathDuringSkill=$true}},
    @{Name='roll-30'; Category='fps-verified'; Args=@{Skill='anchor-slam'; SlamCase='roll'; VideoFps=30}},
    @{Name='roll-120'; Category='fps-verified'; Args=@{Skill='anchor-slam'; SlamCase='roll'; VideoFps=120}}
)
foreach ($taskCase in $taskCases) {
    $taskArgs = @{}; foreach ($taskKey in $taskCommon.Keys) { $taskArgs[$taskKey]=$taskCommon[$taskKey] }
    foreach ($taskKey in $taskCase.Args.Keys) { $taskArgs[$taskKey]=$taskCase.Args[$taskKey] }
    $taskDirectory = Join-Path (Join-Path $taskOut $taskCase.Category) $taskCase.Name
    & $taskBudgetRunner @taskArgs -OutDir $taskDirectory
    if ($LASTEXITCODE -ne 0) { throw "Capture failed: $($taskCase.Name)" }
    $taskLog = Get-Content -LiteralPath (Join-Path $taskDirectory 'player.log') -Raw -Encoding UTF8
    $taskStrains = [regex]::Matches($taskLog, '\[anchor-slam\].*? strain=([\d.,-]+)')
    foreach ($taskMatch in $taskStrains) {
        $taskValue = [double]::Parse($taskMatch.Groups[1].Value.Replace(',','.'),[Globalization.CultureInfo]::InvariantCulture)
        if ($taskValue -gt .02) { throw "Chain strain exceeds 2% in $($taskCase.Name): $taskValue" }
    }
    if ($taskStrains.Count -eq 0) { throw "No chain measurements: $($taskCase.Name)" }
    if (-not $taskLog.Contains('[anchor-release]')) { throw "Capture stopped before weapon handoff: $($taskCase.Name)" }
    if ((Get-ChildItem -LiteralPath $taskDirectory -Filter 'shot_*.png').Count -ne 6) {
        throw "Incomplete shot schedule: $($taskCase.Name)"
    }
    Write-Output "Verified chain: $($taskCase.Name)"
}
