param([string[]] $Only = @(), [switch] $RebuildFirst, [switch] $KeepFrames, [string] $Seed = '')
# -Seed N: another rift map for the normal-run shots (root swarm); output goes to <shot>-sN.
# Reels v2 (29.09): the whole shot list in reels mode without HP bars (-NoBars).
# Run from PowerShell by absolute path. The first call may pass -RebuildFirst (one rebuild).
$ErrorActionPreference = 'Continue'
$script = 'C:\Users\d.grab\Desktop\the-game\capture.ps1'
$outRoot = 'C:\Users\d.grab\Desktop\the-game\artifacts\capture\reels-v2'
$ffmpeg = 'C:\Users\d.grab\Desktop\the-game\artifacts\tools\python\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe'
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null

$common = @{ Reels=$true; NoBars=$true; Video=$true; SilentVideo=$true; WorkspaceName='mobsv2'; VideoFps=60 }
$runs = [ordered]@{
  # KILLS: one clean death each, zoom 0.7 and 0.9
  'kill-guardian-z07'    = @{ Encounter='forest-guardian';    EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.0; VideoDuration=4.5 }
  'kill-guardian-z09'    = @{ Encounter='forest-guardian';    EnemyCase='death'; Enemies=1; ReelsZoom=0.9; VideoStart=2.0; VideoDuration=4.5 }
  'kill-bud-z07'         = @{ Encounter='forest-bud';         EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.0; VideoDuration=5 }
  'kill-bud-z09'         = @{ Encounter='forest-bud';         EnemyCase='death'; Enemies=1; ReelsZoom=0.9; VideoStart=2.0; VideoDuration=5 }
  'kill-stonehoof-z07'   = @{ Encounter='forest-stonehoof';   EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.0; VideoDuration=5 }
  'kill-stonehoof-z09'   = @{ Encounter='forest-stonehoof';   EnemyCase='death'; Enemies=1; ReelsZoom=0.9; VideoStart=2.0; VideoDuration=5 }
  'kill-thorncaster-z07' = @{ Encounter='forest-thorncaster'; EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.0; VideoDuration=5 }
  'kill-thorncaster-z09' = @{ Encounter='forest-thorncaster'; EnemyCase='death'; Enemies=1; ReelsZoom=0.9; VideoStart=2.0; VideoDuration=5 }
  'kill-snarer-z07'      = @{ Encounter='forest-snarer';      EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.0; VideoDuration=5 }
  'kill-snarer-z09'      = @{ Encounter='forest-snarer';      EnemyCase='death'; Enemies=1; ReelsZoom=0.9; VideoStart=2.0; VideoDuration=5 }
  'kill-splitter-z07'    = @{ Encounter='forest-splitter';    EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.0; VideoDuration=7.5 }
  'kill-splitter-z09'    = @{ Encounter='forest-splitter';    EnemyCase='death'; Enemies=1; ReelsZoom=0.9; VideoStart=2.0; VideoDuration=7.5 }
  'kill-wendigo-z07'     = @{ Encounter='forest-wendigo';     EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.0; VideoDuration=4.5 }
  'kill-wendigo-z09'     = @{ Encounter='forest-wendigo';     EnemyCase='death'; Enemies=1; ReelsZoom=0.9; VideoStart=2.0; VideoDuration=4.5 }
  'kill-swarm-z07'       = @{ Encounter='root-swarm'; HitTier='kill'; ActiveEnemies=$true; Enemies=8; ReelsZoom=0.7; VideoStart=0; VideoDuration=5 }
  'kill-swarm-z09'       = @{ Encounter='root-swarm'; HitTier='kill'; ActiveEnemies=$true; Enemies=8; ReelsZoom=0.9; VideoStart=0; VideoDuration=5 }
  # root swarm on a clean glade: the combat-feel swarm always spawns on the entry portal ring (any seed),
  # so the readable swarm kill comes from the Wendigo stand with its 2-swarm pack (Wendigo dies first)
  'kill-swarm-pack-z07'  = @{ Encounter='forest-wendigo'; EnemyCase='death'; Enemies=2; ReelsZoom=0.7; VideoStart=2.0; VideoDuration=9 }
  'kill-swarm-pack-z09'  = @{ Encounter='forest-wendigo'; EnemyCase='death'; Enemies=2; ReelsZoom=0.9; VideoStart=2.0; VideoDuration=9 }
  'after-multikill-z05'  = @{ Encounter='root-swarm'; HitTier='kill'; ActiveEnemies=$true; Enemies=12; ReelsZoom=0.5; VideoStart=0; VideoDuration=4 }
  'boss-hero-swarm-z06'  = @{ Encounter='root-swarm'; HitTier='normal'; ActiveEnemies=$true; Enemies=16; ReelsZoom=0.6; VideoStart=0; VideoDuration=6 }
  'boss-hero-wendigo-pack' = @{ Encounter='forest-wendigo'; EnemyCase='tank'; Enemies=2; ReelsZoom=0.8; VideoStart=0.3; VideoDuration=7 }
  # AFTER: today's game, 3-5 s spectacular pieces
  'after-crowd-fight'    = @{ Encounter='forest-thorncaster'; EnemyCase='death'; Enemies=1; ReelsZoom=0.9; VideoStart=0.5; VideoDuration=11.5; ExtraArgs=@('-capture-all-forest') }
  'after-crowd-tank'     = @{ Encounter='forest-thorncaster'; EnemyCase='tank'; Enemies=1; ReelsZoom=1.0; VideoStart=0.5; VideoDuration=9; ExtraArgs=@('-capture-all-forest') }
  'after-wendigo-howl'   = @{ Encounter='forest-wendigo'; Enemies=1; ReelsZoom=0.8; VideoStart=0.5; VideoDuration=6; ExtraArgs=@('-capture-wendigo-howl') }
  'after-wendigo-leap'   = @{ Encounter='forest-wendigo'; EnemyCase='tank'; Enemies=1; ReelsZoom=0.9; VideoStart=0.3; VideoDuration=6 }
  'after-stonehoof-charge' = @{ Encounter='forest-stonehoof'; EnemyCase='tank'; Enemies=1; ReelsZoom=0.9; VideoStart=0.5; VideoDuration=7 }
  'after-multikill'      = @{ Encounter='root-swarm'; HitTier='kill'; ActiveEnemies=$true; Enemies=12; ReelsZoom=0.8; VideoStart=0; VideoDuration=6 }
  # BOSS v2 atmosphere: calm arena + mob hero shots
  'boss-calm-afterclear' = @{ Encounter='forest-guardian'; EnemyCase='death'; Enemies=1; ReelsZoom=1.2; VideoStart=5.0; VideoDuration=8 }
  'boss-calm-empty'      = @{ Enemies=0; ReelsZoom=1.3; VideoStart=0.5; VideoDuration=8 }
  'boss-hero-howl'       = @{ Encounter='forest-wendigo'; Enemies=1; ReelsZoom=0.65; VideoStart=0.5; VideoDuration=6; ExtraArgs=@('-capture-wendigo-howl') }
  'boss-hero-charge'     = @{ Encounter='forest-stonehoof'; EnemyCase='tank'; Enemies=1; ReelsZoom=0.7; VideoStart=0.5; VideoDuration=7 }
  'boss-hero-roots'      = @{ Encounter='forest-snarer'; EnemyCase='tank'; Enemies=1; ReelsZoom=0.7; VideoStart=0.5; VideoDuration=7 }
  'boss-hero-swarm'      = @{ Encounter='root-swarm'; HitTier='normal'; ActiveEnemies=$true; Enemies=16; ReelsZoom=0.9; VideoStart=0; VideoDuration=6 }
  # DODGE CHALLENGE: telegraph -> impact, hero dodges; Wendigo 360 beat = hero gets hit (turn)
  'dodge-stonehoof-z08'  = @{ Encounter='forest-stonehoof'; EnemyCase='dodge'; Enemies=1; ReelsZoom=0.8; VideoStart=0.5; VideoDuration=8 }
  'dodge-stonehoof-z10'  = @{ Encounter='forest-stonehoof'; EnemyCase='dodge'; Enemies=1; ReelsZoom=1.0; VideoStart=0.5; VideoDuration=8 }
  'dodge-snarer-z07'     = @{ Encounter='forest-snarer'; EnemyCase='dodge'; Enemies=1; ReelsZoom=0.7; VideoStart=0.5; VideoDuration=8 }
  'dodge-snarer-z09'     = @{ Encounter='forest-snarer'; EnemyCase='dodge'; Enemies=1; ReelsZoom=0.9; VideoStart=0.5; VideoDuration=8 }
  'dodge-thorncaster-z08' = @{ Encounter='forest-thorncaster'; EnemyCase='dodge'; Enemies=1; ReelsZoom=0.8; VideoStart=0.5; VideoDuration=8 }
  'dodge-thorncaster-z10' = @{ Encounter='forest-thorncaster'; EnemyCase='dodge'; Enemies=1; ReelsZoom=1.0; VideoStart=0.5; VideoDuration=8 }
  'dodge-wendigo-turn-z08' = @{ Encounter='forest-wendigo'; EnemyCase='turn'; Enemies=1; ReelsZoom=0.8; VideoStart=0.5; VideoDuration=9 }
  'dodge-wendigo-turn-z10' = @{ Encounter='forest-wendigo'; EnemyCase='turn'; Enemies=1; ReelsZoom=1.0; VideoStart=0.5; VideoDuration=9 }
  'dodge-wendigo-dodge-z09' = @{ Encounter='forest-wendigo'; EnemyCase='dodge'; Enemies=1; ReelsZoom=0.9; VideoStart=0.3; VideoDuration=8 }
}
$first = $true
foreach ($name in $runs.Keys) {
  if ($Only.Count -gt 0 -and -not ($Only -contains $name)) { continue }
  $p = @{}
  foreach ($k in $common.Keys) { $p[$k] = $common[$k] }
  foreach ($k in $runs[$name].Keys) { $p[$k] = $runs[$name][$k] }
  $shot = $name
  if ($Seed -ne '') { $p['Seed'] = [uint64]$Seed; $shot = $name + '-s' + $Seed }
  $out = Join-Path $outRoot $shot
  if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
  $p['OutDir'] = $out
  # one still at the end: capture.ps1 wants at least one PNG; the player quits after it
  $end = [double]$p['VideoStart'] + [double]$p['VideoDuration']
  $p['Times'] = $end.ToString([Globalization.CultureInfo]::InvariantCulture)
  if ($RebuildFirst -and $first) { $p['Rebuild'] = $true } else { $p['NoRebuild'] = $true }
  $first = $false
  $t0 = Get-Date
  Write-Host "=== $name start $($t0.ToString('HH:mm:ss')) rebuild=$($p.ContainsKey('Rebuild'))"
  try { & $script @p } catch { Write-Host "!!! $name capture.ps1 error: $_" }
  $frames = Join-Path $out 'video_frames'
  $n = (Get-ChildItem $frames -Filter *.jpg -ErrorAction SilentlyContinue | Measure-Object).Count
  $mp4 = Get-ChildItem $out -Filter *.mp4 -ErrorAction SilentlyContinue | Select-Object -First 1
  $target = Join-Path $out ($shot + '_1080x1920_60fps.mp4')
  if ($null -eq $mp4 -and $n -gt 0) {
    Write-Host "... $name own mux ($n frames)"
    & $ffmpeg -y -loglevel error -framerate 60 -i (Join-Path $frames 'frame_%04d.jpg') -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -an -movflags +faststart $target
  } elseif ($null -ne $mp4) {
    Move-Item -LiteralPath $mp4.FullName -Destination $target -Force
  }
  if ((Test-Path -LiteralPath $target) -and (Get-Item -LiteralPath $target).Length -gt 100KB -and -not $KeepFrames) {
    Remove-Item -LiteralPath $frames -Recurse -Force -ErrorAction SilentlyContinue
  }
  $ok = Test-Path -LiteralPath $target
  Write-Host ("=== $name done in {0:N0}s, frames={1}, mp4={2}" -f ((Get-Date) - $t0).TotalSeconds, $n, $ok)
}
Write-Host 'ALL DONE'
