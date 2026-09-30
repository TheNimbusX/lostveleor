$ErrorActionPreference = 'Stop'
$taskRoot = 'C:\Users\d.grab\Desktop\the-game'
$taskOut = Join-Path $taskRoot 'ART\characters\pelag\demo-polish-2026-09-30\colour\poses-r02'
foreach ($taskSkill in 'whirlwind','cleave','blaze','chain-step','fire-flask','skewer','backblast') {
    $taskArgs = @{WorkspaceName='pelag-colour'; NoRebuild=$true; Seed=42; Width=1280; Height=720;
        CameraSize=2.5; CameraYaw=35; CameraPitch=38; Video=$true; SilentVideo=$true;
        VideoStart=.2; VideoDuration=2.2; VideoFps=60; Times='0.55,0.7,1,1.4,2';
        Skill=$taskSkill; LiveSkill=$true; Enemies=1; OutDir=(Join-Path $taskOut $taskSkill)}
    # Times are relative to the post-warmup capture clock, not Time.time from the CSV.
    if ($taskSkill -eq 'blaze') { $taskArgs.Seed=20260829; $taskArgs.Enemies=0; $taskArgs.CameraSize=3; $taskArgs.CameraPitch=55 }
    if ($taskSkill -eq 'backblast') { $taskArgs.Seed=20260829; $taskArgs.CameraSize=3; $taskArgs.CameraPitch=55 }
    & (Join-Path $taskRoot 'capture.ps1') @taskArgs
    if ($LASTEXITCODE -ne 0) { throw "Capture failed: $taskSkill" }
    Write-Output "Checked: $taskSkill"
}
