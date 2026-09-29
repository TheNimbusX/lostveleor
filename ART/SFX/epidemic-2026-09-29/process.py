"""Звуки мобов леса (поток K, 29.09): выбор владельца из picks.json -> клипы игры.

Каждый выбранный файл режется на отдельные дубли (окна заданы в RECIPES по
огибающей, см. комментарии), лишняя тишина до удара срезается, края
сглаживаются, всё сводится в моно 48 кГц 16 бит. Слои одного звука (удар
когтя Хранителя, общий слоёный удар убийства) сводятся по атаке в один клип
на вариацию. Громкость — по LUFS-M (макс. окна 400 мс, K-взвешивание
BS.1770): обычные разовые звуки -18, крупные удары -16, удар убийства -15,
фоновые (топот роя, шипение лужи, галоп, лечение) -20; пик не выше -1 dBFS,
лишнее снимает мягкий лимитер (до 6 дБ, у контактных щелчков до 9, у фона 3).
Очень острые записи (щелчок, сучок) упираются в пик раньше цели — остаются
на потолке -1 dBFS и тише цели: громче их можно сделать только порчей удара.

Выравнивание:
  attack — клип начинается за 5 мс до атаки (первая точка на -6 дБ от пика
           огибающей): контактные звуки, играются в тик события;
  onset  — за 10 мс до первого звука (-30 дБ): голоса и скрипы;
  peak   — пик огибающей ровно на lead секунд от начала: взмахи, прыжок, вой;
           CombatAudio запускает их за lead до контакта (MobSoundBank);
  window — окно как есть, только края: фоновые звуки.

Выход: razlom/Assets/Resources/Audio/Combat/Mobs/<Mob>/<slot>_<nn>.wav,
отчёт report.json рядом со скриптом, блок источников в LICENSES.md.

Запуск из любой папки: python ART/SFX/epidemic-2026-09-29/process.py
ffmpeg: переменная FFMPEG, пакет imageio_ffmpeg или ffmpeg в PATH.
"""
import glob, io, json, os, re, shutil, subprocess, sys, tempfile, wave
from fractions import Fraction
import numpy as np
from scipy.signal import lfilter, resample_poly
from scipy.ndimage import minimum_filter1d, uniform_filter1d

SR = 48000
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
RAW = os.path.join(HERE, 'raw')
OUT = os.path.join(ROOT, 'razlom', 'Assets', 'Resources', 'Audio', 'Combat', 'Mobs')
LICENSES = os.path.join(ROOT, 'razlom', 'Assets', 'Resources', 'Audio', 'Combat', 'LICENSES.md')
CEILING_DB = -1.0
TARGETS = {'oneshot': -18.0, 'voice': -18.0, 'big': -16.0, 'kill': -15.0, 'bed': -20.0}
MAX_LIMIT_DB = {'oneshot': 6.0, 'voice': 6.0, 'big': 6.0, 'kill': 6.0, 'bed': 3.0}


def find_ffmpeg():
    if os.environ.get('FFMPEG') and os.path.exists(os.environ['FFMPEG']):
        return os.environ['FFMPEG']
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        pass
    for pattern in [os.path.join(ROOT, 'artifacts', 'tools', 'python', 'imageio_ffmpeg', 'binaries', 'ffmpeg-*.exe')]:
        found = glob.glob(pattern)
        if found:
            return found[0]
    path = shutil.which('ffmpeg')
    if path:
        return path
    sys.exit('ffmpeg не найден: задайте FFMPEG или поставьте imageio-ffmpeg')


FF = find_ffmpeg()
_cache = {}


def decode(name):
    """Файл из raw/ -> моно float64 48 кГц. Стерео с противофазой (MS) — громкий канал."""
    if name in _cache:
        return _cache[name]
    path = os.path.join(RAW, name)
    out = subprocess.run([FF, '-v', 'error', '-i', path, '-ac', '2', '-ar', str(SR), '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    st = np.frombuffer(out, dtype=np.float32).reshape(-1, 2).astype(np.float64)
    mono = st.mean(axis=1)
    rl, rr, rm = (np.sqrt((x ** 2).mean() + 1e-20) for x in (st[:, 0], st[:, 1], mono))
    if rm < 0.5 * max(rl, rr):
        mono = st[:, 0] if rl >= rr else st[:, 1]
    _cache[name] = mono
    return mono


def envelope(a, win=0.0025):
    """RMS по окну win с шагом в сэмпл (центрированное)."""
    n = max(1, int(win * SR))
    c = np.concatenate([[0.0], np.cumsum(a * a)])
    lo = np.clip(np.arange(len(a)) - n // 2, 0, len(a))
    hi = np.clip(lo + n, 0, len(a))
    return np.sqrt((c[hi] - c[lo]) / np.maximum(1, hi - lo))


def first_above(a, rel_db):
    e = envelope(a)
    idx = np.nonzero(e >= e.max() * 10 ** (rel_db / 20))[0]
    return int(idx[0]) if len(idx) else 0


def attack_index(a): return first_above(a, -6.0)
def onset_index(a): return first_above(a, -30.0)
def peak_index(a): return int(np.argmax(envelope(a, 0.01)))  # пик свиста — по RMS 10 мс, а не по одному сэмплу


def fade(a, fin, fout):
    a = a.copy()
    ni, no = min(len(a), int(fin * SR)), min(len(a), int(fout * SR))
    if ni > 0:
        a[:ni] *= np.linspace(0.0, 1.0, ni)
    if no > 0:
        a[-no:] *= 0.5 * (1 + np.cos(np.linspace(0, np.pi, no)))
    a[0] = 0.0
    a[-1] = 0.0
    return a


def rate_shift(a, rate):
    """rate > 1 — выше и короче (как проигрыватель с pitch = rate)."""
    if abs(rate - 1.0) < 1e-6:
        return a
    f = Fraction(rate).limit_denominator(64)
    return resample_poly(a, f.denominator, f.numerator)


def cut(name, start, end, rate=1.0):
    a = decode(name)
    s, e = int(start * SR), min(len(a), int(end * SR))
    seg = fade(a[s:e], 0.003, 0.02)
    return rate_shift(seg, rate)


KB1 = ([1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585])
KB2 = ([1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621])


def lufs_m_max(a):
    """
    Максимум громкости M (окно 400 мс, шаг 10 мс), K-взвешивание для 48 кГц.
    Клип короче 400 мс меряется окном своей длины (не короче 100 мс): иначе
    щелчок укуса «разбавлялся» бы тишиной и дотягивался бы до цели только
    лимитером. Та же мера — в тестах (MobAudioClipTests.LoudnessMax).
    """
    y = lfilter(*KB2, lfilter(*KB1, a))
    w, hop = int(min(0.4, max(0.1, len(a) / SR)) * SR), int(0.01 * SR)
    if len(y) < w:
        y = np.concatenate([y, np.zeros(w - len(y))])
    c = np.concatenate([[0.0], np.cumsum(y * y)])
    starts = np.arange(0, len(y) - w + 1, hop)
    ms = (c[starts + w] - c[starts]) / w
    return -0.691 + 10 * np.log10(ms.max() + 1e-20)


def limiter(a, ceiling):
    """Упреждение 2 мс, мгновенная атака, отпускание 20 мс; сверху — жёсткий потолок."""
    g = np.minimum(1.0, ceiling / np.maximum(np.abs(a), 1e-12))
    look = int(0.002 * SR)
    g = minimum_filter1d(g, size=2 * look + 1, mode='nearest')
    # Сглаживание с краями «как есть»: нули за краем тянули бы усиление вниз у начала клипа.
    g = np.minimum(g, uniform_filter1d(g, size=2 * look + 1, mode='nearest'))
    rel = 1.0 - np.exp(-1.0 / (0.02 * SR))
    out = np.empty_like(g)
    y = 1.0
    for i in range(len(g)):
        gi = g[i]
        y = gi if gi < y else y + (gi - y) * rel
        out[i] = y
    return np.clip(a * out, -ceiling, ceiling)


def db(x): return 20 * np.log10(max(float(x), 1e-12))


def normalize(a, category, allow=None):
    """Усиление до цели; пик выше потолка снимает лимитер, но не больше allow дБ."""
    target = TARGETS[category]
    allow = MAX_LIMIT_DB[category] if allow is None else allow
    ceiling = 10 ** (CEILING_DB / 20)
    peak = db(np.abs(a).max())
    gain = target - lufs_m_max(a)
    y = a
    for _ in range(5):
        capped = peak + gain - CEILING_DB > allow
        if capped:
            gain = allow + CEILING_DB - peak
        y = a * 10 ** (gain / 20)
        if np.abs(y).max() > ceiling:
            y = limiter(y, ceiling)
        miss = target - lufs_m_max(y)
        if abs(miss) < 0.2 or (capped and miss > 0):
            break
        gain += miss
    return np.clip(y, -ceiling, ceiling)


def build(recipe, layers):
    """Одна вариация: слои по атаке в один клип, обрезка по режиму, края, громкость."""
    parts = []
    for layer in layers:
        alias, start, end = layer[0], layer[1], layer[2]
        gain = layer[3] if len(layer) > 3 else 0.0
        rate = layer[4] if len(layer) > 4 else 1.0
        parts.append(cut(SRC[alias], start, end, rate) * 10 ** (gain / 20))
    if len(parts) == 1:
        mix = parts[0]
    else:
        refs = [attack_index(p) for p in parts]
        head = max(refs)
        mix = np.zeros(head + max(len(p) - r for p, r in zip(parts, refs)))
        for p, r in zip(parts, refs):
            mix[head - r:head - r + len(p)] += p
    mode = recipe['align']
    if mode == 'attack':
        s = attack_index(mix) - int(0.005 * SR)
    elif mode == 'onset':
        s = onset_index(mix) - int(0.010 * SR)
    elif mode == 'peak':
        s = peak_index(mix) - int(recipe['lead'] * SR)
    else:
        s = 0
    if s < 0:
        mix = np.concatenate([np.zeros(-s), mix])
        s = 0
    clip = mix[s:s + int(recipe['length'] * SR)]
    if mode != 'window':
        e = envelope(clip)
        loud = np.nonzero(e >= e.max() * 10 ** (-50 / 20))[0]
        clip = clip[:min(len(clip), int(loud[-1]) + int(0.01 * SR))]
    dur = len(clip) / SR
    fin = recipe.get('fin', 0.015 if mode == 'peak' else 0.002)
    fout = min(0.4 * dur, recipe.get('fout', 0.15))
    clip = fade(clip, fin, fout)
    # Контактным щелчкам (укус, скорлупа, шип) можно чуть больше лимитера: пик у них — доли мс.
    allow = recipe.get('limit', MAX_LIMIT_DB[recipe['category']] + (3.0 if mode == 'attack' else 0.0))
    return normalize(clip, recipe['category'], allow)


# Псевдонимы источников: имя файла в raw/ (выбор владельца в picks.json).
SRC = {
    'g_swing': 'ES_Swooshes, Swish, Wood Stick, Heavy - Epidemic Sound.wav',
    'g_claw': 'ES_Fight, Impact, Claw Attack, Scratch, Slash, Video Game 01 - Epidemic Sound.wav',
    'g_thud': 'ES_Wood, Impact, Hit, Thud, Heavy, Distant - Epidemic Sound.wav',
    'g_creak': 'ES_Wood, Friction, Creak, Old Wooden Furniture, Short, Dry 03 - Epidemic Sound.wav',
    'g_death': 'ES_Wood, Break, Tree Crack, Fall 04 - Epidemic Sound.wav',
    'g_step': 'ES_Footsteps, Human, Stomp, Moss, Twigs, Rotten Wood, Distant, Forest, Oslo 04 Schoeps (MS) - Epidemic Sound.wav',
    'rs_bite': 'ES_Food & Drink, Eating, Bite Carrot - Epidemic Sound.wav',
    'rs_scuttle': 'ES_Footsteps, Animal, Insect, Movement, Scuttle, Leaves, Undergrowth 02 - Epidemic Sound.wav',
    'rs_squeak': "ES_Creatures, Small, Sheitan's Baby, Voice 03 - Epidemic Sound.wav",
    'rs_snap': 'ES_Vegetation, Tree, Stick, Thin, Snap, Break - Epidemic Sound.wav',
    'bud_spit': 'ES_Toys, Misc, Pea Shooter, Shoot, Release, Deep, Single - Epidemic Sound.wav',
    'bud_splat': 'ES_Cartoon, Splat, Wet - Epidemic Sound.wav',
    'bud_sizzle': 'ES_Chemicals, Acid, Bubbling, Sizzling, Close - Epidemic Sound.wav',
    'bud_gurgle': 'ES_Creatures, Small, Gurgling, Bubble - Epidemic Sound.wav',
    'bud_slime': 'ES_Slime, Movement, Fast 05 - Epidemic Sound.wav',
    'sh_snort': 'ES_Animals, Farm, Pig, Big, Snorts, Grunts - Epidemic Sound.wav',
    'sh_gallop': 'ES_Animals, Horse, Foley Stage, Horse Galloping Dirt 01 - Epidemic Sound.wav',
    'sh_hit': 'ES_Designed, Impact, Sudden, Heavy, Percussive Hit - Epidemic Sound.wav',
    'sh_tusk': 'ES_Swooshes, Swish, Axe, Battle Axe, Large Object, Swing, Swoosh - Epidemic Sound.wav',
    'sh_squeal': 'ES_Pig, Grunt, Squeal, Inside Barn, Wet, Sloppy, Mud 05 - Epidemic Sound.wav',
    'sh_grunt': 'ES_Animals, Farm, Pig, Guttural Grunt, Vocalization, Sniff - Epidemic Sound.wav',
    'w_claw': 'ES_Swooshes, Swish, Fast, Double Swish, Variations - Epidemic Sound.wav',
    'w_leap': 'ES_Swooshes, Whoosh, Short, Deep Reversed, Dry - Epidemic Sound.wav',
    'w_land': 'ES_Rocks, Impact, Heavy, Drop On Ground, Grit - Epidemic Sound.wav',
    'w_howl': 'ES_Creatures, Monster, Wendigo, Spirit, Vocalizations, Shriek, Scary, Horror 01 - Epidemic Sound.wav',
    'w_sweep': 'ES_Swooshes, Whoosh, Deep, Low, Cinematic - Epidemic Sound.wav',
    'w_chuff': 'ES_Creatures, Monster, Wendigo, Spirit, Chuff, Etheral, Horror 01 - Epidemic Sound.wav',
    'w_moan': 'ES_Creatures, Monster, Ill Or Dying, Moan - Epidemic Sound.wav',
    't_dig': 'ES_Tools, Hand, Shovel, Spade, Dig Into Ground x6 - Epidemic Sound.wav',
    't_smash': 'ES_Wood, Break, Ukulele, Thin, Smash 01 - Epidemic Sound.wav',
    't_arrow': 'ES_Weapons, Arrow, Arrows, Shot 05, Flyby, Swish, Sanken (Omni AB) - Epidemic Sound.wav',
    't_twigs': 'ES_Vegetation, Leaves, Bush, Twig Breaks - Epidemic Sound.wav',
    't_troll': 'ES_Creatures, Humanoid, Giant, Troll, Reactions, Pain, Dying, Hurt, Various 02 - Epidemic Sound.wav',
    'sn_slam': 'ES_Designed, Impact, Troll, Fight, Impact Rock Ground 07 - Epidemic Sound.wav',
    'sn_vines': 'ES_Vegetation, Misc, Plant, Vines, Growing, Up From Ground, Crawling 02 - Epidemic Sound.wav',
    'sn_magic': 'ES_Magic, Shimmer, Spell, Pad, Ethereal, Nature Magic, Idle, Complex - Epidemic Sound.wav',
    'sn_moss': 'ES_Vegetation, Grass, Foliage, Moss, Squish - Epidemic Sound.wav',
    'sn_groan': 'ES_Creatures, Humanoid, Giant, Male, Groan - Epidemic Sound.wav',
    'sp_bite': 'ES_Animals, Insect, Caterpillar, Bite, Eat Leaf 04 - Epidemic Sound.wav',
    'sp_roll': 'ES_Swooshes, Whoosh, Wood, Roll Long - Epidemic Sound.wav',
    'sp_shell': 'ES_Food & Drink, Ingredients, Nut, Peanut, Shell, Crush 03 - Epidemic Sound.wav',
    'sp_bounce': 'ES_Creatures, Small, Misc, Granular, Bouncing - Epidemic Sound.wav',
    'sp_pug': "ES_Creatures, Misc, Goblin's Pug, Voice 02 - Epidemic Sound.wav",
    'sp_egg': 'ES_Food & Drink, Ingredients, Egg, Shell, Crack, Break, Crush 03 - Epidemic Sound.wav',
    'k_log': 'ES_Games, Video, Video Game, Wood Break, Fat, Log - Epidemic Sound.wav',
    'k_punch': 'ES_Fight, Impact, Magic Hits, Thick, Punchy - Epidemic Sound.wav',
    'x_bell': 'ES_Metal, Tonal, Impact, Metal Bowl, Bell Like, Ring Out - Epidemic Sound.wav',
    'x_vines': 'ES_Vegetation, Misc, Plant, Vines, Growing, Movement, Creaks, Rubbery Vines - Epidemic Sound.wav',
}

# Пики выровненных клипов — те же числа, что в Game.View/MobSoundBank.cs.
SWING_LEAD, SWEEP_LEAD, LEAP_LEAD, HOWL_LEAD = 0.20, 0.50, 0.25, 0.80


def R(mob, slot, pick, align, category, length, variations, **kw):
    return dict(mob=mob, slot=slot, pick=pick, align=align, category=category, length=length,
                variations=variations, **kw)


# Окна подобраны по огибающей (шаг 20 мс) каждого файла; слой — (источник, начало, конец[, дБ[, rate]]).
RECIPES = [
    # ---- Лесной хранитель ----
    # Четыре взмаха палкой в файле (пики 0,40 / 1,35 / 2,31 / 3,15 с).
    R('Guardian', 'swing', 'Forest Guardian: claw swing whoosh', 'peak', 'oneshot', 0.55,
      [[('g_swing', 0.05, 0.85)], [('g_swing', 1.00, 1.80)], [('g_swing', 1.95, 2.75)], [('g_swing', 2.85, 3.65)]],
      lead=SWING_LEAD, fout=0.2),
    # Когти (пять дублей) + деревянный глухой удар снизу, у каждого дубля своя высота удара.
    R('Guardian', 'impact', 'Forest Guardian: claw impact on hero', 'attack', 'big', 0.75,
      [[('g_claw', 0.03, 1.10), ('g_thud', 0.02, 0.90, -5.0, 1.00)],
       [('g_claw', 1.26, 2.40), ('g_thud', 0.02, 0.90, -5.0, 0.94)],
       [('g_claw', 2.50, 3.60), ('g_thud', 0.02, 0.90, -5.0, 1.06)],
       [('g_claw', 3.74, 4.90), ('g_thud', 0.02, 0.90, -5.0, 0.90)]], fout=0.25),
    # Сухой скрип — две части одного скрипа и ниже тоном первая.
    R('Guardian', 'hurt', 'Forest Guardian: hurt (creak + growl)', 'onset', 'voice', 0.50,
      [[('g_creak', 0.02, 0.52)], [('g_creak', 0.50, 0.98)], [('g_creak', 0.02, 0.52, 0.0, 0.90)]], fout=0.15),
    # Треск и падение дерева: с большого треска (1,26 с) — падение ложится через ~0,9 с, как тело.
    R('Guardian', 'death', 'Forest Guardian: death (crumble, creak, thud)', 'onset', 'big', 1.90,
      [[('g_death', 1.20, 3.20)], [('g_death', 1.20, 3.20, 0.0, 0.93)]], fout=0.55),
    # Отдельные шаги по мху и гнилому дереву. Хука шагов врагов нет — клипы про запас.
    R('Guardian', 'step', 'Forest Guardian: footsteps (heavy wooden)', 'attack', 'oneshot', 0.45,
      [[('g_step', 3.05, 3.60)], [('g_step', 6.64, 7.20)], [('g_step', 8.62, 9.20)], [('g_step', 14.69, 15.25)]], fout=0.2),

    # ---- Корнеполз ----
    R('RootSwarm', 'bite', 'Root Swarm: bite / snap', 'attack', 'oneshot', 0.28,
      [[('rs_bite', 0.02, 0.40)], [('rs_bite', 0.58, 0.93)], [('rs_bite', 0.02, 0.40, 0.0, 1.10)]], fout=0.1),
    R('RootSwarm', 'scuttle', 'Root Swarm: scuttle / skitter', 'window', 'bed', 0.75,
      [[('rs_scuttle', 0.00, 0.75)], [('rs_scuttle', 0.35, 1.10)], [('rs_scuttle', 0.70, 1.45)]], fin=0.03, fout=0.2),
    R('RootSwarm', 'hurt', 'Root Swarm: hurt squeak', 'onset', 'voice', 0.45,
      [[('rs_squeak', 0.02, 0.47)], [('rs_squeak', 0.42, 0.87)], [('rs_squeak', 0.02, 0.47, 0.0, 1.12)]], fout=0.12),
    R('RootSwarm', 'death', 'Root Swarm: death pop / crunch', 'attack', 'oneshot', 0.40,
      [[('rs_snap', 0.56, 1.05)], [('rs_snap', 0.56, 1.05, 0.0, 1.12)], [('rs_snap', 0.56, 1.05, 0.0, 0.90)]], fout=0.15),

    # ---- Плюй-плод ----
    # Выстрел горохострела; плоды идут через 0,2 с — хвост не длиннее 0,24 с.
    R('Bud', 'spit', 'Forest Bud: spit / pop volley', 'attack', 'oneshot', 0.24,
      [[('bud_spit', 0.00, 0.40)], [('bud_spit', 0.00, 0.40, 0.0, 1.08)], [('bud_spit', 0.00, 0.40, 0.0, 0.93)]], fout=0.1),
    R('Bud', 'splat', 'Forest Bud: fruit splat on ground', 'attack', 'oneshot', 0.35,
      [[('bud_splat', 0.00, 0.45)], [('bud_splat', 0.00, 0.45, 0.0, 0.92)], [('bud_splat', 0.00, 0.45, 0.0, 1.09)]], fout=0.12),
    # Кислое бурление: три куска по 2,2 с из разных мест записи.
    R('Bud', 'puddle', 'Forest Bud: acid puddle sizzle / bubbling', 'window', 'bed', 2.2,
      [[('bud_sizzle', 1.0, 3.2)], [('bud_sizzle', 5.5, 7.7)], [('bud_sizzle', 10.0, 12.2)]], fin=0.04, fout=0.7),
    R('Bud', 'hurt', 'Forest Bud: hurt', 'onset', 'voice', 0.45,
      [[('bud_gurgle', 0.02, 0.48)], [('bud_gurgle', 0.55, 1.02)], [('bud_gurgle', 0.02, 0.48, 0.0, 1.10)]], fout=0.12),
    # Главный шлепок записи на 0,58 с и мелкие брызги за ним.
    R('Bud', 'death', 'Forest Bud: death (squelch / burst)', 'attack', 'oneshot', 0.40,
      [[('bud_slime', 0.54, 0.91)], [('bud_slime', 0.54, 0.91, 0.0, 0.90)], [('bud_slime', 0.54, 0.91, 0.0, 1.10)]], fout=0.12),

    # ---- Камнекопыт ----
    R('Stonehoof', 'snort', 'Stonehoof: snort / paw windup', 'onset', 'oneshot', 0.95,
      [[('sh_snort', 0.00, 0.98)], [('sh_snort', 1.00, 1.95)]], fout=0.25),
    # Галоп ниже тоном (вес); таран длится ~0,5-1,5 с, остановка гасит клип.
    R('Stonehoof', 'charge', 'Stonehoof: charge rumble / gallop', 'window', 'bed', 1.5,
      [[('sh_gallop', 1.0, 2.8, 0.0, 0.88)], [('sh_gallop', 3.1, 4.9, 0.0, 0.88)],
       [('sh_gallop', 6.9, 8.7, 0.0, 0.88)], [('sh_gallop', 12.1, 13.9, 0.0, 0.88)]], fin=0.03, fout=0.35),
    R('Stonehoof', 'collision', 'Stonehoof: collision heavy thud', 'attack', 'big', 1.0,
      [[('sh_hit', 0.00, 1.20)], [('sh_hit', 0.00, 1.20, 0.0, 0.90)], [('sh_hit', 0.00, 1.20, 0.0, 1.08)]], fout=0.35),
    R('Stonehoof', 'tusk', 'Stonehoof: tusk swipe', 'peak', 'oneshot', 0.55,
      [[('sh_tusk', 0.00, 0.80)], [('sh_tusk', 0.95, 1.80)], [('sh_tusk', 1.90, 2.75)], [('sh_tusk', 2.90, 3.54)]],
      lead=SWING_LEAD, fout=0.2),
    # Визг нарастает к 0,98 с — клип с 0,84 с, чтобы боль звучала сразу.
    R('Stonehoof', 'hurt', 'Stonehoof: hurt squeal-grunt', 'onset', 'voice', 0.70,
      [[('sh_squeal', 0.84, 1.64)], [('sh_squeal', 0.84, 1.64, 0.0, 0.92)], [('sh_squeal', 0.84, 1.64, 0.0, 1.08)]], fout=0.2),
    R('Stonehoof', 'death', 'Stonehoof: death', 'onset', 'voice', 1.40,
      [[('sh_grunt', 1.22, 2.67)], [('sh_grunt', 0.00, 1.05)]], fout=0.35),

    # ---- Вендиго ----
    R('Wendigo', 'claw', 'Wendigo: claw swipe', 'peak', 'oneshot', 0.55,
      [[('w_claw', 0.00, 0.80)], [('w_claw', 1.35, 2.20)], [('w_claw', 2.90, 3.70)], [('w_claw', 4.20, 4.74)]],
      lead=SWING_LEAD, fout=0.2),
    # Обратный свист нарастает к взлёту: пик ставится на тик отрыва.
    R('Wendigo', 'leap', 'Wendigo: leap takeoff', 'peak', 'oneshot', 0.42,
      [[('w_leap', 0.15, 0.80)], [('w_leap', 0.15, 0.80, 0.0, 0.90)], [('w_leap', 0.15, 0.80, 0.0, 1.10)]],
      lead=LEAP_LEAD, fout=0.12),
    R('Wendigo', 'land', 'Wendigo: heavy landing', 'attack', 'big', 0.60,
      [[('w_land', 0.00, 0.70)], [('w_land', 0.95, 1.70)], [('w_land', 1.80, 2.19)]], fout=0.2),
    # Три крика из пяти, пик — на удар кольца. Снизу тот же крик ниже на кварту (-9 дБ) для веса.
    R('Wendigo', 'howl', 'Wendigo: howl / roar (eerie)', 'peak', 'big', 2.0,
      [[('w_howl', 0.30, 2.10), ('w_howl', 0.30, 2.10, -9.0, 0.75)],
       [('w_howl', 12.55, 14.40), ('w_howl', 12.55, 14.40, -9.0, 0.75)],
       [('w_howl', 16.35, 18.50), ('w_howl', 16.35, 18.50, -9.0, 0.75)]],
      lead=HOWL_LEAD, fout=0.5),
    R('Wendigo', 'sweep', 'Wendigo: 360 sweep', 'peak', 'big', 1.10,
      [[('w_sweep', 0.10, 2.10)], [('w_sweep', 0.10, 2.10, 0.0, 0.92)], [('w_sweep', 0.10, 2.10, 0.0, 1.08)]],
      lead=SWEEP_LEAD, fout=0.3),
    R('Wendigo', 'hurt', 'Wendigo: hurt', 'onset', 'voice', 0.70,
      [[('w_chuff', 0.30, 1.05)], [('w_chuff', 3.58, 4.40)], [('w_chuff', 11.52, 12.30)], [('w_chuff', 14.44, 15.25)]], fout=0.25),
    R('Wendigo', 'death', 'Wendigo: death', 'onset', 'voice', 2.00,
      [[('w_moan', 0.06, 2.20)], [('w_moan', 0.06, 2.20, 0.0, 0.93)]], fout=0.5),

    # ---- Шипомёт ----
    # Лопата в землю, шесть ударов — четыре самых чистых.
    R('Thorncaster', 'spike', 'Thorncaster: thorn spikes erupting (sequential)', 'attack', 'oneshot', 0.35,
      [[('t_dig', 0.00, 0.45)], [('t_dig', 0.66, 1.10)], [('t_dig', 1.34, 1.80)], [('t_dig', 3.27, 3.75)]], fout=0.12),
    R('Thorncaster', 'burst', 'Thorncaster: thorn burst', 'attack', 'big', 0.60,
      [[('t_smash', 0.00, 0.65)], [('t_smash', 0.00, 0.65, 0.0, 0.90)], [('t_smash', 0.00, 0.65, 0.0, 1.10)]], fout=0.2),
    R('Thorncaster', 'shot', 'Thorncaster: thorn projectile whoosh + wooden impact', 'onset', 'oneshot', 0.45,
      [[('t_arrow', 0.15, 0.75)], [('t_arrow', 0.15, 0.75, 0.0, 1.08)], [('t_arrow', 0.15, 0.75, 0.0, 0.93)]], fout=0.15),
    R('Thorncaster', 'hurt', 'Thorncaster: hurt', 'onset', 'voice', 0.45,
      [[('t_twigs', 4.22, 4.68), ('t_twigs', 0.08, 0.55, -2.0)], [('t_twigs', 4.62, 5.10), ('t_twigs', 0.08, 0.55, -2.0, 0.9)],
       [('t_twigs', 4.22, 4.68, 0.0, 0.92), ('t_twigs', 4.62, 5.10, -2.0)]], fout=0.15),
    R('Thorncaster', 'death', 'Thorncaster: death', 'onset', 'voice', 1.60,
      [[('t_troll', 0.00, 1.95)], [('t_troll', 2.93, 3.92)], [('t_troll', 4.68, 6.85)]], fout=0.45),

    # ---- Корнехват ----
    R('RootSnarer', 'slam', 'Root Snarer: heavy slam into ground', 'attack', 'big', 1.10,
      [[('sn_slam', 0.00, 1.30)], [('sn_slam', 0.00, 1.30, 0.0, 0.92)], [('sn_slam', 0.00, 1.30, 0.0, 1.07)]], fout=0.35),
    R('RootSnarer', 'roots', 'Root Snarer: roots erupting from ground', 'window', 'oneshot', 1.0,
      [[('sn_vines', 0.02, 1.02)], [('sn_vines', 0.90, 1.90)], [('sn_vines', 5.90, 6.90)]], fin=0.02, fout=0.3),
    R('RootSnarer', 'mend', 'Root Snarer: healing channel / nature heal', 'window', 'bed', 1.6,
      [[('sn_magic', 2.0, 3.6)], [('sn_magic', 10.0, 11.6)], [('sn_magic', 20.0, 21.6)]], fin=0.15, fout=0.5),
    R('RootSnarer', 'hurt', 'Root Snarer: hurt', 'onset', 'voice', 0.45,
      [[('sn_moss', 0.26, 0.74)], [('sn_moss', 1.08, 1.56)], [('sn_moss', 1.92, 2.42)]], fout=0.12),
    R('RootSnarer', 'death', 'Root Snarer: death', 'onset', 'voice', 2.20,
      [[('sn_groan', 0.06, 2.60)], [('sn_groan', 0.06, 2.60, 0.0, 0.94)]], fout=0.6),

    # ---- Расщепень ----
    R('Splitter', 'bite', 'Splitter: bite', 'attack', 'oneshot', 0.40,
      [[('sp_bite', 0.10, 0.80)], [('sp_bite', 0.10, 0.80, 0.0, 1.10)], [('sp_bite', 0.10, 0.80, 0.0, 0.92)]], fout=0.12),
    R('Splitter', 'roll', 'Splitter: rolling ball rumble', 'window', 'oneshot', 0.90,
      [[('sp_roll', 0.25, 1.15)], [('sp_roll', 2.35, 3.25)], [('sp_roll', 4.95, 5.85)]], fin=0.03, fout=0.3),
    R('Splitter', 'crack', 'Splitter: shell crack / split', 'attack', 'oneshot', 0.35,
      [[('sp_shell', 0.06, 0.45)], [('sp_shell', 0.54, 0.87)], [('sp_shell', 0.06, 0.45, 0.0, 0.90)]], fout=0.12),
    R('Splitter', 'pop', 'Splitter: small creature pop / hop', 'onset', 'oneshot', 0.28,
      [[('sp_bounce', 1.34, 1.64)], [('sp_bounce', 6.17, 6.47)], [('sp_bounce', 9.25, 9.57)], [('sp_bounce', 3.16, 3.46)]], fout=0.1),
    R('Splitter', 'hurt', 'Splitter: hurt', 'onset', 'voice', 0.40,
      [[('sp_pug', 0.20, 0.56)], [('sp_pug', 0.52, 0.90)], [('sp_pug', 0.20, 0.56, 0.0, 1.12)]], fout=0.12),
    R('Splitter', 'death', 'Splitter: death', 'attack', 'oneshot', 0.45,
      [[('sp_egg', 0.02, 0.55)], [('sp_egg', 1.16, 1.62)], [('sp_egg', 2.58, 3.05)]], fout=0.15),

    # ---- Общие ----
    # Слои 1+2 владельца: жирный деревянный разлом + плотный удар, по атаке.
    R('Generic', 'kill', 'Generic: layered kill impact (crack + thump)', 'attack', 'kill', 0.90,
      [[('k_log', 0.24, 1.10), ('k_punch', 0.00, 0.95, -3.0)],
       [('k_log', 1.46, 2.40), ('k_punch', 1.22, 2.20, -3.0)],
       [('k_log', 0.24, 1.10, 0.0, 0.94), ('k_punch', 2.28, 3.35, -3.0)],
       [('k_log', 1.46, 2.40, 0.0, 1.05), ('k_punch', 3.48, 4.60, -3.0)]], fout=0.3),
    R('Generic', 'stun', 'Generic: hero stunned (daze ring)', 'attack', 'oneshot', 1.30,
      [[('x_bell', 0.08, 1.90)], [('x_bell', 0.08, 1.90, 0.0, 0.94)]], fout=0.6),
    R('Generic', 'rooted', 'Generic: hero rooted (roots wrapping)', 'window', 'oneshot', 1.0,
      [[('x_vines', 1.6, 2.6)], [('x_vines', 6.0, 7.0)], [('x_vines', 7.9, 8.9)]], fin=0.02, fout=0.3),
]


def write_wav(path, a):
    buf = io.BytesIO()
    with wave.open(buf, 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(np.round(np.clip(a, -1, 1) * 32767).astype('<i2').tobytes())
    data = buf.getvalue()
    try:
        with open(path, 'wb') as f:
            f.write(data)
    except OSError:
        # На части файлов репозитория open(..., 'w') падает с EINVAL — пишем поверх.
        with open(path, 'r+b') as f:
            f.write(data)
            f.truncate()


def track_urls():
    """Название записи -> ссылка Epidemic из shortlist.md."""
    urls = {}
    for line in open(os.path.join(HERE, 'shortlist.md'), encoding='utf-8'):
        cells = [c.strip() for c in line.strip().strip('|').split('|')]
        if len(cells) >= 6 and cells[5].startswith('https://'):
            urls[cells[2]] = cells[5]
    return urls


def title_of(name):
    return re.sub(r'^ES_', '', name).replace(' - Epidemic Sound.wav', '')


def clean_stale(folder, slot, keep):
    for path in glob.glob(os.path.join(folder, slot + '_*.wav')):
        m = re.match(re.escape(slot) + r'_(\d+)\.wav$', os.path.basename(path))
        if m and int(m.group(1)) > keep:
            os.remove(path)
            if os.path.exists(path + '.meta'):
                os.remove(path + '.meta')


def update_licenses(report, urls):
    begin, end = '<!-- mobs-sfx:begin -->', '<!-- mobs-sfx:end -->'
    by_source = {}
    for clip in report['clips']:
        for src in clip['sources']:
            by_source.setdefault(src['file'], set()).add(f"Mobs/{clip['mob']}/{clip['slot']}")
    lines = [begin,
             '## Epidemic Sound — звуки мобов леса (выбор владельца 29.09.2026)',
             '',
             '**Epidemic Sound — прототип, проверить лицензию на использование в игре до релиза.**',
             'Записи скачаны по подписке владельца; это не CC0. Клипы `Mobs/<Mob>/<slot>_<nn>.wav`',
             'нарезаны и сведены скриптом `ART/SFX/epidemic-2026-09-29/process.py` (отчёт — `report.json`',
             'рядом с ним); исходники лежат в `ART/SFX/epidemic-2026-09-29/raw/`.',
             '']
    for name in sorted(by_source):
        title = title_of(name)
        url = urls.get(title, 'n/a')
        track = url.rstrip('/').rsplit('/', 1)[-1] if url != 'n/a' else 'n/a'
        used = ', '.join(f'`{u}_*`' for u in sorted(by_source[name]))
        lines.append(f'- «{title}» — Epidemic Sound, track {track}, {url} → {used}')
    lines.append(end)
    block = '\r\n'.join(lines)
    with open(LICENSES, 'r+', encoding='utf-8', newline='') as f:
        text = f.read()
        if begin in text and end in text:
            text = text[:text.index(begin)] + block + text[text.index(end) + len(end):]
        else:
            text = text.rstrip('\r\n') + '\r\n\r\n' + block + '\r\n'
        f.seek(0)
        f.write(text)
        f.truncate()


def main():
    picks = json.load(open(os.path.join(HERE, 'picks.json'), encoding='utf-8'))
    picked = {p['file'] for k, v in picks.items() if k != '_note' for p in v['picked']}
    urls = track_urls()
    report = {'sample_rate': SR, 'ceiling_dbfs': CEILING_DB, 'targets_lufs_m_max': TARGETS, 'clips': []}
    for recipe in RECIPES:
        folder = os.path.join(OUT, recipe['mob'])
        os.makedirs(folder, exist_ok=True)
        for n, layers in enumerate(recipe['variations'], 1):
            for layer in layers:
                assert SRC[layer[0]] in picked, f"{layer[0]} не из выбора владельца"
                assert SRC[layer[0]] in [p['file'] for p in picks[recipe['pick']]['picked']], recipe['pick']
            clip = build(recipe, layers)
            name = f"{recipe['slot']}_{n:02d}.wav"
            write_wav(os.path.join(folder, name), clip)
            e = envelope(clip)
            entry = {
                'path': f"razlom/Assets/Resources/Audio/Combat/Mobs/{recipe['mob']}/{name}",
                'mob': recipe['mob'], 'slot': recipe['slot'], 'pick': recipe['pick'],
                'align': recipe['align'], 'category': recipe['category'],
                'lead_s': recipe.get('lead'),
                'seconds': round(len(clip) / SR, 3),
                'peak_dbfs': round(db(np.abs(clip).max()), 2),
                'lufs_m_max': round(lufs_m_max(clip), 2),
                'onset_ms': round(onset_index(clip) / SR * 1000, 1),
                'peak_s': round(peak_index(clip) / SR, 3),
                'sources': [{'file': SRC[l[0]], 'title': title_of(SRC[l[0]]), 'url': urls.get(title_of(SRC[l[0]]), 'n/a'),
                             'from_s': l[1], 'to_s': l[2], 'gain_db': l[3] if len(l) > 3 else 0.0,
                             'rate': l[4] if len(l) > 4 else 1.0} for l in layers],
            }
            report['clips'].append(entry)
            print(f"{recipe['mob']:12s} {name:14s} {entry['seconds']:5.2f}s peak {entry['peak_dbfs']:6.1f} "
                  f"LUFS-M {entry['lufs_m_max']:6.1f} ({TARGETS[recipe['category']]:+.0f}) onset {entry['onset_ms']:5.1f}ms "
                  f"peak@{entry['peak_s']:.3f}")
        clean_stale(folder, recipe['slot'], len(recipe['variations']))
    unused = sorted(k for k, v in picks.items() if k != '_note' and v['picked']
                    and k not in {r['pick'] for r in RECIPES})
    report['unused_picks'] = unused
    report['silent_by_choice'] = [k for k, v in picks.items() if k != '_note' and not v['picked']]
    with open(os.path.join(HERE, 'report.json'), 'w', encoding='utf-8', newline='\n') as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    update_licenses(report, urls)
    print(f"{len(report['clips'])} клипов; без звука по выбору: {report['silent_by_choice']}; не использованы: {unused}")


if __name__ == '__main__':
    main()
