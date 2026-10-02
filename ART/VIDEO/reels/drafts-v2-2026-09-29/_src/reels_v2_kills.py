"""Ролики v2 (29.09): «Сочные убийства» чистые + отдельные клипы убийств, «Было → стало» v2.

Запуск: python reels_v2_kills.py [kills|clips|before|all] [ru,en,clean]
(reels_v2.py рядом — другой набор: «Увернёшься?» и «Босс v2».)
Кадры исходников (60 fps mp4 из artifacts/capture/reels-v2 и старая запись владельца 31.08)
раскладываются в REELS_WORK/frames/<съёмка>/frame_%04d — это делает prep (≈1,4 ГБ, не в репозитории).
Правила владельца: в чистых версиях нет текста, цифр, музыки и концовки; звук — только наши клипы
из Audio/Combat; «Было → стало» — без сплита и без дат.
"""
import functools, json, os, subprocess, sys
import numpy as np
from PIL import Image, ImageFilter

V1 = r"C:/Users/d.grab/Desktop/the-game/ART/VIDEO/reels/drafts-2026-09-29/_src"
sys.path.insert(0, V1)
import reelkit as rk
from reelkit import W, H, FF, REPO, SR, text_block, ease_out, decode, contact_sheet, render_clip, IVORY
import reels as r1  # KILL_SFX / sfx_path первого раунда — те же клипы, что играла игра

OUT = REPO + "/ART/VIDEO/reels/drafts-v2-2026-09-29"
CLIPS = OUT + "/kills-clips"
CAP = REPO + "/artifacts/capture/reels-v2"
MUSIC = REPO + "/razlom/Assets/Resources/Audio/Music/Forest"
ENDCARD = REPO + "/ART/VIDEO/reels/endcard-2026-09-29/TWR-endcard-A-3s-1080x1920.mp4"
# Старая запись владельца: только «razlom - SampleScene - … Unity 6.5 … <дата>.mp4».
OLD = (r"C:/Users/d.grab/Videos/Captures/razlom - SampleScene - Windows, Mac, Linux - Unity 6.5 "
       r"(6000.5.10f1) _DX12_ 2026-08-31 11-17-03.mp4")
OLD_T0 = 7.5                        # кадр 1 старой нарезки = 7,5 с записи
OLD_GAME = (283, 101, 1201, 676)    # Game view в окне редактора (x, y, w, h)
WORK = os.environ.get('REELS_WORK', r"C:/Users/DE06B~1.GRA/AppData/Local/Temp/claude/"
                      r"C--Users-d-grab-Desktop-the-game/b9a62d32-b302-4af1-9255-9c30476f872b/scratchpad/ba")
FRAMES = WORK + "/frames"
TMP = WORK + "/v2"
for d in (OUT, CLIPS, TMP):
    os.makedirs(d, exist_ok=True)

ENC = ['-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-color_range', 'tv']
AAC = ['-c:a', 'aac', '-b:a', '256k']
FLASH = (255, 236, 210)             # тёплый свет вспышки (как у удара в «Дыме и свете»)
LIMIT = 0.84                        # потолок лимитера: запас под выброс AAC на резких атаках


def mp4(shot):
    return '%s/%s/%s_1080x1920_60fps.mp4' % (CAP, shot, shot)


def fr(t, fps=60, t0=0.0):
    """Номер кадра нарезки для времени t исходника (кадр 1 = t0)."""
    return (t - t0) * fps + 1


# ------------------------------------------------------------------ подготовка кадров
SHOTS = ['kill-wendigo-z07', 'kill-guardian-z07', 'kill-stonehoof-z07', 'kill-bud-z07', 'kill-thorncaster-z09',
         'kill-snarer-z09', 'kill-swarm-pack-z07', 'kill-splitter-z07', 'after-wendigo-howl', 'after-crowd-fight',
         'after-stonehoof-charge']


def prep():
    for s in SHOTS:
        d = FRAMES + '/' + s
        if os.path.isdir(d) and os.listdir(d):
            continue
        os.makedirs(d, exist_ok=True)
        subprocess.run([FF, '-loglevel', 'error', '-y', '-i', mp4(s), '-fps_mode', 'passthrough', '-q:v', '2',
                        d + '/frame_%04d.jpg'], check=True)
    d = FRAMES + '/old0831'
    if not (os.path.isdir(d) and os.listdir(d)):
        os.makedirs(d, exist_ok=True)
        x, y, w, h = OLD_GAME
        subprocess.run([FF, '-loglevel', 'error', '-y', '-ss', str(OLD_T0), '-i', OLD, '-t', '5.0',
                        '-vf', 'fps=30,crop=%d:%d:%d:%d' % (w, h, x, y), d + '/frame_%04d.png'], check=True)


def register():
    for s in SHOTS:
        rk.SOURCES[s] = (FRAMES + '/' + s, 60)
    rk.SOURCES['old0831'] = (FRAMES + '/old0831', 30)
    # reelkit читает jpg; старую нарезку держим в png — подменяем загрузчик модуля.
    base_load = rk.load.__wrapped__

    def load_any(name, n):
        if name == 'old0831':
            d, _ = rk.src_info(name)
            n = max(1, min(len(os.listdir(d)), n))
            return Image.open(os.path.join(d, 'frame_%04d.png' % n)).convert('RGB')
        return base_load(name, n)
    rk.load = functools.lru_cache(maxsize=48)(load_any)


def focus(fx, fy, zoom):
    return r1.focus(fx, fy, zoom=zoom)


# ------------------------------------------------------------------ рендер
def render(spec, out_path, fps=30, post=None, overlays=()):
    """Как reelkit.render_video, но с частотой кадров и пост-обработкой всего кадра (переход)."""
    n = int(round(spec['dur'] * fps))
    cmd = [FF, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '%dx%d' % (W, H),
           '-r', str(fps), '-i', '-', '-vf', 'scale=out_color_matrix=bt709:out_range=tv,format=yuv420p',
           '-c:v', 'libx264', '-preset', 'medium', '-crf', '15'] + ENC + [out_path]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for i in range(n):
        t = i / fps
        canvas = Image.new('RGBA', (W, H), (0, 0, 0, 255))
        for c in spec['clips']:
            if c['at'] <= t < c['at'] + c['dur']:
                render_clip(canvas, c, t)
        img = canvas.convert('RGB')
        if post:
            img = post(img, t)
        if overlays:
            img = img.convert('RGBA')
            for fn in overlays:
                fn(img, t)
            img = img.convert('RGB')
        proc.stdin.write(img.tobytes())
    proc.stdin.close(); proc.wait()
    if proc.returncode != 0:
        raise RuntimeError('ffmpeg failed: ' + out_path)


# ------------------------------------------------------------------ звук
def S(rel, t, v):
    return (max(0.0, t), r1.sfx_path(rel), v * r1.SFX_GAIN)


def kill_set(mob, k, skip_split=False):
    """Звуки добивания моба на вспышке k (как в первом раунде)."""
    out = r1.kill_sfx(mob, k)
    if skip_split:
        out = [e for e in out if 'crack_' not in e[1] and 'pop_' not in e[1]]
    return out


def mix(total, sfx, music=None):
    m = np.zeros((int(round(total * SR)), 2), np.float32)
    if music:
        a = decode(music[0], music[1], total)[:len(m)] * music[2]
        m[:len(a)] += a
    for t, path, g in sfx:
        s = decode(path) * g
        k = min(len(s), int(0.003 * SR))    # 3 мс подъёма: у клипов резкий старт, AAC даёт выброс
        s[:k] *= np.linspace(0, 1, k)[:, None]
        i = int(t * SR)
        if i < 0:               # начало до склейки — срезаем голову звука
            s, i = s[-i:], 0
        if i >= len(m):
            continue
        e = min(len(m), i + len(s))
        m[i:e] += s[:e - i]
    return m


def loudness(m):
    r = subprocess.run([FF, '-hide_banner', '-f', 'f32le', '-ar', str(SR), '-ac', '2', '-i', '-', '-af',
                        'loudnorm=I=-15:TP=-1.5:LRA=11:print_format=json', '-f', 'null', '-'],
                       input=m.astype('<f4').tobytes(), capture_output=True)
    err = r.stderr.decode('utf-8', 'ignore')
    return float(json.loads(err[err.rfind('{'):err.rfind('}') + 1])['input_i'])


def write_limited(m, gain, out_wav, fade_out=0.08):
    """Один общий коэффициент (без «насоса» loudnorm) → мягкий лимитер → wav."""
    m = m * gain
    fi = int(0.01 * SR); m[:fi] *= np.linspace(0, 1, fi)[:, None]
    fo = int(fade_out * SR)
    if fo:
        m[-fo:] *= np.linspace(1, 0, fo)[:, None]
    subprocess.run([FF, '-y', '-loglevel', 'error', '-f', 'f32le', '-ar', str(SR), '-ac', '2', '-i', '-',
                    # Хруст укусов корнеползуна — скачок 0,6 за сэмпл: без среза верхов AAC даёт +3 дБ выброса.
                    '-af', 'lowpass=f=15500:poles=2,alimiter=limit=%.2f:attack=2:release=50:level=false' % LIMIT,
                    '-c:a', 'pcm_s16le',
                    out_wav], input=m.astype('<f4').tobytes(), check=True)


def mux(video, wav, out):
    cmd = [FF, '-y', '-loglevel', 'error', '-i', video]
    if wav:
        cmd += ['-i', wav, '-map', '0:v', '-map', '1:a'] + AAC + ['-shortest']
    else:
        cmd += ['-map', '0:v', '-an']
    subprocess.run(cmd + ['-c:v', 'copy', '-movflags', '+faststart', out], check=True)


# ------------------------------------------------------------------ 1. Сочные убийства (чистые)
# (моб, съёмка, вспышки убийства на видео, окно, фокус, зум). Вспышка на видео = лог + ~0,05 с.
KILLS = [
    ('wendigo',     'kill-wendigo-z07',     [1.68],       (1.05, 2.55), (700, 760), 1.25),
    ('guardian',    'kill-guardian-z07',    [1.65],       (1.20, 2.50), (650, 820), 1.30),
    ('stonehoof',   'kill-stonehoof-z07',   [2.03],       (1.60, 2.90), (560, 740), 1.30),
    ('bud',         'kill-bud-z07',         [2.45],       (2.00, 3.30), (700, 720), 1.25),
    ('thorncaster', 'kill-thorncaster-z09', [2.45],       (2.00, 3.30), (670, 800), 1.40),
    ('snarer',      'kill-snarer-z09',      [2.18],       (1.75, 3.05), (670, 840), 1.40),
    ('swarm',       'kill-swarm-pack-z07',  [3.65, 4.32], (3.25, 4.75), (560, 860), 1.30),
    ('splitter',    'kill-splitter-z07',    [1.88],       (1.45, 2.95), (400, 880), 1.15),
]
SPLIT_T = 2.28          # раскол Расщепня на видео
WENDIGO_LAND = 1.22     # приземление прыжка Вендиго на видео


def kill_events(mob, ks):
    """Звуки одного убийства во времени исходника."""
    if mob == 'swarm':
        ev = []
        for j, k in enumerate(ks):
            ev += [S('Pelag/Attack_0%d' % (2 + 2 * j), k - 0.07, .31), S('Pelag/Finisher', k, .30),
                   S('Mobs/Generic/kill_0%d' % (1 + 2 * j), k, .30),
                   S('Mobs/RootSwarm/death_0%d' % (1 + 2 * j), k + 0.03, .40)]
        return ev
    ev = kill_set(mob, ks[0], skip_split=(mob == 'splitter'))
    if mob == 'wendigo':
        ev.append(S('Mobs/Wendigo/land_01', WENDIGO_LAND, .30))
    if mob == 'splitter':
        ev += [S('Mobs/Splitter/crack_02', SPLIT_T, .30), S('Mobs/Splitter/pop_04', SPLIT_T + 0.10, .22),
               S('Mobs/Splitter/pop_03', SPLIT_T + 0.17, .18)]
    return ev


def shift(ev, dt, lo=-1e9, hi=1e9):
    return [(t + dt, p, g) for t, p, g in ev if lo - 0.08 <= t + dt < hi]


def kills_spec():
    clips, sfx, t = [], [], 0.0
    for mob, shot, ks, (w0, w1), (fx, fy), z in KILLS:
        d = w1 - w0
        beats = [k - w0 for k in ks]
        clips.append(dict(src=shot, at=t, dur=d, crop=focus(fx, fy, z), punch=beats, flash=beats,
                          **{'from': fr(w0)}))
        # Берём звуки, начавшиеся в своём отрезке; хвосты перетекают через склейку.
        sfx += shift(kill_events(mob, ks), t - w0, t, t + d)
        t += d
    return dict(dur=t, clips=clips), sfx


def build_kills():
    spec, sfx = kills_spec()
    video = TMP + '/kills_clean.mp4'
    render(spec, video, fps=30)
    m = mix(spec['dur'], sfx)
    li = loudness(m)
    gain = 10 ** ((-15.0 - li) / 20)
    wav = TMP + '/kills_clean.wav'
    write_limited(m, gain, wav)
    json.dump({'gain': gain, 'measured_lufs': li}, open(TMP + '/kills_gain.json', 'w'))
    a = OUT + '/TWR-reel-kills-clean-sfx.mp4'
    mux(video, wav, a)
    mux(video, None, OUT + '/TWR-reel-kills-clean-silent.mp4')
    contact_sheet(a, OUT + '/TWR-reel-kills-clean-sheet.jpg', cols=11, every=0.25, w=150)
    print('ok kills %.2fs gain %.2f (%.1f LUFS до)' % (spec['dur'], gain, li))


def build_clips():
    """Каждое убийство отдельным клипом: окно ±0,5 с, тот же кроп, без панча/вспышки, 60 fps, наш звук."""
    gain = json.load(open(TMP + '/kills_gain.json'))['gain']
    for i, (mob, shot, ks, (w0, w1), (fx, fy), z) in enumerate(KILLS):
        a, b = w0 - 0.5, w1 + 0.5
        x0, y0, x1, y1 = focus(fx, fy, z)
        vf = ('crop=%d:%d:%d:%d,scale=1080:1920:flags=lanczos,'
              'scale=out_color_matrix=bt709:out_range=tv,format=yuv420p'
              % (round(x1 - x0), round(y1 - y0), round(x0), round(y0)))
        m = mix(b - a, shift(kill_events(mob, ks), -a, 0.0, b - a))
        wav = TMP + '/clip_%02d.wav' % (i + 1)
        write_limited(m, gain, wav)
        out = CLIPS + '/%02d-%s.mp4' % (i + 1, mob)
        subprocess.run([FF, '-y', '-loglevel', 'error', '-ss', '%.3f' % a, '-i', mp4(shot), '-i', wav,
                        '-t', '%.3f' % (b - a), '-map', '0:v', '-map', '1:a', '-vf', vf, '-r', '60',
                        '-c:v', 'libx264', '-preset', 'slow', '-crf', '15'] + ENC + AAC +
                       ['-movflags', '+faststart', out], check=True)
        print('ok clip', out)


# ------------------------------------------------------------------ 2. Было → стало v2
T = 2.60            # переход = удар в старой записи (11,40 с) = дроп музыки (80,0 с Normal_A)
MUSIC_START = 77.40
OLD_FROM = 8.80     # старая запись 8,80–11,40
OLD_CROP = (392, 0, 698, 544)       # 9:16 внутри Game view: рыцари и герой крупно
OLD_HIT_C = (523, 734)              # где в кадре старый удар — центр наезда
# «Стало»: (съёмка, старт на видео, длительность, фокус, зум, удары-панчи на видео)
NEW = [
    ('after-wendigo-howl',     0.52, 1.50, (620, 820), 1.15, [1.52]),   # взрыв воя на доле 81,0
    ('after-crowd-fight',      0.90, 1.50, (520, 900), 1.12, []),       # все девять видов, метки B
    ('after-stonehoof-charge', 4.10, 1.00, (560, 950), 1.15, [4.72]),   # таран по полосе в героя
    ('after-crowd-fight',      5.60, 1.00, (620, 880), 1.20, [5.88]),   # убийство + корни
    ('kill-splitter-z07',      1.65, 1.00, (400, 880), 1.15, [1.88]),   # убийство → раскол
]
LABELS = {'ru': ('Было', 'Стало'), 'en': ('Before', 'After')}


def before_spec():
    clips = [dict(src='old0831', at=0.0, dur=T, crop=OLD_CROP, **{'from': fr(OLD_FROM, 30, OLD_T0)})]
    t = T
    for shot, s0, d, (fx, fy), z, hits in NEW:
        beats = [h - s0 for h in hits]
        clips.append(dict(src=shot, at=t, dur=d, crop=focus(fx, fy, z), punch=beats, flash=beats,
                          **{'from': fr(s0)}))
        t += d
    return dict(dur=t, clips=clips)


def zoom_blur(img, z, amount, c, n=10):
    """Радиальное размытие: n копий с растущим масштабом вокруг точки c, усреднение."""
    acc = None
    for i in range(n):
        s = z * (1 + amount * i / (n - 1))
        w, h = W / s, H / s
        x0 = min(max(0, c[0] - w / 2), W - w); y0 = min(max(0, c[1] - h / 2), H - h)
        f = np.asarray(img.resize((W, H), Image.BILINEAR, box=(x0, y0, x0 + w, y0 + h)), np.float32)
        acc = f if acc is None else acc + f
    return acc / n


def post_before(img, t):
    if t < T:
        img = img.filter(ImageFilter.UnsharpMask(radius=2.2, percent=70, threshold=2))
    pre, after = 0.22, 0.40
    if T - pre <= t < T:        # наезд в удар старой записи, нарастают размытие и свет
        p = (t - (T - pre)) / pre
        z, a, fl, c = 1 + 0.45 * p * p, 0.14 * p, 0.85 * p * p, OLD_HIT_C
    elif T <= t < T + after:    # новая игра выезжает из света и зума
        q = (t - T) / after
        z, a, fl, c = 1.35 - 0.35 * ease_out(q), 0.12 * (1 - q) ** 1.5, 0.85 * (1 - ease_out(q)), (W / 2, H * 0.45)
    else:
        return img
    arr = zoom_blur(img, z, a, c)
    arr = arr * (1 - fl) + np.array(FLASH, np.float32) * fl
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))


def label_fn(text, at, until, fin, fout):
    """Маленькая подпись Philosopher Bold слева сверху, светлая, тонкая обводка и мягкая тень."""
    img, pad = text_block(text, 'phil', 76, IVORY, 3, 'left', 0.12, None, False)

    def fn(canvas, t):
        if t < at or t >= until:
            return
        a = min(1.0, (t - at) / fin if fin else 1.0, (until - t) / fout if fout else 1.0)
        im = img
        if a < 1.0:
            im = img.copy(); im.putalpha(img.getchannel('A').point(lambda v: int(v * a)))
        canvas.alpha_composite(im, (64 - pad, 230 - pad))
    return fn


def before_sfx():
    ev = [S('HitBody/body_hit_01', 9.667 - OLD_FROM, .30),                 # старый удар по рыцарю
          S('Whoosh/sword_whoosh_04', T - 0.25, .34),                      # свист в переход
          S('Pelag/Finisher', T, .42), S('Mobs/Generic/kill_02', T, .36)]  # удар-склейка
    t = T
    for shot, s0, d, _, _, _ in NEW:
        k = t - s0      # время видео → время ролика
        if shot == 'after-wendigo-howl':
            ev += [S('Mobs/Wendigo/howl_02', 1.52 - 0.81 + k, .34), S('Mobs/Wendigo/land_01', 1.52 + k, .34)]
        elif shot == 'after-crowd-fight' and s0 < 3:
            ev += [S('Mobs/Wendigo/land_01', 1.22 + k, .30), S('Mobs/Bud/splat_01', 1.85 + k, .30),
                   S('HitBody/body_hit_02', 2.02 + k, .22)]
        elif shot == 'after-stonehoof-charge':
            ev += [S('Mobs/Stonehoof/charge_01', s0 + k, .26), S('Mobs/Stonehoof/collision_01', 4.62 + k, .34),
                   S('Mobs/Generic/stun_01', 4.80 + k, .22)]
        elif shot == 'after-crowd-fight':
            ev += [S('Pelag/Attack_03', 5.81 + k, .31), S('Pelag/Finisher', 5.88 + k, .39),
                   S('Mobs/Generic/kill_03', 5.88 + k, .36), S('Mobs/RootSnarer/roots_01', 6.18 - 0.59 + k, .34)]
        elif shot == 'kill-splitter-z07':
            ev += kill_set('splitter', 1.88 + k, skip_split=True)
            ev += [S('Mobs/Splitter/crack_02', SPLIT_T + k, .30), S('Mobs/Splitter/pop_04', SPLIT_T + 0.10 + k, .22)]
        t += d
    return ev


def concat_endcard(content, wav, out):
    fc = ('[1:v]fps=30,format=yuv420p,settb=1/30,setpts=PTS-STARTPTS[e];'
          '[0:v]fps=30,format=yuv420p,settb=1/30,setpts=PTS-STARTPTS[c];[c][e]concat=n=2:v=1:a=0[v]')
    subprocess.run([FF, '-y', '-loglevel', 'error', '-i', content, '-i', ENDCARD, '-i', wav, '-filter_complex', fc,
                    '-map', '[v]', '-map', '2:a', '-c:v', 'libx264', '-preset', 'slow', '-crf', '17',
                    '-pix_fmt', 'yuv420p', '-r', '30'] + ENC + AAC +
                   ['-shortest', '-movflags', '+faststart', out], check=True)


def build_before(which=('ru', 'en', 'clean')):
    spec = before_spec()
    total = spec['dur'] + 3.0
    sfx = before_sfx()
    for v in which:
        ovs = []
        if v in LABELS:
            b, a = LABELS[v]
            ovs = [label_fn(b, 0.10, 2.50, 0.25, 0.15), label_fn(a, 2.75, spec['dur'], 0.25, 0.0)]
        content = TMP + '/before_%s.mp4' % v
        render(spec, content, fps=30, post=post_before, overlays=ovs)
        wav = TMP + '/before_%s.wav' % v
        if v == 'clean':        # без музыки: только наши удары, тот же подход, что у «Убийств»
            m = mix(total, sfx)
            write_limited(m, 10 ** ((-15.0 - loudness(m)) / 20), wav, fade_out=0.3)
        else:                   # музыка владельца Normal_A: дроп 80,0 с ровно на переходе
            rk.build_audio(total, (MUSIC + '/Forest_Normal_A.ogg', MUSIC_START), sfx, wav, fade_out=1.2)
        out = OUT + '/TWR-reel-before-after-v2-%s.mp4' % v
        concat_endcard(content, wav, out)
        contact_sheet(out, OUT + '/TWR-reel-before-after-v2-%s-sheet.jpg' % v, cols=12, every=0.25, w=150)
        print('ok', out, '%.2fs' % total)


if __name__ == '__main__':
    what = sys.argv[1] if len(sys.argv) > 1 else 'all'
    prep(); register()
    if what in ('kills', 'all'):
        build_kills()
    if what in ('clips', 'all'):
        build_clips()
    if what in ('before', 'all'):
        build_before(tuple(sys.argv[2].split(',')) if len(sys.argv) > 2 else ('ru', 'en', 'clean'))
