$ErrorActionPreference = 'Stop'
$taskRoot = 'C:\Users\d.grab\Desktop\the-game'
$taskShadow = Join-Path $taskRoot 'artifacts\pelag-colour-project'
$taskBuild = Join-Path $taskRoot 'artifacts\pelag-colour-build'
$taskLog = Join-Path $taskRoot 'artifacts\pelag-colour-build.log'
$taskSource = Join-Path $taskRoot 'razlom'
foreach ($taskFolder in 'Assets','Packages','ProjectSettings') {
    $taskCopyArgs = @('/E','/NFL','/NDL','/NJH','/NJS','/NP')
    if ($taskFolder -eq 'Assets') { $taskCopyArgs += @('/XD', (Join-Path $taskSource 'Assets\Game.Tests')) }
    & robocopy (Join-Path $taskSource $taskFolder) (Join-Path $taskShadow $taskFolder) @taskCopyArgs | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Shadow copy failed: $taskFolder" }
}
$taskPackageCache = Join-Path $taskShadow 'Library\PackageCache'
if (-not (Test-Path -LiteralPath $taskPackageCache)) {
    New-Item -ItemType Directory -Force -Path (Join-Path $taskShadow 'Library') | Out-Null
    New-Item -ItemType Junction -Path $taskPackageCache -Target (Join-Path $taskSource 'Library\PackageCache') | Out-Null
}
$taskUnity = 'C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe'
$taskProcess = Start-Process -FilePath $taskUnity -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
    '-batchmode','-nographics','-quit','-projectPath',$taskShadow,
    '-executeMethod','PelagDemoAppearanceSetup.BuildCapture','-razlom-build-out',$taskBuild,'-logFile',$taskLog
)
$taskLogText = Get-Content -Raw -Encoding UTF8 -LiteralPath $taskLog
if ($taskLogText -notmatch 'Build Finished, Result:\s*Success\.' -or -not (Test-Path -LiteralPath (Join-Path $taskBuild 'Razlom.exe'))) {
    throw "Capture player build failed (exit $($taskProcess.ExitCode)): $taskLog"
}
Write-Output 'Capture player built successfully.'
