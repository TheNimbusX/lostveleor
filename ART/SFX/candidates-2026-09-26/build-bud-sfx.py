"""Плюй-плод, сигнал крупного телеграфа и замах хранителя — кандидаты 26.09.2026.

Только своя семья из razlom/Assets/Resources/Audio/Combat:
  HitBody (body_hit_00..04), Kill (kill_body_00..02), Whoosh (sword_whoosh_*),
  Cast (whirlwind_cast_01..02), HitMetal (sword_hit_*), Dissolve (dissolve_sand_00),
  Footstep (step_pt1..3) и записи сабли владельца Pelag/Attack_01..05.
Набор Вихря (Whirlwind_*, WhirlwindHit/Pulse — в нём слой Epidemic) не берётся,
никаких «кишок»: плод деревянно-сочный, не мясной.
Преобразования: высота (передискретизация), плавное падение высоты, реверс,
фильтры, огибающие, короткие зёрна из тех же записей, слои.

Каждый слот — три кандидата a/b/c рядом со скриптом (bud_<слот>_<x>.wav и т. п.)
и страница index.html для прослушивания. В игру ставится выбранный кандидат
(по умолчанию «a» каждого слота):

  python "ART/SFX/candidates-2026-09-26/build-bud-sfx.py"
  python "ART/SFX/candidates-2026-09-26/build-bud-sfx.py" --install volley=b fruit=c swing=none

Залп в игре — не одна склейка: раскрытие звучит по ForestBudVolleyStarted, а каждый
из пяти хлопков — по своему ForestFruitLaunched (номер плода = номер хлопка). Так
звук не уезжает от плодов при хит-стопе, оглушении и уходе героя из дальности.
Файл-кандидат bud_volley_<x>.wav — та же фраза, собранная по таймингу симуляции
по умолчанию (замах 24 тика, 5 плодов через 6 тиков), только для прослушивания.
"""
import argparse, json, os, subprocess, wave
from fractions import Fraction
import numpy as np
from scipy.ndimage import minimum_filter1d
from scipy.signal import butter, sosfilt, resample_poly
import imageio_ffmpeg

SR = 48000
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
AUDIO = os.path.join(ROOT, 'razlom', 'Assets', 'Resources', 'Audio', 'Combat')
PARTS = os.path.join(HERE, 'parts')
FF = imageio_ffmpeg.get_ffmpeg_exe()

# Тайминг залпа из ForestBudSettings по умолчанию (30 тиков в секунду).
TICK = 1 / 30
VOLLEY_FIRST = 24 * TICK
VOLLEY_STEP = 6 * TICK
SHOTS = 5

# ---------------------------------------------------------------- источники

def decode(rel):
    cmd = [FF, '-v', 'error', '-i', os.path.join(AUDIO, rel), '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-']
    a = np.frombuffer(subprocess.run(cmd, capture_output=True, check=True).stdout, dtype=np.float32).copy()
    return a / max(np.abs(a).max(), 1e-9)

body = [decode(f'HitBody/body_hit_{i:02d}.ogg') for i in range(5)]
kill = [decode(f'Kill/kill_body_{i:02d}.ogg') for i in range(3)]
whoosh = [decode(f'Whoosh/sword_whoosh_{i:02d}.ogg') for i in (1, 3, 4, 6, 7, 8)]
cast = [decode(f'Cast/whirlwind_cast_{i:02d}.ogg') for i in (1, 2)]
metal = [decode(f'HitMetal/sword_hit_{i:02d}.ogg') for i in (1, 3, 5, 8, 10)]
dissolve = decode('Dissolve/dissolve_sand_00.ogg')
steps = [decode(f'Footstep/step_pt{i}.mp3') for i in (1, 2, 3)]
attack = {i: decode(f'Pelag/Attack_{i:02d}.wav') for i in range(1, 6)}

# ---------------------------------------------------------------- обработка

def rate(a, r):
    """r > 1 — выше и короче, r < 1 — ниже и длиннее (как asetrate)."""
    if abs(r - 1) < 1e-6: return a.copy()
    f = Fraction(r).limit_denominator(96)
    return resample_poly(a, f.denominator, f.numerator).astype(np.float32)

def sweep(a, r0, r1, curve=1.0):
    """Плавная смена скорости чтения от r0 к r1: «сдувание» при r1 < r0."""
    n = int(len(a) / min(r0, r1)) + 2
    t = np.linspace(0, 1, n) ** curve
    pos = np.cumsum(r0 + (r1 - r0) * t)
    pos = pos[pos < len(a) - 1]
    return np.interp(pos, np.arange(len(a)), a).astype(np.float32)

def _sos(kind, f, order): return butter(order, f, kind, fs=SR, output='sos')
def lp(a, f, order=2): return sosfilt(_sos('low', f, order), a).astype(np.float32)
def hp(a, f, order=2): return sosfilt(_sos('high', f, order), a).astype(np.float32)
def bp(a, lo, hi, order=2): return sosfilt(_sos('band', [lo, hi], order), a).astype(np.float32)

def seg(a, start, length):
    s = int(start * SR); out = np.zeros(int(length * SR), dtype=np.float32)
    part = a[max(0, s):s + len(out)]; out[:len(part)] = part; return out

def fade(a, fin=0.002, fout=0.05, curve=1.5):
    a = a.copy(); ni = min(len(a), int(fin * SR)); no = min(len(a), int(fout * SR))
    if ni: a[:ni] *= np.linspace(0, 1, ni, dtype=np.float32)
    if no: a[-no:] *= np.linspace(1, 0, no, dtype=np.float32) ** curve
    return a

def decay(a, tau):
    return (a * np.exp(-np.arange(len(a)) / (tau * SR))).astype(np.float32)

def ramp(a, points):
    """Огибающая по точкам (время, дБ); между точками — линейно в дБ."""
    t = np.arange(len(a)) / SR
    xs, ys = zip(*points)
    return (a * 10 ** (np.interp(t, xs, ys) / 20)).astype(np.float32)

def rev(a): return a[::-1].copy()

def peak_time(a): return int(np.argmax(np.abs(a))) / SR

def mix(length, layers):
    """layers: (массив, время начала, дБ)."""
    out = np.zeros(int(length * SR), dtype=np.float32)
    for a, start, gain in layers:
        s = int(round(start * SR)); g = 10 ** (gain / 20)
        lo = max(0, -s); hi = min(len(a), len(out) - s)
        if hi > lo: out[s + lo:s + hi] += a[lo:hi] * g
    return out

def at_peak(a, when, gain):
    """Слой, у которого пик ставится на время when."""
    return (a, when - peak_time(a), gain)

def grains(sources, count, t0, t1, r_lo, r_hi, seed, gain_lo=-20, gain_hi=-12, length=0.035):
    """Капли: короткие высокие зёрна тех же ударов тела, разбросанные после удара."""
    rng = np.random.default_rng(seed)
    out = []
    for i in range(count):
        src = sources[i % len(sources)]
        g = fade(seg(rate(src, rng.uniform(r_lo, r_hi)), 0.0, length), 0.001, length * 0.8, 2.0)
        out.append((bp(g, 1200, 7000), rng.uniform(t0, t1), rng.uniform(gain_lo, gain_hi)))
    return out

def loudest(a, window=0.05):
    """Самые громкие 50 мс, дБ: так слышится короткий удар рядом с банками HitBody/Kill."""
    w = int(window * SR); n = max(1, len(a) // w)
    r = np.sqrt((np.resize(a, n * w).reshape(n, w) ** 2).mean(axis=1))
    return db(r.max())

def limit(a, ceiling_db=-1.0, release=0.05):
    """Пиковый лимитер с заглядыванием 2 мс: пики срезаются мягко, без щелчков."""
    c = 10 ** (ceiling_db / 20); look = int(0.002 * SR)
    need = np.minimum(1.0, c / np.maximum(np.abs(a), 1e-9))
    need = minimum_filter1d(need, 2 * look + 1)
    k = np.exp(-1 / (release * SR)); g = np.empty_like(need); cur = 1.0
    for i in range(len(need)):
        cur = need[i] if need[i] < cur else need[i] + (cur - need[i]) * k
        g[i] = cur
    g = np.convolve(g, np.ones(look) / look, mode='same')
    return np.clip(a * g, -c, c).astype(np.float32)

def master(a, loud_db, squash_db=4.0, ceiling_db=-1.0):
    """Громкость по самым громким 50 мс; лимитер режет пики не больше чем на squash_db."""
    a = hp(a - a.mean(), 30)
    g = 10 ** ((loud_db - loudest(a)) / 20)
    over = db(np.abs(a).max() * g) - ceiling_db
    if over > squash_db: g *= 10 ** (-(over - squash_db) / 20)
    return limit(a * g, ceiling_db)

def write(path, a):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((np.clip(a, -1, 1) * 32767).astype('<i2').tobytes())

def db(x): return 20 * np.log10(max(float(x), 1e-9))

def report(name, a):
    print(f'{name:28s} {len(a)/SR:5.2f}s  peak {db(np.abs(a).max()):5.1f} dB @ {peak_time(a):.3f}s  '
          f'rms {db(np.sqrt((a ** 2).mean())):6.1f} dB')

# ---------------------------------------------------------------- залп: раскрытие + 5 хлопков

def volley_open(kind):
    L = VOLLEY_FIRST + 0.02
    if kind == 'a':   # листва и вдох: шаги по листве ниже и медленнее + обратный удар тела как вдох
        rustle = np.concatenate([rate(s, 0.6) for s in steps])
        rustle = ramp(bp(seg(rustle, 0.06, L), 300, 3500), [(0, -20), (0.55, -4), (0.74, 0), (L, -30)])
        inhale = lp(rev(seg(rate(body[0], 0.6), 0, 0.6)), 700)
        creak = fade(bp(seg(rate(dissolve, 0.4), 0.15, 0.6), 500, 1600), 0.08, 0.2)
        out = mix(L, [(rustle, 0, -4), (inhale, VOLLEY_FIRST - 0.01 - len(inhale) / SR, -6), (creak, 0.1, -12)])
        return master(fade(out, 0.03, 0.03), -13)
    if kind == 'b':   # деревянная мортирка: скрип коры + низкий обратный подъём
        creak = np.concatenate([rate(steps[2], 0.45), rate(steps[0], 0.45)])
        creak = ramp(bp(seg(creak, 0.18, L), 200, 2200), [(0, -18), (0.6, -3), (0.76, 0), (L, -30)])
        swell = lp(rev(seg(rate(kill[1], 0.8), 0, 0.55)), 400)
        out = mix(L, [(creak, 0, -2), (swell, VOLLEY_FIRST - 0.01 - len(swell) / SR, -3)])
        return master(fade(out, 0.03, 0.03), -13)
    # c — воздух: обратный каст как вдох бутона + мерцание песка вместо листвы
    air = bp(rev(rate(cast[1], 0.7)), 400, 5000)
    air = fade(seg(air, peak_time(air) - (VOLLEY_FIRST - 0.01), VOLLEY_FIRST - 0.01), 0.05, 0.012, 1.0)
    shimmer = fade(hp(seg(rate(dissolve, 0.6), 0.05, 0.7), 1500), 0.1, 0.25)
    out = mix(L, [(ramp(air, [(0, -22), (0.45, -9), (0.74, 0)]), 0, -2), (shimmer, 0.05, -21)])
    return master(fade(out, 0.03, 0.03), -13)

POP_PITCH = {'a': [1.0, 1.05, 0.96, 1.08, 1.02],
             'b': [1.0, 0.95, 1.04, 0.92, 1.0],
             'c': [1.0, 1.03, 1.06, 1.09, 1.12]}

def volley_pop(kind, i):
    p = POP_PITCH[kind][i]
    if kind == 'a':   # мягкое «пок»: удар тела выше и короче + чуть воздуха от свиста
        pok = fade(lp(seg(rate(body[3], 1.9 * p), 0, 0.16), 3200), 0.001, 0.07)
        air = fade(hp(seg(rate(whoosh[3], 1.25 * p), 0, 0.12), 2500), 0.001, 0.08)
        out = mix(0.2, [at_peak(pok, 0.012, 0), at_peak(air, 0.02, -15)])
    elif kind == 'b':  # глухое «тумп» из низкого удара + щелчок тела
        thump = fade(lp(seg(rate(kill[2], 2.4 * p), 0, 0.18), 1500), 0.001, 0.08)
        tock = fade(seg(rate(body[2], 1.6 * p), 0, 0.14), 0.001, 0.07)
        out = mix(0.22, [at_peak(thump, 0.012, 0), at_peak(tock, 0.01, -6)])
    else:              # короткий взмах сабли владельца как «пфф» + высокий «ток»
        a2 = attack[2]; pk = peak_time(a2)
        puff = fade(lp(rate(seg(a2, pk - 0.04, 0.13), 1.3 * p), 5000), 0.004, 0.06)
        tock = fade(seg(rate(body[4], 2.3 * p), 0, 0.12), 0.001, 0.06)
        out = mix(0.2, [at_peak(puff, 0.02, -2), at_peak(tock, 0.014, -3)])
    return master(fade(out, 0.001, 0.05), -11)

# ---------------------------------------------------------------- падение плода

def fruit(kind):
    if kind == 'a':   # мягкий шлепок с брызгами
        splat = fade(lp(seg(rate(body[2], 1.1), 0, 0.3), 2200), 0.001, 0.12)
        spatter = decay(hp(seg(rate(dissolve, 0.9), 0.0, 0.22), 1800), 0.06)
        layers = [at_peak(splat, 0.012, 0), (spatter, 0.008, -14)]
        layers += grains([body[3], body[4]], 7, 0.02, 0.16, 2.6, 3.6, seed=11)
    elif kind == 'b':  # глухой плюх: низкий удар выше + лёгкое «сдувание» тела
        plop = fade(lp(seg(rate(kill[1], 1.7), 0, 0.26), 900), 0.001, 0.1)
        squish = fade(lp(sweep(seg(body[1], 0, 0.3), 1.25, 0.85), 2500), 0.001, 0.1)
        layers = [at_peak(plop, 0.012, 0), at_peak(squish, 0.02, -5)]
        layers += grains([body[1], body[3]], 4, 0.03, 0.14, 2.8, 3.4, seed=23, gain_lo=-22, gain_hi=-15)
    else:              # хрусткая кожура: шаг по листве как хруст + удар тела
        crunch = fade(bp(seg(rate(steps[1], 0.85), 0.0, 0.3), 600, 6000), 0.001, 0.12)
        thud = fade(lp(seg(rate(body[0], 1.3), 0, 0.26), 3000), 0.001, 0.1)
        layers = [at_peak(thud, 0.012, 0), at_peak(crunch, 0.016, -3)]
        layers += grains([body[4], body[0]], 5, 0.03, 0.15, 2.7, 3.5, seed=37, gain_lo=-21, gain_hi=-14)
    return master(fade(mix(0.36, layers), 0.001, 0.08), -9)

# ---------------------------------------------------------------- боль бутона

def hurt(kind):
    if kind == 'a':   # сочное «тук» + дрожь листьев
        thok = fade(bp(seg(rate(body[1], 1.25), 0, 0.28), 180, 4500), 0.001, 0.1)
        shiver = decay(bp(seg(rate(dissolve, 0.7), 0.0, 0.25), 900, 5000), 0.07)
        out = mix(0.3, [at_peak(thok, 0.01, 0), (shiver, 0.012, -11)])
    elif kind == 'b':  # удар тела + хруст шага
        thok = fade(lp(seg(rate(body[3], 1.05), 0, 0.3), 3500), 0.001, 0.1)
        crunch = fade(bp(seg(rate(steps[1], 1.1), 0.0, 0.22), 800, 6000), 0.001, 0.1)
        out = mix(0.3, [at_peak(thok, 0.01, 0), at_peak(crunch, 0.014, -7)])
    else:              # высокий «ток» + скрип стебля из взмаха сабли владельца, опущенного вниз
        tok = fade(seg(rate(body[4], 1.45), 0, 0.22), 0.001, 0.08)
        creak = fade(bp(seg(rate(attack[4], 0.55), 0, 0.3), 400, 1800), 0.004, 0.12)
        out = mix(0.32, [at_peak(tok, 0.01, 0), at_peak(creak, 0.05, -9)])
    return master(fade(out, 0.001, 0.07), -8)

# ---------------------------------------------------------------- смерть бутона (клип 1,2 с, касание ~0,95 с)

def death(kind):
    if kind == 'a':   # удар, «сдувание», осыпание листвы, мягкое касание земли
        hit = fade(seg(rate(body[0], 1.1), 0, 0.3), 0.001, 0.1)
        deflate = fade(lp(sweep(seg(body[2], 0, 0.3), 1.1, 0.4, 0.7), 1400), 0.01, 0.2)
        layers = [at_peak(hit, 0.012, 0), (deflate, 0.05, -3)]
        for t, s, g in ((0.12, 0, -11), (0.36, 1, -9), (0.60, 2, -10)):
            layers.append((fade(bp(rate(steps[s], 0.65), 300, 4000), 0.005, 0.12), t, g))
        land = fade(seg(rate(kill[1], 1.4), 0, 0.3), 0.001, 0.12)
        layers += [at_peak(land, 0.95, -3), (decay(hp(seg(rate(dissolve, 0.6), 0, 0.4), 1200), 0.12), 0.97, -16)]
        out = mix(1.35, layers)
    elif kind == 'b':  # глухой удар и долгое шуршание, касание телом
        hit = fade(seg(rate(kill[0], 1.6), 0, 0.3), 0.001, 0.1)
        snap = fade(seg(rate(body[3], 1.2), 0, 0.25), 0.001, 0.1)
        rustle = ramp(bp(seg(rate(dissolve, 0.45), 0, 1.2), 500, 4500), [(0, -30), (0.15, -6), (0.8, 0), (1.2, -30)])
        land = fade(lp(seg(rate(body[2], 0.8), 0, 0.35), 900), 0.001, 0.14)
        out = mix(1.35, [at_peak(hit, 0.012, 0), at_peak(snap, 0.012, -4), (rustle, 0.05, -8), at_peak(land, 0.92, -4)])
    else:              # сочный удар с каплями, выдох (свист ниже), касание
        hit = fade(seg(rate(body[4], 1.3), 0, 0.25), 0.001, 0.1)
        sigh = fade(lp(rate(whoosh[4], 0.45), 1800), 0.05, 0.3)
        land = fade(seg(rate(kill[2], 1.5), 0, 0.3), 0.001, 0.12)
        soft = fade(seg(rate(body[1], 0.9), 0, 0.3), 0.001, 0.12)
        layers = [at_peak(hit, 0.012, 0), (sigh, -0.12, -9), at_peak(land, 1.0, -3), at_peak(soft, 1.0, -8)]
        layers += grains([body[3], body[4]], 5, 0.02, 0.14, 2.6, 3.4, seed=41, gain_lo=-22, gain_hi=-15)
        out = mix(1.4, layers)
    return master(fade(out, 0.001, 0.15), -9)

# ---------------------------------------------------------------- сигнал крупного телеграфа

def warning(kind):
    if kind == 'a':   # «вдох»: обратный свист, обрезанный на пике, и низ под ним
        w = rev(bp(rate(whoosh[2], 0.8), 300, 3500))
        end = peak_time(w)
        swell = fade(seg(w, end - 0.40, 0.40), 0.03, 0.012, 1.0)
        low = lp(rev(seg(rate(kill[0], 0.9), 0, 0.4)), 300)
        out = mix(0.44, [(swell, 0, 0), (low[-int(0.40 * SR):], 0, -10)])
    elif kind == 'b':  # двойной лесной стук «тук-тук»
        knock = fade(lp(seg(rate(body[3], 0.72), 0, 0.2), 1300), 0.001, 0.08)
        out = mix(0.36, [at_peak(knock, 0.012, 0), at_peak(knock, 0.132, -3)])
    else:              # тёмный звон навстречу: сталь вниз и задом наперёд, в конце тихий «ток»
        m = rev(bp(rate(metal[2], 0.5), 350, 2200))
        end = peak_time(m)
        swell = fade(seg(m, end - 0.38, 0.38), 0.04, 0.012, 1.0)
        tick = fade(seg(rate(body[4], 2.2), 0, 0.08), 0.001, 0.05)
        out = mix(0.46, [(swell, 0, 0), at_peak(tick, 0.385, -10)])
    return master(fade(out, 0.002, 0.03), -11)

# ---------------------------------------------------------------- тихий взмах хранителя в начале замаха

def swing(kind):
    if kind == 'a':   # тяжёлый медленный свист
        s = fade(hp(lp(rate(whoosh[4], 0.7), 2600), 150), 0.01, 0.12)
    elif kind == 'b':  # взмах сабли владельца ниже + тихий вес
        s = mix(0.7, [(lp(rate(attack[2], 0.72), 3000), 0, 0), (lp(seg(rate(body[2], 0.6), 0, 0.5), 400), 0.02, -14)])
        s = fade(s, 0.01, 0.15)
    else:              # воздушный, «выдох»
        s = fade(bp(seg(rate(cast[1], 0.55), 0.25, 0.85), 250, 2400), 0.03, 0.25)
    nz = np.nonzero(np.abs(s) > np.abs(s).max() * 0.01)[0]
    s = s[max(0, nz[0] - int(0.005 * SR)):nz[-1] + 1]
    return master(fade(s, 0.004, 0.08), -12)

# ---------------------------------------------------------------- сборка, установка, страница

SLOTS = [
    # ключ, префикс файла, заголовок, пояснение
    ('volley', 'bud_volley', 'Залп Плюй-плода', 'Бутон раскрывается (0,8 с) и выпускает 5 плодов через 0,2 с. В игре раскрытие и каждый хлопок звучат по своим событиям.'),
    ('fruit', 'bud_fruit', 'Падение плода', 'Мягкий сочный хлопок в точке падения; звучит на каждом из пяти плодов.'),
    ('hurt', 'bud_hurt', 'Боль бутона', 'Тело бутона при ударе героя (сталь сабли звучит отдельно, как у всех).'),
    ('death', 'bud_death', 'Смерть бутона', 'От удара до касания земли (~1 с); осыпание потом — общее.'),
    ('warning', 'enemy_warning', 'Сигнал крупного телеграфа', 'Только в начале крупных: таран Камнекопыта, прыжок/коготь (и будущий вой) Вендиго, залп Плюй-плода.'),
    ('swing', 'guardian_swing', 'Замах хранителя (очень тихо)', 'Обычный замах хранителя больше не зовёт общий сигнал. Вариант «тишина» тоже годится: --install swing=none.'),
]
DESC = {
    'volley': {'a': 'листва и вдох, мягкое «пок»', 'b': 'скрип коры, глухое «тумп»', 'c': 'воздушный вдох, «пфф-ток», хлопки идут вверх'},
    'fruit': {'a': 'мягкий шлепок с брызгами', 'b': 'глухой плюх', 'c': 'хрусткая кожура'},
    'hurt': {'a': 'сочное «тук» и дрожь листьев', 'b': 'удар и хруст', 'c': 'высокий «ток» и скрип стебля'},
    'death': {'a': 'удар, сдувание, осыпание листвы, касание', 'b': 'глухой удар, долгое шуршание', 'c': 'сочный удар с каплями, выдох, касание'},
    'warning': {'a': '«вдох» — обратный свист', 'b': 'двойной стук «тук-тук»', 'c': 'тёмный звон навстречу'},
    'swing': {'a': 'тяжёлый медленный свист', 'b': 'взмах сабли ниже', 'c': 'воздушный выдох'},
}
BUILD = {'fruit': fruit, 'hurt': hurt, 'death': death, 'warning': warning, 'swing': swing}
INSTALL = {
    'fruit': ('Bud', 'BudFruitImpact'), 'hurt': ('Bud', 'BudHurt'), 'death': ('Bud', 'BudDeath'),
    'warning': ('EnemyWarning', 'EnemyWarning'), 'swing': ('GuardianSwing', 'GuardianSwing'),
}

def build_all():
    for kind in 'abc':
        opening = volley_open(kind)
        pops = [volley_pop(kind, i) for i in range(SHOTS)]
        write(os.path.join(PARTS, f'bud_volley_{kind}_open.wav'), opening)
        for i, p in enumerate(pops):
            write(os.path.join(PARTS, f'bud_volley_{kind}_pop{i + 1}.wav'), p)
        phrase = mix(VOLLEY_FIRST + (SHOTS - 1) * VOLLEY_STEP + 0.3,
                     [(opening, 0, 0)] + [(p, VOLLEY_FIRST + i * VOLLEY_STEP, -1) for i, p in enumerate(pops)])
        write(os.path.join(HERE, f'bud_volley_{kind}.wav'), phrase)
        report(f'bud_volley_{kind}', phrase)
        for key, fn in BUILD.items():
            prefix = next(s[1] for s in SLOTS if s[0] == key)
            a = fn(kind)
            write(os.path.join(HERE, f'{prefix}_{kind}.wav'), a)
            report(f'{prefix}_{kind}', a)

def remove(path):
    for p in (path, path + '.meta'):
        if os.path.exists(p): os.remove(p)

def install(choice):
    for key, kind in choice.items():
        if key == 'volley':
            folder = os.path.join(AUDIO, 'Bud')
            if kind == 'none':
                remove(os.path.join(folder, 'BudVolley.wav'))
                for i in range(SHOTS): remove(os.path.join(folder, f'BudPop_{i + 1:02d}.wav'))
                continue
            copy(os.path.join(PARTS, f'bud_volley_{kind}_open.wav'), os.path.join(folder, 'BudVolley.wav'))
            for i in range(SHOTS):
                copy(os.path.join(PARTS, f'bud_volley_{kind}_pop{i + 1}.wav'), os.path.join(folder, f'BudPop_{i + 1:02d}.wav'))
            continue
        folder, name = INSTALL[key]
        target = os.path.join(AUDIO, folder, name + '.wav')
        if kind == 'none': remove(target); continue
        prefix = next(s[1] for s in SLOTS if s[0] == key)
        copy(os.path.join(HERE, f'{prefix}_{kind}.wav'), target)

def copy(src, dst):
    # Перезапись того же файла сохраняет .meta и GUID — ссылки в профиле не рвутся.
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    with open(src, 'rb') as f: data = f.read()
    with open(dst, 'wb') as f: f.write(data)

def page(choice):
    rows = []
    for key, prefix, title, note in SLOTS:
        players = []
        for kind in 'abc':
            mark = ' <b>— сейчас в игре</b>' if choice.get(key) == kind else ''
            players.append(f'<p>{kind.upper()} — {DESC[key][kind]}{mark}<br><audio controls preload="none" src="{prefix}_{kind}.wav"></audio></p>')
        if choice.get(key) == 'none':
            players.append('<p><b>Сейчас в игре: тишина</b></p>')
        rows.append(f'<h2>{title}</h2>\n<p>{note}</p>\n' + '\n'.join(players))
    html = ('<!doctype html>\n<html lang="ru"><head><meta charset="utf-8"><title>Звуки: Плюй-плод и сигналы</title>\n'
            '<style>body{font:16px sans-serif;max-width:720px;margin:24px auto;padding:0 16px}'
            'audio{width:100%}h2{margin-top:32px}</style></head><body>\n'
            '<h1>Звуки Плюй-плода и сигналов — кандидаты 26.09</h1>\n'
            '<p>Всё собрано из своей семьи (HitBody, Kill, Whoosh, Cast, HitMetal, Dissolve, шаги, '
            'записи сабли Attack_01..05) — высота, фильтры, огибающие, слои. Выбор ставится командой '
            '<code>python build-bud-sfx.py --install volley=b fruit=a ...</code></p>\n'
            + '\n'.join(rows) + '\n</body></html>\n')
    with open(os.path.join(HERE, 'index.html'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(html)

if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--install', nargs='*', default=[], help='слот=a|b|c|none')
    ap.add_argument('--no-build', action='store_true')
    args = ap.parse_args()
    state_path = os.path.join(HERE, 'installed.json')
    choice = {key: 'a' for key, *_ in SLOTS}
    if os.path.exists(state_path):
        with open(state_path, encoding='utf-8') as f: choice.update(json.load(f))
    for item in args.install:
        key, kind = item.split('=')
        if key not in choice or kind not in ('a', 'b', 'c', 'none'): raise SystemExit(f'не понял: {item}')
        choice[key] = kind
    if not args.no_build: build_all()
    install(choice)
    with open(state_path, 'w', encoding='utf-8', newline='\n') as f: json.dump(choice, f, indent=1)
    page(choice)
    print('installed:', choice)
