<#
    Готовит звуки владельца (Downloads) для игры: срезает тишину в начале и конце,
    моно 48 кГц, ровняет громкость, пишет ogg под игровыми именами.

    Интерфейс -> Resources/Audio/UI/Prepared (банк UiSoundBank заполняется по имени
    файла, см. PauseMenuBuilder.EnsureSoundBank). Игра -> Resources/Audio/Game/Prepared.
    Лагерь -> Resources/Audio/Camp/Prepared (рядом с петлями 13 сентября).

    Длинные записи (ветер, листва, дятел) режутся на несколько коротких одиночных
    звуков: в игре они играют вразнобой, а целая запись звучала бы как один трек.
    Петли (вечерний лес, гул Разлома) идут целиком и без среза тишины.

    Тишина считается от пика самого файла (пик − 48 dB): тихие исходники потом
    усиливаются на 10–18 dB, и порог −50 dB обрезал у них слышимый хвост.
    Громкость: усиление = меньшее из (цель пика − пик) и (цель среднего − среднее) —
    по одному пику плотный свист становился громче тихого щелчка.
#>
param(
    [string] $Source = (Join-Path $env:USERPROFILE 'Downloads'),
    [string] $Root = 'razlom/Assets/Resources/Audio'
)
# Не Stop: PowerShell 5.1 считает ошибкой любую строку ffmpeg в stderr (баннер, «Guessed Channel Layout»).
# Сбои ffmpeg ловятся по $LASTEXITCODE после каждого вызова.
$ErrorActionPreference = 'Continue'

$ffmpeg = (Get-ChildItem 'artifacts/tools/python/imageio_ffmpeg/binaries/ffmpeg-*.exe' | Select-Object -First 1).FullName
if (-not $ffmpeg) { throw 'Нет ffmpeg: запустите capture.ps1 -Video один раз, он ставит imageio-ffmpeg.' }

$invariant = [Globalization.CultureInfo]::InvariantCulture

# Имя, папка, источник, цель пика dB, цель среднего dB, резать тишину, начало с (пусто — сначала), длительность (пусто — целиком)
$sounds = @(
    [pscustomobject]@{ Name='ui_click';       Dir='UI'; File='click.mp3';                Peak=-6; Mean=-24; Trim=$true },
    [pscustomobject]@{ Name='ui_back';        Dir='UI'; File='back.mp3';                 Peak=-6; Mean=-24; Trim=$true },
    [pscustomobject]@{ Name='ui_tab';         Dir='UI'; File='tab switch.mp3';           Peak=-6; Mean=-24; Trim=$true },
    [pscustomobject]@{ Name='ui_toggle';      Dir='UI'; File='toggle switch on off.mp3'; Peak=-6; Mean=-24; Trim=$true },
    [pscustomobject]@{ Name='ui_window_open'; Dir='UI'; File='ui whoosh short,.mp3';     Peak=-6; Mean=-24; Trim=$true },
    [pscustomobject]@{ Name='ui_denied';      Dir='UI'; File='ui error.mp3';             Peak=-6; Mean=-24; Trim=$true },
    # «dropdown open.mp3» 16 сентября совпал байт в байт с «cooldown denied.mp3» — не берём, пока не придёт свой.

    [pscustomobject]@{ Name='ability_denied';  Dir='Game'; File='cooldown denied.mp3';      Peak=-4; Mean=-22; Trim=$true },
    [pscustomobject]@{ Name='mana_empty';      Dir='Game'; File='mana empty.mp3';           Peak=-4; Mean=-22; Trim=$true },
    [pscustomobject]@{ Name='level_up';        Dir='Game'; File='level up stinger.mp3';     Peak=-3; Mean=-20; Trim=$true },
    [pscustomobject]@{ Name='low_health_loop'; Dir='Game'; File='low health heartbeat.mp3'; Peak=-8; Mean=-28; Trim=$false },
    [pscustomobject]@{ Name='potion_drink';    Dir='Game'; File='potion drink.wav';          Peak=-4; Mean=-22; Trim=$true },
    [pscustomobject]@{ Name='loot_drop';       Dir='Game'; File='loot drop.wav';             Peak=-4; Mean=-22; Trim=$true },
    [pscustomobject]@{ Name='rift_portal';     Dir='Game'; File='portal whoosh.mp3';         Peak=-3; Mean=-20; Trim=$true },
    [pscustomobject]@{ Name='map_ping';        Dir='Game'; File='map ping.mp3';              Peak=-6; Mean=-24; Trim=$true },
    [pscustomobject]@{ Name='bag_open';        Dir='Game'; File='leather bag.wav';           Peak=-4; Mean=-22; Trim=$true },
    [pscustomobject]@{ Name='salvage';         Dir='Game'; File='salvage crunch.mp3';        Peak=-4; Mean=-22; Trim=$true },
    [pscustomobject]@{ Name='coins';           Dir='Game'; File='coins.mp3';                 Peak=-4; Mean=-22; Trim=$true },
    [pscustomobject]@{ Name='shop_bell';       Dir='Game'; File='shop bell.wav';             Peak=-4; Mean=-22; Trim=$true },
    [pscustomobject]@{ Name='tent_flap';       Dir='Game'; File='tent flap.wav';             Peak=-4; Mean=-22; Trim=$true }
)

# Лагерь, 16 сентября. Петли — целиком; одиночные звуки нарезаны из длинных записей.
$forest = 'ES_Ambience, Forest, Forest Crickets, Birds, Connecticut - Epidemic Sound.mp3'
$drone  = 'ES_Designed, Drone, Musical, Bass Static Tension - Epidemic Sound.mp3'
$gust   = 'ES_Wind, General, Muffled Wind At, Desert, Bagdad Highway, Wind Gusts, Light Whistling 02 - Epidemic Sound.mp3'
$leaves = 'ES_Wind, Vegetation, Field, Cornfield, Folliage, Leaves, Rustle, Intense 01 - Epidemic Sound.mp3'
$cloth  = 'ES_Cloth, Flap, Flag, Movement, Wind - Epidemic Sound.mp3'
$wood   = 'ES_Ambience, Misc, Birds, Woodpecker, Rythms, Bugyal, High Mountains, Himalaya 01 - Epidemic Sound.mp3'
$crow   = 'ES_Birds, Crow, Ravens, Call, Response Distant - Epidemic Sound.mp3'
$flock  = 'ES_Birds, Misc, Flock, Flapping, Chirps - Epidemic Sound.mp3'

$camp = @(
    [pscustomobject]@{ Name='camp_evening_forest'; Dir='Camp'; File=$forest; Peak=-10; Mean=-32; Trim=$false },
    [pscustomobject]@{ Name='camp_rift_drone';     Dir='Camp'; File=$drone;  Peak=-14; Mean=-34; Trim=$false },
    [pscustomobject]@{ Name='camp_wind_gust_01';   Dir='Camp'; File=$gust;   Peak=-8;  Mean=-28; Trim=$true; Start=18;  Length=5 },
    [pscustomobject]@{ Name='camp_wind_gust_02';   Dir='Camp'; File=$gust;   Peak=-8;  Mean=-28; Trim=$true; Start=74;  Length=5 },
    [pscustomobject]@{ Name='camp_wind_gust_03';   Dir='Camp'; File=$gust;   Peak=-8;  Mean=-28; Trim=$true; Start=132; Length=5 },
    [pscustomobject]@{ Name='camp_leaves_01';      Dir='Camp'; File=$leaves; Peak=-8;  Mean=-28; Trim=$true; Start=22;  Length=4 },
    [pscustomobject]@{ Name='camp_leaves_02';      Dir='Camp'; File=$leaves; Peak=-8;  Mean=-28; Trim=$true; Start=88;  Length=4 },
    [pscustomobject]@{ Name='camp_leaves_03';      Dir='Camp'; File=$leaves; Peak=-8;  Mean=-28; Trim=$true; Start=150; Length=4 },
    [pscustomobject]@{ Name='camp_cloth_01';       Dir='Camp'; File=$cloth;  Peak=-8;  Mean=-28; Trim=$true; Start=4;   Length=3 },
    [pscustomobject]@{ Name='camp_cloth_02';       Dir='Camp'; File=$cloth;  Peak=-8;  Mean=-28; Trim=$true; Start=24;  Length=3 },
    [pscustomobject]@{ Name='camp_cloth_03';       Dir='Camp'; File=$cloth;  Peak=-8;  Mean=-28; Trim=$true; Start=44;  Length=3 },
    [pscustomobject]@{ Name='camp_woodpecker_01';  Dir='Camp'; File=$wood;   Peak=-9;  Mean=-29; Trim=$true; Start=12;  Length=4 },
    [pscustomobject]@{ Name='camp_woodpecker_02';  Dir='Camp'; File=$wood;   Peak=-9;  Mean=-29; Trim=$true; Start=60;  Length=4 },
    [pscustomobject]@{ Name='camp_woodpecker_03';  Dir='Camp'; File=$wood;   Peak=-9;  Mean=-29; Trim=$true; Start=110; Length=4 },
    [pscustomobject]@{ Name='camp_crow_01';        Dir='Camp'; File=$crow;   Peak=-9;  Mean=-29; Trim=$true; Start=0;   Length=6 },
    [pscustomobject]@{ Name='camp_crow_02';        Dir='Camp'; File=$crow;   Peak=-9;  Mean=-29; Trim=$true; Start=9;   Length=6 },
    [pscustomobject]@{ Name='camp_birds_takeoff';  Dir='Camp'; File=$flock;  Peak=-7;  Mean=-27; Trim=$true }
)

function Measure-Level([string] $path) {
    $log = & $ffmpeg -hide_banner -nostats -i $path -af volumedetect -f null NUL 2>&1 | Out-String
    [pscustomobject]@{
        Peak = [double]::Parse([regex]::Match($log, 'max_volume: (\S+) dB').Groups[1].Value, $invariant)
        Mean = [double]::Parse([regex]::Match($log, 'mean_volume: (\S+) dB').Groups[1].Value, $invariant)
    }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) 'razlom-sounds'
New-Item -ItemType Directory -Force $temp | Out-Null
$failed = 0
foreach ($s in ($sounds + $camp)) {
    $src = Join-Path $Source $s.File
    if (-not (Test-Path -LiteralPath $src)) { Write-Host ("  нет файла: {0}" -f $s.File); $failed++; continue }
    $outDir = Join-Path $Root "$($s.Dir)/Prepared"
    New-Item -ItemType Directory -Force $outDir | Out-Null

    # Кусок длинной записи. Затухание по краям 30 мс, иначе на срезе щелчок.
    $cut = @()
    if ($s.PSObject.Properties['Length'] -and $s.Length) {
        $start = [double]$s.Start
        $cut = @('-ss', $start.ToString($invariant), '-t', ([double]$s.Length).ToString($invariant))
    }

    # Порог тишины — от пика исходника; в конце затухание 40 мс, чтобы обрез не щёлкал.
    $threshold = ([Math]::Min(-50, (Measure-Level $src).Peak - 48)).ToString('0.0', $invariant)
    $filters = @()
    if ($s.Trim) {
        $filters += "silenceremove=start_periods=1:start_threshold=${threshold}dB"
        $filters += "areverse"
        $filters += "silenceremove=start_periods=1:start_threshold=${threshold}dB"
        $filters += "afade=t=in:d=0.04"
        $filters += "areverse"
    }
    if ($cut.Count -gt 0) { $filters += 'afade=t=in:d=0.03'; $filters += "afade=t=out:st=$(([double]$s.Length - 0.03).ToString($invariant)):d=0.03" }
    $filters += 'aresample=48000'
    $shaped = Join-Path $temp ($s.Name + '.wav')
    & $ffmpeg -hide_banner -loglevel error -y @cut -i $src -af ($filters -join ',') -ac 1 -c:a pcm_s16le $shaped
    if ($LASTEXITCODE -ne 0) { Write-Host "  ffmpeg не обработал $($s.Name)"; $failed++; continue }

    $level = Measure-Level $shaped
    $gain = [Math]::Min($s.Peak - $level.Peak, $s.Mean - $level.Mean)
    $out = Join-Path $outDir ($s.Name + '.ogg')
    $gainText = $gain.ToString('0.0', $invariant)
    & $ffmpeg -hide_banner -loglevel error -y -i $shaped -af "volume=${gainText}dB,alimiter=limit=0.95:level=false" -c:a libvorbis -q:a 6 $out
    if ($LASTEXITCODE -ne 0) { Write-Host "  ffmpeg не записал $out"; $failed++; continue }

    $final = Measure-Level $out
    $seconds = [double]::Parse(((& $ffmpeg -hide_banner -i $out 2>&1 | Out-String) -replace '(?s).*Duration: 00:00:([\d.]+).*', '$1'), $invariant)
    '{0,-22} {1,-5} {2,6:0.00} с  усиление {3,6:+0.0;-0.0} dB  пик {4,5:0.0}  среднее {5,5:0.0}' -f $s.Name, $s.Dir, $seconds, $gain, $final.Peak, $final.Mean
}
Remove-Item -Recurse -Force $temp
exit $failed
