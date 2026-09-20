[CmdletBinding()]
param([switch]$AuditOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$capture = Join-Path $root 'capture.ps1'
$cases = @(
    @{ Name='gameplay'; Enemies=1 },
    @{ Name='front'; Enemies=0; CameraPitch=10; CameraYaw=-90; CameraSize=3.4 },
    @{ Name='north'; Enemies=1; CastYaw=90 },
    @{ Name='south'; Enemies=1; CastYaw=-90 },
    @{ Name='west'; Enemies=1; CastYaw=180 },
    @{ Name='fast'; Enemies=0; SlamCase='fast'; VideoFps=120 },
    @{ Name='fps30'; Enemies=0; VideoFps=30 },
    @{ Name='fps120'; Enemies=0; VideoFps=120 },
    @{ Name='roll'; Enemies=0; SlamCase='roll' },
    @{ Name='ability'; Enemies=0; SlamCase='ability' },
    @{ Name='death'; Enemies=1; DeathDuringSkill=$true },
    @{ Name='repeat'; Enemies=0; SlamCase='repeat'; VideoDuration=6 },
    @{ Name='autoattack'; Enemies=1; SlamCase='autoattack' },
    @{ Name='run'; Enemies=0; Run=$true }
)
$results = @()
foreach ($case in $cases) {
    $name = $case.Name
    $directory = Join-Path $root ('artifacts/anchor-slam-qa/' + $name)
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $arguments = @{
        WorkspaceName='anchor-slam'; NoRebuild=$true; Skill='anchor-slam'; LiveSkill=$true
        NoVfx=$true; SilentVideo=$true; Video=$true; VideoStart=0; VideoDuration=2
        VideoFps=60; Width=1280; Height=720; Times='0.85,1.10,1.40,1.65'; CameraSize=3.8; OutDir=$directory
    }
    foreach ($key in $case.Keys) { if ($key -ne 'Name') { $arguments[$key] = $case[$key] } }
    Write-Output "Capture $name"
    if (-not $AuditOnly) { & $capture @arguments *> (Join-Path $directory 'capture.log') }
    $log = Get-Content -LiteralPath (Join-Path $directory 'player.log') -Raw
    $strains = [regex]::Matches($log, 'strain=([0-9,.]+)') | ForEach-Object {
        [double]::Parse($_.Groups[1].Value.Replace(',','.'), [Globalization.CultureInfo]::InvariantCulture)
    }
    $maximum = ($strains | Measure-Object -Maximum).Maximum
    $release = [regex]::Matches($log, '\[anchor-release\][^\r\n]+')
    $exceptions = $log -match '(NullReferenceException|IndexOutOfRangeException|MissingReferenceException)'
    $expectedReleases = if ($name -eq 'repeat') { 2 } else { 1 }
    $cancelled = $name -in @('roll','ability','death')
    $contactCorrect = if ($name -in @('roll','ability')) { $release.Value -match 'contact=False' } else { $release.Value -match 'contact=True' }
    $handoffs = [regex]::Matches(($release.Value -join ' '), 'handoff=([0-9,.]+)') | ForEach-Object {
        [double]::Parse($_.Groups[1].Value.Replace(',','.'), [Globalization.CultureInfo]::InvariantCulture)
    }
    $handoffCorrect = $cancelled -or ($handoffs.Count -eq $expectedReleases -and ($handoffs | Measure-Object -Maximum).Maximum -le .002)
    $passed = $strains.Count -gt 0 -and $maximum -le .02 -and $release.Count -eq $expectedReleases -and -not $exceptions -and $contactCorrect -and $handoffCorrect
    $results += [PSCustomObject]@{ Case=$name; Frames=$strains.Count; MaxStrain=$maximum; Passed=$passed; Releases=($release.Value -join ' | ') }
    $results | Export-Csv -LiteralPath (Join-Path $root 'artifacts/anchor-slam-qa/results.csv') -NoTypeInformation -Encoding utf8
    Write-Output "$name strain=$maximum passed=$passed"
}
if ($results.Passed -contains $false) { throw 'Есть непройденные проверки Удара якорем; см. results.csv и кадры.' }
