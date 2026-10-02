param([string[]] $Only = @())
$ErrorActionPreference = 'Continue'
$script = 'C:\Users\d.grab\Desktop\the-game\capture.ps1'
$outRoot = 'C:\Users\d.grab\Desktop\the-game\artifacts\capture\reels-drafts'
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null

$common = @{ Reels=$true; Video=$true; SilentVideo=$true; NoRebuild=$true; WorkspaceName='mobsv2' }
$runs = [ordered]@{
  'k-guardian'    = @{ Encounter='forest-guardian';    EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.4; VideoDuration=4; Times='6.6' }
  'k-bud'         = @{ Encounter='forest-bud';         EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.4; VideoDuration=4; Times='6.6' }
  'k-stonehoof'   = @{ Encounter='forest-stonehoof';   EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.4; VideoDuration=4; Times='6.6' }
  'k-thorncaster' = @{ Encounter='forest-thorncaster'; EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.4; VideoDuration=4; Times='6.6' }
  'k-snarer'      = @{ Encounter='forest-snarer';      EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.4; VideoDuration=4; Times='6.6' }
  'k-splitter'    = @{ Encounter='forest-splitter';    EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.4; VideoDuration=4; Times='6.6' }
  'k-wendigo'     = @{ Encounter='forest-wendigo';     EnemyCase='death'; Enemies=1; ReelsZoom=0.7; VideoStart=2.4; VideoDuration=4; Times='6.6' }
  'k-swarm'       = @{ Encounter='root-swarm'; HitTier='kill'; ActiveEnemies=$true; Enemies=8; ReelsZoom=0.7; VideoStart=0.3; VideoDuration=5.5; Times='6' }
  'h-wendigo-howl'  = @{ Encounter='forest-wendigo'; Enemies=1; VideoStart=0.5; VideoDuration=6; Times='6.5'; ExtraArgs=@('-capture-wendigo-howl') }
  'h-wendigo-sweep' = @{ Encounter='forest-wendigo'; EnemyCase='turn'; Enemies=1; VideoStart=1; VideoDuration=8; Times='9' }
  'h-stonehoof-hug' = @{ Encounter='forest-stonehoof'; EnemyCase='hug'; Enemies=1; VideoStart=1; VideoDuration=7; Times='8' }
  'h-bud-puddle'    = @{ Encounter='forest-bud'; ForestBudCase='puddle'; Enemies=1; VideoStart=11; VideoDuration=5; Times='16.5' }
  'h-snarer-tank'   = @{ Encounter='forest-snarer'; EnemyCase='tank'; Enemies=2; VideoStart=0.5; VideoDuration=7; Times='8' }
  'z-guardian'    = @{ Encounter='forest-guardian';    EnemyCase='death'; Enemies=1; ReelsZoom=0.5; VideoStart=2.8; VideoDuration=3; Times='6' }
  'z-bud'         = @{ Encounter='forest-bud';         EnemyCase='death'; Enemies=1; ReelsZoom=0.5; VideoStart=2.8; VideoDuration=3; Times='6' }
  'z-stonehoof'   = @{ Encounter='forest-stonehoof';   EnemyCase='death'; Enemies=1; ReelsZoom=0.5; VideoStart=2.8; VideoDuration=3; Times='6' }
  'z-thorncaster' = @{ Encounter='forest-thorncaster'; EnemyCase='death'; Enemies=1; ReelsZoom=0.5; VideoStart=2.8; VideoDuration=3; Times='6' }
  'z-snarer'      = @{ Encounter='forest-snarer';      EnemyCase='death'; Enemies=1; ReelsZoom=0.5; VideoStart=2.8; VideoDuration=3; Times='6' }
  'z-splitter'    = @{ Encounter='forest-splitter';    EnemyCase='death'; Enemies=1; ReelsZoom=0.5; VideoStart=2.8; VideoDuration=3; Times='6' }
  'z-wendigo'     = @{ Encounter='forest-wendigo';     EnemyCase='death'; Enemies=1; ReelsZoom=0.5; VideoStart=2.8; VideoDuration=3; Times='6' }
  'z-swarm'       = @{ Encounter='root-swarm'; HitTier='kill'; ActiveEnemies=$true; Enemies=8; ReelsZoom=0.5; VideoStart=0.3; VideoDuration=5.5; Times='6' }
  'h-all-tank'      = @{ Encounter='forest-thorncaster'; EnemyCase='tank'; Enemies=1; ReelsZoom=1.0; VideoStart=0.5; VideoDuration=9; Times='10'; ExtraArgs=@('-capture-all-forest') }
  'h-all-fight'     = @{ Encounter='forest-thorncaster'; EnemyCase='death'; Enemies=1; ReelsZoom=0.9; VideoStart=0.5; VideoDuration=10; Times='11'; ExtraArgs=@('-capture-all-forest') }
  'h-swarm-crowd'   = @{ Encounter='root-swarm'; HitTier='normal'; ActiveEnemies=$true; Enemies=8; VideoStart=0.4; VideoDuration=6; Times='6.5' }
}
foreach ($name in $runs.Keys) {
  if ($Only.Count -gt 0 -and -not ($Only -contains $name)) { continue }
  $p = @{}
  foreach ($k in $common.Keys) { $p[$k] = $common[$k] }
  foreach ($k in $runs[$name].Keys) { $p[$k] = $runs[$name][$k] }
  $p['OutDir'] = Join-Path $outRoot $name
  $t0 = Get-Date
  Write-Host "=== $name start $($t0.ToString('HH:mm:ss'))"
  try { & $script @p } catch { Write-Host "!!! $name error: $_" }
  $n = (Get-ChildItem (Join-Path $p['OutDir'] 'video_frames') -Filter *.jpg -ErrorAction SilentlyContinue | Measure-Object).Count
  Write-Host ("=== $name done in {0:N0}s, frames={1}" -f ((Get-Date) - $t0).TotalSeconds, $n)
}
Write-Host 'ALL DONE'
