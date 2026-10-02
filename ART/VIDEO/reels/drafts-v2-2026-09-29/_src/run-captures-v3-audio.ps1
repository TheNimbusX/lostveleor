param([string[]] $Only = @())
# Досъёмка 29.09 для «Увернёшься?» и «Босс v2»: те же прогоны, что в run-captures-v2.ps1
# (тот же сид и аргументы — события на тех же тиках), но с -capture-audio-log:
# плеер съёмки звук не пишет (AudioRenderer недоступен, wav выходит тихим), поэтому берём
# журнал [audio-log] — какой клип, с какой громкостью и на каком тике сыграла игра, —
# и по нему кладём наши звуки в монтаже. Кадры не нужны: после прогона удаляются.
# Запуск из PowerShell по абсолютному пути; сборка не нужна (-NoRebuild).
$ErrorActionPreference = 'Continue'
$script = 'C:\Users\d.grab\Desktop\the-game\capture.ps1'
$outRoot = 'C:\Users\d.grab\Desktop\the-game\artifacts\capture\reels-v3-audiolog'
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null

$common = @{ Reels=$true; NoBars=$true; Video=$true; SilentVideo=$true; WorkspaceName='mobsv2'; VideoFps=60; NoRebuild=$true }
$runs = [ordered]@{
  'dodge-stonehoof-z10'    = @{ Encounter='forest-stonehoof'; EnemyCase='dodge'; Enemies=1; ReelsZoom=1.0; VideoStart=0.5; VideoDuration=8 }
  'dodge-snarer-z09'       = @{ Encounter='forest-snarer'; EnemyCase='dodge'; Enemies=1; ReelsZoom=0.9; VideoStart=0.5; VideoDuration=8 }
  'dodge-thorncaster-z10'  = @{ Encounter='forest-thorncaster'; EnemyCase='dodge'; Enemies=1; ReelsZoom=1.0; VideoStart=0.5; VideoDuration=8 }
  'dodge-wendigo-turn-z10' = @{ Encounter='forest-wendigo'; EnemyCase='turn'; Enemies=1; ReelsZoom=1.0; VideoStart=0.5; VideoDuration=9 }
  'after-wendigo-howl'     = @{ Encounter='forest-wendigo'; Enemies=1; ReelsZoom=0.8; VideoStart=0.5; VideoDuration=6; ExtraArgs=@('-capture-wendigo-howl') }
  'boss-hero-charge'       = @{ Encounter='forest-stonehoof'; EnemyCase='tank'; Enemies=1; ReelsZoom=0.7; VideoStart=0.5; VideoDuration=7 }
  'boss-hero-roots'        = @{ Encounter='forest-snarer'; EnemyCase='tank'; Enemies=1; ReelsZoom=0.7; VideoStart=0.5; VideoDuration=7 }
  'kill-wendigo-z07'       = @{ Encounter='forest-wendigo'; EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.0; VideoDuration=4.5 }
  # Босс v2: первый кадр нарезки — Страж на рунном круге (замах, удар героя).
  'kill-guardian-z07'      = @{ Encounter='forest-guardian'; EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.0; VideoDuration=4.5 }
}
foreach ($name in $runs.Keys) {
  if ($Only.Count -gt 0 -and -not ($Only -contains $name)) { continue }
  $p = @{}
  foreach ($k in $common.Keys) { $p[$k] = $common[$k] }
  foreach ($k in $runs[$name].Keys) { $p[$k] = $runs[$name][$k] }
  $extra = @('-capture-audio-log')
  if ($p.ContainsKey('ExtraArgs')) { $extra += $p['ExtraArgs'] }
  $p['ExtraArgs'] = $extra
  $out = Join-Path $outRoot $name
  if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
  $p['OutDir'] = $out
  $end = [double]$p['VideoStart'] + [double]$p['VideoDuration']
  $p['Times'] = $end.ToString([Globalization.CultureInfo]::InvariantCulture)
  $t0 = Get-Date
  Write-Host "=== $name start $($t0.ToString('HH:mm:ss'))"
  try { & $script @p } catch { Write-Host "!!! $name capture.ps1 error: $_" }
  $frames = Join-Path $out 'video_frames'
  $n = (Get-ChildItem $frames -Filter *.jpg -ErrorAction SilentlyContinue | Measure-Object).Count
  # Нужен только журнал: кадры и ролики этого прогона не храним.
  Remove-Item -LiteralPath $frames -Recurse -Force -ErrorAction SilentlyContinue
  Get-ChildItem $out -Filter *.mp4 -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
  $log = Join-Path $out 'player.log'
  $lines = 0
  if (Test-Path -LiteralPath $log) { $lines = (Select-String -LiteralPath $log -Pattern '\[audio-log\]' | Measure-Object).Count }
  Write-Host ("=== $name done in {0:N0}s, frames={1}, audio-log lines={2}" -f ((Get-Date) - $t0).TotalSeconds, $n, $lines)
}
Write-Host 'ALL DONE'
