$ErrorActionPreference = 'Stop'
$taskRoot = 'C:\Users\d.grab\Desktop\the-game'
$taskOut = Join-Path $taskRoot 'ART\characters\pelag\demo-polish-2026-09-30\colour\fps'
foreach ($taskFps in 30,120) {
    foreach ($taskCase in 'roll','wreck') {
        $taskArgs = @{WorkspaceName='pelag-colour'; NoRebuild=$true; Seed=42; Width=1280; Height=720;
            CameraSize=2.5; CameraYaw=35; CameraPitch=38; Video=$true; SilentVideo=$true;
            VideoStart=.2; VideoDuration=3.9; VideoFps=$taskFps; Times='1,2,3.5';
            LiveSkill=$true; Enemies=1; OutDir=(Join-Path $taskOut "$taskCase-$taskFps")}
        if ($taskCase -eq 'roll') { $taskArgs.Skill='anchor-slam'; $taskArgs.SlamCase='roll' }
        else { $taskArgs.Skill='wreck' }
        & (Join-Path $taskRoot 'capture.ps1') @taskArgs
        if ($LASTEXITCODE -ne 0) { throw "Capture failed: $taskCase-$taskFps" }
        Write-Output "Checked: $taskCase-$taskFps"
    }
}
