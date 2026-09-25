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
    [string] $Root = 'razlom/Assets/Resources/Audio',
    # camp-sfx — только пак владельца из ART/camp-sfxs (22 сентября); ui-pack — звуки интерфейса
    # и моментов забега из ART/ui-sfx (25 сентября); пусто — всё остальное.
    [ValidateSet('', 'camp-sfx', 'ui-pack')] [string] $Group = ''
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

# Действия лагеря, 22 сентября: пак владельца в ART/camp-sfxs. Серии ударов нарезаны на одиночные
# варианты (_01, _02 …): GameSound.Play по имени без номера берёт случайный, не повторяя подряд.
# Магия (зелья, Разлом) — тише, вторым слоем под стеклом и жидкостью.
$sfx = 'ART/camp-sfxs'
function Sfx($name, $file, $peak, $mean, $start = $null, $length = $null) {
    $o = [pscustomobject]@{ Name=$name; Dir='Game'; File=$file; Src=$sfx; Peak=$peak; Mean=$mean; Trim=$true }
    # Окно куска выбрано по записи; срез тишины по порогу до усиления отрезал бы слышимый хвост удара.
    if ($null -ne $length) { $o | Add-Member Start $start; $o | Add-Member Length $length; $o.Trim = $false }
    $o
}
$grassSteps = 'ES_Footsteps, Human, Boots, Grass, Jog, Run, Distant - Epidemic Sound.mp3'
$woodSteps  = 'ES_Footsteps, Human, Shoes, Wood, Walk 02 - Epidemic Sound.mp3'
$boards     = 'ES_Wood, Friction, Old Cabin Squeaking Floorboards 02 - Epidemic Sound.mp3'
$bag        = 'ES_Objects, Bag, Leather Bag, Grab, Squish, Fill Up - Epidemic Sound.mp3'
$anvil      = 'ES_Tools, Hand, Hammer, Anvil, Impacts, Blacksmith, Indoors, Close - Epidemic Sound.mp3'
$brace      = 'ES_Metal, Impact, Steel, Roof Brace, Drop On Wooden Floor x5 - Epidemic Sound.mp3'
$helm       = 'ES_Weapons, Armor, Armour, Hand Axe Hitting Steel Helm - Epidemic Sound.mp3'
$coinBox    = 'ES_Objects, Coin, Ceramic, Box, Pour, Movement - Epidemic Sound.mp3'
$cards      = 'ES_Games, Misc, Playing Card, Dealing Table, Flip, Singles - Epidemic Sound.mp3'
$bottles    = 'ES_Magic, Misc, Potion Bottle, Put Down 06 - Epidemic Sound.mp3'
$clink      = 'ES_Glass, Impact, Bottle, Hit, Clink, Tonal, Clean Small - Epidemic Sound.mp3'
$shield     = 'ES_Weapons, Misc, Spear Thrust, Shield Boss, Wooden - Epidemic Sound.mp3'
$creak      = 'ES_Wood, Friction, Creak, Old Wooden Furniture, Short, Dry 03 - Epidemic Sound.mp3'
$portal     = 'ES_Magic, Spell, Dark Portal, Open & Close, Ghostly Whispers, Evil, Mysterious - Epidemic Sound - 0000-16404.wav'

$campSfx = @(
    # Шаги по траве и земле — отдельные шаги из пробежки, тихо.
    (Sfx 'camp_step_grass_01' $grassSteps -12 -32 2.08 .34), (Sfx 'camp_step_grass_02' $grassSteps -12 -32 2.44 .34),
    (Sfx 'camp_step_grass_03' $grassSteps -12 -32 2.82 .34), (Sfx 'camp_step_grass_04' $grassSteps -12 -32 3.21 .34),
    (Sfx 'camp_step_grass_05' $grassSteps -12 -32 3.59 .34), (Sfx 'camp_step_grass_06' $grassSteps -12 -32 3.97 .34),
    (Sfx 'camp_step_grass_07' $grassSteps -12 -32 4.36 .34), (Sfx 'camp_step_grass_08' $grassSteps -12 -32 4.73 .34),
    # Мост: шаги по доскам и редкий скрип настила.
    (Sfx 'camp_step_wood_01' $woodSteps -10 -30 0.05 .45), (Sfx 'camp_step_wood_02' $woodSteps -10 -30 0.61 .45),
    (Sfx 'camp_step_wood_03' $woodSteps -10 -30 1.14 .45), (Sfx 'camp_step_wood_04' $woodSteps -10 -30 1.68 .45),
    (Sfx 'camp_step_wood_05' $woodSteps -10 -30 2.16 .45), (Sfx 'camp_step_wood_06' $woodSteps -10 -30 2.67 .45),
    (Sfx 'camp_bridge_creak_01' $boards -12 -32 2.45 1.2), (Sfx 'camp_bridge_creak_02' $boards -12 -32 8.70 1.2),
    # Палатка: ткань и шорох сумки.
    (Sfx 'tent_cloth' 'ES_Cloth, Flap, Flap, Blanket, Whoosh - Epidemic Sound.mp3' -8 -28),
    (Sfx 'tent_rustle_01' $bag -8 -28 0.20 1.6), (Sfx 'tent_rustle_02' $bag -8 -28 1.90 1.7),
    # Кузнец: молот, пар, звон готовой вещи; разбор — лом и осыпающиеся детали.
    (Sfx 'smith_hammer_01' $anvil -5 -24 3.45 .6), (Sfx 'smith_hammer_02' $anvil -5 -24 0.90 .6),
    (Sfx 'smith_hammer_03' $anvil -5 -24 1.55 .6), (Sfx 'smith_hammer_04' $anvil -5 -24 2.18 .6),
    (Sfx 'smith_sizzle' 'ES_Water, Steam, Sizzle On Hot Metal Plate 02 - Epidemic Sound.mp3' -9 -28),
    (Sfx 'smith_ring' 'ES_Metal, Friction, Metal, Spade, Ring - Epidemic Sound - 3795-5766.wav' -8 -28),
    (Sfx 'smith_break_01' $helm -6 -24 0.01 .85), (Sfx 'smith_break_02' $helm -6 -24 0.94 .85),
    (Sfx 'smith_debris_01' $brace -7 -26 0.07 .5), (Sfx 'smith_debris_02' $brace -7 -26 1.41 .5), (Sfx 'smith_debris_03' $brace -7 -26 2.35 .5),
    (Sfx 'smith_crash' 'ES_Metal, Crash & Debris, Impact, Small Items, Drop, Clash - Epidemic Sound.mp3' -7 -26),
    # Торговец: кошель, пересчёт, монеты на стойке, вещь в ящик, перекладка товара, тихий акцент.
    (Sfx 'trader_pouch' 'ES_Objects, Coin, Coins, Money Pouch, Fabric, Movement, Shake 01 - Epidemic Sound.mp3' -7 -26),
    (Sfx 'trader_count_01' $coinBox -8 -27 0.15 1.7), (Sfx 'trader_count_02' $coinBox -8 -27 3.70 1.2),
    (Sfx 'trader_coins_table_01' 'ES_Objects, Coin, Money, Coins, Handful, Down On Table - Epidemic Sound.mp3' -7 -26),
    (Sfx 'trader_coins_table_02' 'ES_Objects, Coin, Money, Pound, Coin, Down On Table - Epidemic Sound.mp3' -7 -26),
    (Sfx 'trader_crate_01' 'ES_Objects, Furniture, Impact, Wood Table, Desk, Hit, Items On Top Rattle 01 - Epidemic Sound.mp3' -8 -27),
    (Sfx 'trader_crate_02' 'ES_Wood, Impact, Wooden Blocks, Small, Place Down On Others - Epidemic Sound.mp3' -8 -27),
    (Sfx 'trader_shuffle' 'ES_Games, Misc, Playing Cards, Shuffle Fast - Epidemic Sound.mp3' -9 -28 0 1.4),
    (Sfx 'trader_flip_01' $cards -9 -28 0.24 .6), (Sfx 'trader_flip_02' $cards -9 -28 1.52 .6),
    (Sfx 'trader_chime' 'ES_Clocks, Chime, Chime Rods, Ringing 01 - Epidemic Sound - 0000-1761.wav' -14 -34),
    # Алхимик: бутылка на стол и звон стекла; выбор — пробка, переливание, тихое бурление.
    (Sfx 'alch_bottle_01' $bottles -7 -26 0.01 1.1), (Sfx 'alch_bottle_02' $bottles -7 -26 1.22 1.1), (Sfx 'alch_bottle_03' $bottles -7 -26 2.45 1.1),
    (Sfx 'alch_clink_01' $clink -10 -30 0.04 1.6), (Sfx 'alch_clink_02' $clink -10 -30 2.73 1.6),
    (Sfx 'alch_cork_01' 'ES_Food & Drink, Glassware, Bottle, Glass, Cork, Open, Pop - Epidemic Sound.mp3' -8 -27),
    (Sfx 'alch_cork_02' 'ES_Food & Drink, Glassware, Bottle, Wine, Cork, Pop Open - Epidemic Sound.mp3' -8 -27),
    (Sfx 'alch_pour_01' 'ES_Food & Drink, Pour, Milk, Poured Into Glass - Epidemic Sound.mp3' -9 -28),
    (Sfx 'alch_pour_02' 'ES_Food & Drink, Pour, Pour Small Amount From Jug Into Small Glass - Epidemic Sound.mp3' -9 -28),
    (Sfx 'alch_bubble_01' 'ES_Water, Bubbles, Underwater, Bubble, Single, Tonal 01 - Epidemic Sound.mp3' -14 -34 0 1.4),
    (Sfx 'alch_bubble_02' 'ES_Water, Bubbles, Underwater, Bubble, Single, Tonal 03 - Epidemic Sound.mp3' -14 -34 0 1.4),
    # Зелье в бою: глоток и тонкий отклик ресурса.
    (Sfx 'potion_gulp_01' 'ES_Food & Drink, Drinking, Human, Swallow, Gulp - Epidemic Sound.mp3' -7 -26),
    (Sfx 'potion_gulp_02' 'ES_Food & Drink, Drinking, Human, Swallowing, Loud - Epidemic Sound.mp3' -7 -26),
    (Sfx 'potion_heal' 'ES_Magic, Angelic, Spell, Cast, Buff, Power Up, Holy, Healing, Twinkle, Glimmer, Positive 01 - Epidemic Sound.mp3' -14 -34 0.1 1.8),
    (Sfx 'potion_lavidium' 'ES_Magic, Spell, Soft Airy, Chimes, Bells - Epidemic Sound.mp3' -14 -34),
    # Манекены: удар по дереву и сухой скрип стойки.
    (Sfx 'dummy_hit_01' $shield -7 -26 0.09 1.0), (Sfx 'dummy_hit_02' $shield -7 -26 2.03 1.0), (Sfx 'dummy_hit_03' $shield -7 -26 4.06 1.0),
    (Sfx 'dummy_creak_01' $creak -10 -30 0.03 .5), (Sfx 'dummy_creak_02' $creak -10 -30 0.59 .45),
    # Вход в Разлом: пробуждение арки и короткий переход.
    (Sfx 'rift_awaken' $portal -10 -30 0.14 5),
    (Sfx 'rift_whoosh' 'ES_Swooshes, Whoosh, Eerie, Anxiety, Tonal, Mystic, Fast 02 - Epidemic Sound - 4521-7037.wav' -8 -28)
)

# Интерфейс и моменты забега, 25 сентября (аудит UI, этап 2): «400 Sounds Pack» владельца из Downloads,
# выбранные файлы лежат в ART/ui-sfx. Наведение и шаг ползунка — тихие щелчки; пауза — книга, окно — карта;
# концы забега и события арены — одна семья, рояль и клавесин.
$ui = 'ART/ui-sfx'
function Ui($name, $dir, $file, $peak, $mean) { [pscustomobject]@{ Name=$name; Dir=$dir; File=$file; Src=$ui; Peak=$peak; Mean=$mean; Trim=$true } }
$uiPack = @(
    (Ui 'ui_hover_01' 'UI' 'select_1.wav' -16 -36), (Ui 'ui_hover_02' 'UI' 'select_2.wav' -16 -36),
    (Ui 'ui_hover_03' 'UI' 'select_3.wav' -16 -36), (Ui 'ui_hover_04' 'UI' 'select_4.wav' -16 -36),
    (Ui 'ui_slider' 'UI' 'pop_1.wav' -14 -34),
    (Ui 'ui_list_open' 'UI' 'click_double_on.wav' -9 -29), (Ui 'ui_list_close' 'UI' 'click_double_off.wav' -9 -29),
    (Ui 'ui_window_close' 'UI' 'map_close.wav' -8 -28),
    (Ui 'ui_pause_open' 'UI' 'book_open.wav' -8 -28), (Ui 'ui_pause_close' 'UI' 'book_close.wav' -8 -28),
    (Ui 'ui_key_waiting' 'UI' 'pop_2.wav' -9 -29),
    (Ui 'run_death' 'Game' 'grand_piano_defeated.wav' -6 -24),
    (Ui 'run_victory' 'Game' 'grand_piano_level_complete.wav' -6 -24),
    (Ui 'run_leave' 'Game' 'grand_piano_chime_positive.wav' -7 -26),
    (Ui 'arena_cleared' 'Game' 'harpsichord_chime_positive.wav' -7 -26),
    (Ui 'boss_intro' 'Game' 'grand_piano_mystery.wav' -7 -26),
    (Ui 'toast_item' 'Game' 'item_equip.wav' -9 -29),
    (Ui 'toast_rare' 'Game' 'gem_collect.wav' -9 -29),
    (Ui 'toast_gold' 'Game' 'coin_collect.wav' -10 -30)
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
$list = if ($Group -eq 'camp-sfx') { $campSfx } elseif ($Group -eq 'ui-pack') { $uiPack } else { $sounds + $camp }
foreach ($s in $list) {
    $srcDir = if ($s.PSObject.Properties['Src']) { $s.Src } else { $Source }
    $src = Join-Path $srcDir $s.File
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
