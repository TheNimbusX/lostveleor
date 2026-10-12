param([string]$Out, [string]$Log, [int]$Series = 3, [string]$Yaws = '0,-135,60', [double]$Duration = 12.5,
      [string]$Form = '', [switch]$NoRebuild, [double]$CameraSize = 0)
# Бросок якоря 07.10: сборка изолированного плеера (зеркало wreckvfx) и съёмка серии бросков в линию Хранителей.
$lock = 'C:\Users\d.grab\Desktop\the-game\artifacts\wreck\v4\forms-vfx\wreckvfx-busy.lock'
"throw-0710 clips agent since $(Get-Date -Format s)" | Out-File -Encoding utf8 $lock
$extra = @{}
if ($NoRebuild) { $extra['NoRebuild'] = $true }
if ($CameraSize -gt 0) { $extra['CameraSize'] = $CameraSize }
$args2 = @('-capture-throw-series', [string]$Series, '-capture-throw-yaws', $Yaws, '-capture-throw-lane')
if ($Form -ne '') { $args2 += @('-capture-throw-form', $Form) }
$t = [string]([math]::Ceiling($Duration + 1))
try {
  & 'C:\Users\d.grab\Desktop\the-game\capture.ps1' -WorkspaceName wreckvfx -Skill anchor-throw -LiveSkill -Encounter forest-guardian -Enemies 4 -Times $t -Video -VideoStart 0 -VideoDuration $Duration -VideoFps 120 -OutDir $Out -ExtraArgs $args2 @extra *> $Log
} catch { "THROW: $_" | Out-File -Append $Log }
finally { Remove-Item -Force $lock -ErrorAction SilentlyContinue }
"DONE frames=$((Get-ChildItem (Join-Path $Out 'video_frames') -Filter *.jpg -ErrorAction SilentlyContinue).Count)" | Out-File -Append $Log
