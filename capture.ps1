<#
.SYNOPSIS
    Собирает плеер и снимает из него кадры игры.

.DESCRIPTION
    Визуальная задача закрывается картинкой, а не словами «компилируется».
    Скрипт даёт повторяемую картинку: один сид, одно расположение врагов,
    одно расписание кадров — значит два запуска можно честно сравнить.

    Плеер пересобирается только если исходники новее exe: сборка занимает
    минуты, а снимок с уже собранного плеера — секунды.
#>
[CmdletBinding()]
param(
    [int]    $Enemies = 6,
    [uint64] $Seed    = 20260829,
    [string] $Times   = '3,6,10',
    [int]    $Width   = 1920,
    [int]    $Height  = 1080,
    [string] $OutDir  = '',
    [switch] $Whirlwind,
    [switch] $Run,
    [switch] $MovingCombat,
    [switch] $Video,
    [ValidateSet('', 'autoattack', 'whirlwind', 'anchor-leap', 'anchor-sweep', 'chain-step', 'rotation')]
    [string] $Skill = '',
    [ValidateSet('', 'normal', 'crit', 'kill')]
    [string] $HitTier = '',
    [double] $VideoStart = 0.4,
    [double] $VideoDuration = 3.9,
    [int]    $VideoFps = 60,
    [double] $CameraSize = 0,
    [switch] $Rebuild,
    [switch] $NoRebuild
)

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'razlom'
$unity   = 'C:\Program Files\Unity\Hub\Editor\6000.5.10f1\Editor\Unity.exe'
$build   = Join-Path $root 'artifacts\capture-build'
$player  = Join-Path $build 'Razlom.exe'

if (-not (Test-Path -LiteralPath $unity)) { throw "Unity не найден: $unity" }

if ($OutDir -eq '') {
    $OutDir = Join-Path $root ('artifacts\capture\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}

# --- нужна ли пересборка -------------------------------------------------
if ($Rebuild -and $NoRebuild) { throw '-Rebuild и -NoRebuild нельзя использовать вместе.' }
$needsBuild = $Rebuild -or -not (Test-Path -LiteralPath $player)
if ($NoRebuild) {
    if (-not (Test-Path -LiteralPath $player)) { throw "Проверенный capture player не найден: $player" }
    $needsBuild = $false
} elseif (-not $needsBuild) {
    # Unity инкрементально не переписывает launcher Razlom.exe, если меняется
    # только managed-код. Метка актуальности — Game.View.dll, а не старый exe.
    $managedStamp = Join-Path $build 'Razlom_Data\Managed\Game.View.dll'
    $playerTime = if (Test-Path -LiteralPath $managedStamp) {
        (Get-Item -LiteralPath $managedStamp).LastWriteTimeUtc
    } else {
        (Get-Item -LiteralPath $player).LastWriteTimeUtc
    }
    $newest = Get-ChildItem (Join-Path $project 'Assets') -Recurse -File -Include *.cs,*.shader,*.unity,*.prefab,*.mat |
              Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($newest -and $newest.LastWriteTimeUtc -gt $playerTime) {
        Write-Host "Исходники новее плеера ($($newest.Name)) — пересборка."
        $needsBuild = $true
    }
}

if ($needsBuild) {
    # Пакетный Unity не может открыть проект, который уже открыт в редакторе:
    # проект заблокирован, и процесс молча выходит с кодом 1. Закрывать
    # редактор ради снимка неправильно — там открытая работа. Поэтому сборка
    # идёт из теневой копии: Assets/Packages/ProjectSettings зеркалятся
    # robocopy, своя Library у копии остаётся и переживает запуски.
    $shadow = Join-Path $root 'artifacts\capture-project'
    Write-Host 'Синхронизация теневого проекта...'
    foreach ($folder in 'Assets', 'Packages', 'ProjectSettings') {
        # /MIR — зеркало: удалённый в оригинале файл исчезает и в копии,
        # иначе теневой проект копил бы удалённые скрипты и не собирался.
        $copyArgs = @('/MIR', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
        if ($folder -eq 'Assets') {
            # -noUpm намеренно не поднимает NUnit. Tests остаются в исходном
            # проекте и обычном Test Runner, но не входят в capture player.
            $copyArgs += @('/XD', (Join-Path $project 'Assets\Game.Tests'))
        }
        & robocopy (Join-Path $project $folder) (Join-Path $shadow $folder) @copyArgs | Out-Null
        # robocopy отдаёт 0–7 как успех, 8+ как ошибку.
        if ($LASTEXITCODE -ge 8) { throw "robocopy $folder завершился с кодом $LASTEXITCODE." }
    }

    # /XD не удаляет папку, оставшуюся от предыдущей версии зеркала. Удаляем
    # только сгенерированную теневую копию после проверки абсолютного пути.
    $shadowAssets = [IO.Path]::GetFullPath((Join-Path $shadow 'Assets'))
    $shadowTests = [IO.Path]::GetFullPath((Join-Path $shadowAssets 'Game.Tests'))
    if (-not $shadowTests.StartsWith($shadowAssets + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Небезопасный путь shadow tests: $shadowTests"
    }
    if (Test-Path -LiteralPath $shadowTests) {
        Remove-Item -LiteralPath $shadowTests -Recurse -Force
    }

    # Reuse the package graph already resolved by the open project. Package
    # contents remain read-only and shared by junction; UPM still starts so
    # package asmdefs such as Unity.InputSystem are registered for compilation.
    $sourcePackageCache = Join-Path $project 'Library\PackageCache'
    $shadowPackageCache = Join-Path $shadow 'Library\PackageCache'
    $existingPackageCache = Get-Item -LiteralPath $shadowPackageCache -Force -ErrorAction SilentlyContinue
    if ($null -eq $existingPackageCache) {
        New-Item -ItemType Junction -Path $shadowPackageCache -Target $sourcePackageCache | Out-Null
    } elseif ($existingPackageCache.LinkType -eq 'Junction' -and
              $existingPackageCache.Target -ne $sourcePackageCache) {
        throw "Теневая PackageCache junction указывает не туда: $($existingPackageCache.Target)"
    }

    # Не зеркалим Library/PackageManager: projectResolution.json содержит
    # абсолютные пути исходного проекта и ломает UPM в shadow-проекте
    # сообщением «path argument ... undefined». PackageCache достаточно для
    # офлайн-резолва, а граф зависимостей Unity пересоберёт сам в shadow.

    Write-Host 'Сборка плеера...'
    $log = Join-Path $root 'artifacts\capture-build.log'
    New-Item -ItemType Directory -Path (Split-Path $log) -Force | Out-Null

    # Через Start-Process -Wait, а не через «&»: Unity.exe — GUI-приложение,
    # оболочка не ждёт его и отдаёт код выхода от постороннего процесса.
    # Первая сборка теневого проекта импортирует ассеты с нуля и идёт долго.
    $unityRun = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
        '-batchmode', '-nographics', '-quit'
        '-projectPath', $shadow
        '-executeMethod', 'Game.EditorTools.RazlomCaptureBuild.Build'
        '-razlom-build-out', $build
        '-logFile', $log
    )

    if ($unityRun.ExitCode -ne 0) {
        Write-Host '--- хвост лога сборки ---'
        if (Test-Path $log) { Get-Content $log -Tail 60 -Encoding UTF8 }
        throw "Сборка завершилась с кодом $($unityRun.ExitCode). Полный лог: $log"
    }
    if (-not (Test-Path -LiteralPath $player)) { throw "Сборка прошла, но $player нет." }
}

# --- съёмка --------------------------------------------------------------
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
Write-Host "Съёмка: врагов $Enemies, сид $Seed, кадры на $Times с -> $OutDir"

$playerLog = Join-Path $OutDir 'player.log'
$playerArgs = @(
    '-screen-width',  $Width
    '-screen-height', $Height
    '-screen-fullscreen', '0'
    '-logFile',       $playerLog
    '-razlom-capture'
    '-capture-out',     $OutDir
    '-capture-times',   $Times
    '-capture-enemies', $Enemies
    '-capture-seed',    $Seed
    '-capture-width',   $Width
    '-capture-height',  $Height
)
if ($Whirlwind) { $playerArgs += '-capture-whirlwind' }
if ($Run) { $playerArgs += '-capture-run' }
if ($MovingCombat) { $playerArgs += '-capture-moving-combat' }
if ($Skill -ne '') { $playerArgs += @('-capture-skill', $Skill) }
if ($HitTier -ne '') { $playerArgs += @('-capture-hit-tier', $HitTier) }
if ($CameraSize -gt 0) {
    $playerArgs += @(
        '-capture-camera-size', $CameraSize.ToString([Globalization.CultureInfo]::InvariantCulture)
    )
}
if ($Video) {
    $playerArgs += @(
        '-capture-video'
        '-capture-video-start', $VideoStart.ToString([Globalization.CultureInfo]::InvariantCulture)
        '-capture-video-duration', $VideoDuration.ToString([Globalization.CultureInfo]::InvariantCulture)
        '-capture-video-fps', $VideoFps
    )
}
$process = Start-Process -FilePath $player -PassThru -ArgumentList $playerArgs

# Запас поверх последнего кадра: если рига не дошла до Application.Quit,
# висящий плеер должен быть убит, а не ждать человека.
$budget = ([double[]]($Times -split ',')) | Measure-Object -Maximum
$captureEnd = if ($Video) { $VideoStart + $VideoDuration } else { 0 }
$deadline = [int][Math]::Ceiling([Math]::Max($budget.Maximum, $captureEnd)) + 90

if (-not $process.WaitForExit($deadline * 1000)) {
    $process.Kill()
    Write-Warning "Плеер не вышел сам за $deadline с и был закрыт."
}

$shots = Get-ChildItem $OutDir -Filter *.png -ErrorAction SilentlyContinue
if (-not $shots) {
    Write-Host '--- хвост лога плеера ---'
    if (Test-Path $playerLog) { Get-Content $playerLog -Tail 40 -Encoding UTF8 }
    throw "Кадры не записались. Лог: $playerLog"
}

$shots | ForEach-Object { Write-Host ("  {0}  {1:N0} КБ" -f $_.Name, ($_.Length / 1KB)) }

if ($Video) {
    $frames = Join-Path $OutDir 'video_frames\frame_%04d.jpg'
    $movieName = if ($MovingCombat) {
        'pelag_moving_combat_1080p60.mp4'
    } elseif ($HitTier -ne '') {
        "pelag_basic_${HitTier}_1080p60.mp4"
    } elseif ($Skill -ne '') {
        "pelag_${Skill}_1080p60.mp4"
    } else {
        'pelag_whirlwind_1080p60.mp4'
    }
    $movie = Join-Path $OutDir $movieName
    $tools = Join-Path $root 'artifacts\tools\python'
    $imageio = Join-Path $tools 'imageio_ffmpeg'
    if (-not (Test-Path -LiteralPath $imageio)) {
        Write-Host 'Устанавливаю локальный ffmpeg-кодек для сборки MP4...'
        python -m pip install --disable-pip-version-check --target $tools imageio-ffmpeg
        if ($LASTEXITCODE -ne 0) { throw 'Не удалось установить imageio-ffmpeg.' }
    }

    $ffmpeg = python -c "import sys; sys.path.insert(0, r'$tools'); import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())"
    & $ffmpeg -y -framerate $VideoFps -i $frames -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -movflags +faststart $movie
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $movie)) {
        throw "Не удалось собрать MP4: $movie"
    }
    Write-Host ("  {0}  {1:N1} МБ" -f (Split-Path -Leaf $movie), ((Get-Item $movie).Length / 1MB))
}
Write-Host "Готово: $OutDir"

# Явный ноль: последним в скрипте отработал robocopy, а он возвращает 1 на
# «файлы скопированы». Без этой строки успешная съёмка выглядела бы провалом.
exit 0
