$ErrorActionPreference = 'Stop'
$taskRoot = 'C:\Users\d.grab\Desktop\the-game'
$taskOut = Join-Path $taskRoot 'ART\characters\pelag\demo-polish-2026-09-30\colour\game'
$taskCommon = @{ WorkspaceName='pelag-colour'; NoRebuild=$true; Seed=42; Width=1280; Height=720;
    CameraSize=2.5; CameraYaw=35; CameraPitch=38; Video=$true; SilentVideo=$true;
    VideoStart=.2; VideoDuration=3.9; VideoFps=60; Times='0.5,1,2,3.5' }
$taskCases = @(
    @{ Name='run-after'; Args=@{Run=$true; Equipment=$true; Enemies=0} },
    @{ Name='slam-after'; Args=@{Skill='anchor-slam';LiveSkill=$true;Enemies=1} },
    @{ Name='roll-after'; Args=@{Skill='anchor-slam';LiveSkill=$true;SlamCase='roll';Enemies=1} },
    @{ Name='leap-after'; Args=@{Skill='anchor-leap';LiveSkill=$true;Enemies=1} },
    @{ Name='wreck-after'; Args=@{Skill='wreck';LiveSkill=$true;Enemies=1} },
    @{ Name='death-after'; Args=@{Skill='anchor-slam';LiveSkill=$true;DeathDuringSkill=$true;Enemies=1} }
)
foreach ($taskCase in $taskCases) {
    $taskSpecific = $taskCase.Args
    & (Join-Path $taskRoot 'capture.ps1') @taskCommon @taskSpecific -OutDir (Join-Path $taskOut $taskCase.Name)
    if ($LASTEXITCODE -ne 0) { throw "Capture failed: $($taskCase.Name)" }
    Write-Output "Checked: $($taskCase.Name)"
}
