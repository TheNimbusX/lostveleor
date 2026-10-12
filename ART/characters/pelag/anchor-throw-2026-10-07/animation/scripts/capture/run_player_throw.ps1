param([string]$Out, [int]$Series = 3, [string]$Yaws = '0,-135,60', [double]$Duration = 12.5, [string]$Form = '',
      [double]$CameraSize = 0, [double]$CameraYaw = 0, [int]$Fps = 120, [int]$WaitSeconds = 1500)
# Бросок якоря 07.10: съёмка из уже собранного плеера wreckvfx-build (ключи как capture.ps1 -Skill anchor-throw -LiveSkill),
# без убийства через (Times + 90) с: 120 к/с видео идёт ~10 к/с реального времени. Замок wreckvfx-busy.lock — на время съёмки.
$lock = 'C:\Users\d.grab\Desktop\the-game\artifacts\wreck\v4\forms-vfx\wreckvfx-busy.lock'
"throw-0710 clips agent (player) since $(Get-Date -Format s)" | Out-File -Encoding utf8 $lock
try {
  $player = 'C:\Users\d.grab\Desktop\the-game\artifacts\wreckvfx-build\Razlom.exe'
  New-Item -ItemType Directory -Path $Out -Force | Out-Null
  $inv = [Globalization.CultureInfo]::InvariantCulture
  $a = @('-screen-width', 1920, '-screen-height', 1080, '-screen-fullscreen', '0', '-logFile', (Join-Path $Out 'player.log'),
         '-razlom-capture', '-capture-out', $Out, '-capture-times', ([string]([math]::Ceiling($Duration + 1))), '-capture-enemies', 4,
         '-capture-seed', '20260829', '-capture-width', 1920, '-capture-height', 1080, '-capture-skill', 'anchor-throw', '-capture-live-skill')
  $a += @('-capture-throw-series', [string]$Series, '-capture-throw-yaws', $Yaws, '-capture-throw-lane')
  if ($Form -ne '') { $a += @('-capture-throw-form', $Form) }
  $a += @('-capture-cast-distance', '3', '-capture-hold-ticks', '60', '-capture-encounter', 'forest-guardian')
  if ($CameraYaw -ne 0) { $a += @('-capture-camera-yaw', $CameraYaw.ToString($inv)) }
  if ($CameraSize -gt 0) { $a += @('-capture-camera-size', $CameraSize.ToString($inv)) }
  $a += @('-capture-silent-video', '-capture-video', '-capture-video-start', '0', '-capture-video-duration', $Duration.ToString($inv), '-capture-video-fps', $Fps)
  $p = Start-Process -FilePath $player -WindowStyle Hidden -PassThru -ArgumentList $a
  if (-not $p.WaitForExit($WaitSeconds * 1000)) { $p.Kill(); "KILLED after $WaitSeconds s" }
  "EXIT $($p.ExitCode) frames=$((Get-ChildItem (Join-Path $Out 'video_frames') -Filter *.jpg -ErrorAction SilentlyContinue).Count)"
} finally { Remove-Item -Force $lock -ErrorAction SilentlyContinue }
