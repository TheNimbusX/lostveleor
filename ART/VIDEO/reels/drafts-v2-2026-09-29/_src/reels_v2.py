"""Рилсы 29.09, вторая волна: «Увернёшься от всех 4?» (петля) и «Босс v2» (тихий тизер).

python reels_v2.py dodge|boss|all [--frames] [--no-sheet]
Материал — дубли reels-v2 (60 fps, 1080×1920, без полос HP); звуки — по журналу [audio-log]
(audiolog.py), только клипы игры. Выход — ART/VIDEO/reels/drafts-v2-2026-09-29.
"""
import functools, json, math, os, subprocess, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = r"C:/Users/d.grab/Desktop/the-game"
sys.path.insert(0, REPO + "/ART/VIDEO/reels/drafts-2026-09-29/_src")
import reelkit as rk  # noqa: E402  текст, контактные листы, ffmpeg
from audiolog import audio_map, clip_path  # noqa: E402

FF = rk.FF
W, H, FPS, SR = 1080, 1920, 30, 48000
OUT = REPO + "/ART/VIDEO/reels/drafts-v2-2026-09-29"
CAP = REPO + "/artifacts/capture/reels-v2"
WORK = REPO + "/artifacts/reels-work-v2"
AUD = REPO + "/razlom/Assets/Resources/Audio"
ENDCARD = REPO + "/ART/VIDEO/reels/endcard-2026-09-29/TWR-endcard-A-3s-1080x1920.mp4"
FOREST_HARD = AUD + "/Music/Forest/Forest_Hard.ogg"
# 120 BPM, первая доля 0.029 с; такты 35–40 — ровные по громкости, такт перед началом похож
# на последний такт отрезка, поэтому шов петли на музыке не слышен.
HARD_FROM = 70.029
rk.FONTS['philr'] = REPO + "/razlom/Assets/Resources/UI/Fonts/Philosopher-Regular.ttf"
SAFE_L, SAFE_R, SAFE_T, SAFE_B = rk.SAFE_L, rk.SAFE_R, rk.SAFE_T, rk.SAFE_B
SAFE_CX = rk.SAFE_CX
IVORY, INK = rk.IVORY, rk.INK
RED = (214, 58, 44)
EMBER = (255, 150, 70)
os.makedirs(WORK, exist_ok=True)


# ================================================================ кадры источника
def mp4(shot):
    return '%s/%s/%s_1080x1920_60fps.mp4' % (CAP, shot, shot)


def grab(shot, s0, s1):
    """Кадры mp4 с номерами n (время n/60) в WORK/<shot>/f_nnnnn.jpg — только недостающие."""
    d = '%s/%s' % (WORK, shot)
    os.makedirs(d, exist_ok=True)
    a, b = max(0, int(math.floor(s0 * 60)) - 2), int(math.ceil(s1 * 60)) + 2
    need = [n for n in range(a, b + 1) if not os.path.exists('%s/f_%05d.jpg' % (d, n))]
    if not need:
        return
    a, b = min(need), max(need)
    subprocess.run([FF, '-y', '-loglevel', 'error', '-i', mp4(shot), '-vf', 'select=between(n\\,%d\\,%d)' % (a, b),
                    '-fps_mode', 'passthrough', '-start_number', str(a), '-q:v', '2', '%s/f_%%05d.jpg' % d], check=True)


@functools.lru_cache(maxsize=12)
def frame_n(shot, n):
    d = '%s/%s' % (WORK, shot)
    p = '%s/f_%05d.jpg' % (d, n)
    while not os.path.exists(p) and n > 0:  # за концом записи — последний кадр
        n -= 1
        p = '%s/f_%05d.jpg' % (d, n)
    return np.asarray(Image.open(p).convert('RGB'))


def frame_at(shot, s, blend):
    """Кадр источника на время s; в замедлении — смесь двух соседних кадров по дробной части."""
    f = max(0.0, s * 60.0)
    a = int(math.floor(f))
    w = f - a
    if not blend or w < 0.06:
        return frame_n(shot, a if w < 0.5 or blend else a + 1)
    if w > 0.94:
        return frame_n(shot, a + 1)
    x = frame_n(shot, a).astype(np.float32)
    y = frame_n(shot, a + 1).astype(np.float32)
    return (x + (y - x) * w).astype(np.uint8)


# ================================================================ клип со скоростной кривой
class Clip:
    """segs: [(s0, s1, dur)] — отрезок источника s0→s1 за dur секунд ролика; s0 == s1 — стоп-кадр.
    focus: [(s, fx, fy)] по времени ИСТОЧНИКА (камера игры едет — кроп едет за ней);
    punch/flash/shake — по времени источника (первое прохождение), затухание — по времени ролика."""

    def __init__(self, src, at, segs, focus, zoom=1.1, punch=(), flash=(), shake=(), slowzoom=0.05,
                 kb=None, dark=None, vign=0.0, flash_amt=0.22, grade=True, wobble=None):
        self.src, self.at, self.segs = src, at, segs
        self.dur = sum(d for _, _, d in segs)
        self.focus, self.zoom, self.slowzoom, self.kb, self.dark, self.vign = focus, zoom, slowzoom, kb, dark, vign
        self.flash_amt, self.grade, self.wobble = flash_amt, grade, wobble
        self.punch = [(self.out_time(s), k) for s, k in punch]
        self.flash = [self.out_time(s) for s in flash]
        self.shake = [(self.out_time(s), a) for s, a in shake]
        lo = min(min(a, b) for a, b, _ in segs)
        hi = max(max(a, b) for a, b, _ in segs)
        grab(src, lo - 0.05, hi + 0.05)
        # Замедленные участки в секундах ролика (для звука и для «дыхания» кадра).
        self.slow = []
        t = at
        for s0, s1, d in segs:
            if s1 > s0 and (s1 - s0) / d < 0.9:
                self.slow.append((t, t + d))
            t += d

    def src_time(self, T):
        r = T - self.at
        for s0, s1, d in self.segs:
            if r < d:
                sp = (s1 - s0) / d if d > 0 else 0.0
                return s0 + r * sp, sp
            r -= d
        s0, s1, d = self.segs[-1]
        return s1, 1.0

    def out_time(self, s):
        t = self.at
        for s0, s1, d in self.segs:
            if s1 > s0 and s0 <= s <= s1:
                return t + (s - s0) / (s1 - s0) * d
            t += d
        raise ValueError('%s: %.3f вне отрезков' % (self.src, s))

    def fxy(self, s):
        k = self.focus
        if s <= k[0][0]:
            return k[0][1], k[0][2]
        for (a, x0, y0), (b, x1, y1) in zip(k, k[1:]):
            if s <= b:
                u = (s - a) / (b - a)
                u = u * u * (3 - 2 * u)
                return x0 + (x1 - x0) * u, y0 + (y1 - y0) * u
        return k[-1][1], k[-1][2]

    def slow_amt(self, T):
        """0..1: насколько кадр «в замедлении» (для приближения и виньетки); сброс на рывке — 0.07 с."""
        v = 0.0
        for a, b in self.slow:
            if a <= T < b:
                v = max(v, min(1.0, (T - a) / 0.12) * ((T - a) / (b - a)))
            elif b <= T < b + 0.4:
                v = max(v, math.exp(-(T - b) / 0.07))
        return v

    def render(self, T):
        s, sp = self.src_time(T)
        img = frame_at(self.src, s, blend=0 < sp < 0.9)
        z = self.zoom
        r = T - self.at
        if self.kb:
            z *= self.kb[0] + (self.kb[1] - self.kb[0]) * min(1.0, r / self.dur)
        z *= 1 + self.slowzoom * self.slow_amt(T)
        for p, k in self.punch:
            if T >= p:
                z *= 1 + k * math.exp(-(T - p) / 0.09)
        fx, fy = self.fxy(s)
        dx = dy = 0.0
        for p, a in self.shake:
            if T >= p:
                e = a * math.exp(-(T - p) / 0.16)
                dx += e * math.sin((T - p) * 71.0)
                dy += e * math.cos((T - p) * 53.0)
        if self.wobble:
            ddx, ddy = self.wobble(T)
            dx, dy = dx + ddx, dy + ddy
        w, h = W / z, H / z
        x0 = min(max(0.0, fx - w / 2), W - w) + dx / z
        y0 = min(max(0.0, fy - h / 2), H - h) + dy / z
        x0, y0 = min(max(0.0, x0), W - w), min(max(0.0, y0), H - h)
        frame = Image.fromarray(img).resize((W, H), Image.BICUBIC, box=(x0, y0, x0 + w, y0 + h))
        arr = np.asarray(frame).astype(np.float32)
        if self.grade:
            arr = grade(arr)
        v = self.vign * self.slow_amt(T)
        if self.dark:
            k = self.dark(r)
            arr *= np.array([k * 0.93, k * 0.96, k * 1.04], np.float32)
            v = max(v, (1 - k) * 0.8)
        if v > 0.01:
            arr *= 1 - v * VIGN
        for p in self.flash:
            if 0 <= T - p < 0.14:
                a = self.flash_amt * (1 - (T - p) / 0.14)
                arr += (np.array([255, 236, 214], np.float32) - arr) * a
        return np.clip(arr, 0, 255)


def _vignette():
    y, x = np.mgrid[0:H, 0:W].astype(np.float32)
    d = np.sqrt(((x - W / 2) / (W * 0.62)) ** 2 + ((y - H / 2) / (H * 0.60)) ** 2)
    return np.clip((d - 0.45) / 0.75, 0, 1)[..., None] ** 1.6


VIGN = _vignette()


def grade(arr):
    """Мягко убираем охру пола: насыщенность −12 % и чуть прохладнее в светах; тени не трогаем."""
    lum = arr @ np.array([0.299, 0.587, 0.114], np.float32)
    arr = lum[..., None] + (arr - lum[..., None]) * 0.88
    return arr * np.array([0.985, 1.0, 1.03], np.float32)


# ================================================================ текст и значки
def text_img(text, font, size, stroke=None, spacing=0.12):
    img, pad = rk.text_block(text, font, size, IVORY, stroke, 'center', spacing, None, False)
    return img, pad


def text_fit(text, font, size, maxw, stroke=None):
    while True:
        img, pad = text_img(text, font, size, stroke)
        if img.width - 2 * pad <= maxw or size <= 30:
            return img, pad
        size -= 4


def put(canvas, img, pad, cx, top, alpha=1.0):
    """Блок текста: центр по x, верх букв на top."""
    if alpha <= 0.0:
        return
    if alpha < 1.0:
        img = img.copy()
        img.putalpha(img.getchannel('A').point(lambda v: int(v * alpha)))
    canvas.alpha_composite(img, (int(cx - img.width / 2), int(top - pad)))


def draw_tick(d, cx, cy, r, color, width):
    d.line([(cx - r * 0.55, cy + r * 0.02), (cx - r * 0.15, cy + r * 0.42), (cx + r * 0.6, cy - r * 0.45)],
           fill=color, width=width, joint='curve')


def draw_cross(d, cx, cy, r, color, width):
    k = r * 0.5
    d.line([(cx - k, cy - k), (cx + k, cy + k)], fill=color, width=width)
    d.line([(cx - k, cy + k), (cx + k, cy - k)], fill=color, width=width)


@functools.lru_cache(maxsize=64)
def counter_img(k, states, pop_key):
    """Строка «k/4» + четыре кружка. states: строка из 4 символов: '.' впереди, 'c' текущий, 'v' пройден, 'x' провал."""
    num, pad = text_img('%d/4' % k, 'phil', 62)
    tw, th = num.width - 2 * pad, num.height - 2 * pad
    R, gap = 17, 16
    pw = 4 * 2 * R + 3 * gap
    extra = 58 if 'x' in states else 0
    bw = tw + 26 + pw + extra
    img = Image.new('RGBA', (bw + 60, th + 60), (0, 0, 0, 0))
    img.alpha_composite(num, (30 - pad, 30 - pad))
    cy = 30 + th * 0.56
    sh = Image.new('L', img.size, 0)
    ds = ImageDraw.Draw(sh)
    for i in range(4):
        cx = 30 + tw + 26 + R + i * (2 * R + gap)
        ds.ellipse((cx - R - 4, cy - R - 2, cx + R + 4, cy + R + 6), fill=190)
    if extra:
        cx = 30 + tw + 26 + pw + 34
        ds.ellipse((cx - 26, cy - 24, cx + 26, cy + 28), fill=150)
    shadow = Image.new('RGBA', img.size, INK + (0,))
    shadow.putalpha(sh.filter(ImageFilter.GaussianBlur(6)))
    out = Image.alpha_composite(shadow, img)
    d = ImageDraw.Draw(out)
    for i, st in enumerate(states):
        cx = 30 + tw + 26 + R + i * (2 * R + gap)
        box = (cx - R, cy - R, cx + R, cy + R)
        if st == '.':
            d.ellipse(box, fill=INK + (150,), outline=IVORY + (200,), width=3)
        elif st == 'c':
            d.ellipse((cx - R - 5, cy - R - 5, cx + R + 5, cy + R + 5), outline=EMBER + (140,), width=3)
            d.ellipse(box, fill=EMBER + (255,), outline=IVORY + (255,), width=3)
        elif st == 'v':
            d.ellipse(box, fill=IVORY + (255,), outline=IVORY + (255,), width=2)
            draw_tick(d, cx, cy, R, INK + (255,), 5)
        elif st == 'x':
            d.ellipse(box, fill=RED + (255,), outline=IVORY + (255,), width=3)
            draw_cross(d, cx, cy, R, IVORY + (255,), 5)
    if extra:
        cx = 30 + tw + 26 + pw + 34
        draw_cross(d, cx, cy, 44, INK + (255,), 15)
        draw_cross(d, cx, cy, 44, RED + (255,), 9)
    return out


# ================================================================ звук
@functools.lru_cache(maxsize=None)
def dec(path, af=None, start=0.0, dur=None):
    cmd = [FF, '-loglevel', 'error']
    if start:
        cmd += ['-ss', '%.3f' % start]
    cmd += ['-i', path]
    if dur:
        cmd += ['-t', '%.3f' % dur]
    if af:
        cmd += ['-af', af]
    cmd += ['-ac', '2', '-ar', str(SR), '-f', 'f32le', '-']
    raw = subprocess.run(cmd, capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()


def resample(x, rate):
    """Скорость воспроизведения rate (<1 — ниже и длиннее), как замедленная плёнка."""
    if abs(rate - 1.0) < 1e-3:
        return x
    n = int(len(x) / rate)
    idx = np.arange(n) * rate
    return np.stack([np.interp(idx, np.arange(len(x)), x[:, c]) for c in range(2)], 1).astype(np.float32)


def add(mix, T, x, gain=1.0):
    i = int(round(T * SR))
    if i < 0:
        x = x[-i:]
        i = 0
    e = min(len(mix), i + len(x))
    if e > i:
        mix[i:e] += x[:e - i] * gain


def finalize(mix, out_wav, fin=0.01, fout=0.01, target=-14.0):
    fi = max(1, int(fin * SR))
    mix[:fi] *= np.linspace(0, 1, fi)[:, None]
    fo = max(1, int(fout * SR))
    mix[-fo:] *= np.linspace(1, 0, fo)[:, None] ** 1.5
    tmp = out_wav + '.pre.wav'
    rk.write_wav(tmp, mix)
    r = subprocess.run([FF, '-hide_banner', '-i', tmp, '-af', 'loudnorm=I=%g:TP=-1.5:LRA=11:print_format=json' % target,
                        '-f', 'null', '-'], capture_output=True, text=True)
    m = json.loads(r.stderr[r.stderr.rfind('{'):r.stderr.rfind('}') + 1])
    af = ('loudnorm=I=%g:TP=-1.5:LRA=11:measured_I=%s:measured_TP=%s:measured_LRA=%s:measured_thresh=%s:'
          'offset=%s:linear=true,alimiter=limit=0.84:attack=2:release=40:level=false'
          % (target, m['input_i'], m['input_tp'], m['input_lra'], m['input_thresh'], m['target_offset']))
    subprocess.run([FF, '-y', '-loglevel', 'error', '-i', tmp, '-af', af, '-ar', str(SR), out_wav], check=True)
    os.remove(tmp)
    return out_wav


def sfx(T, path, g, rate=1.0, af=None, off=0.0, until=None):
    """Событие звука: T — время ролика; off — начать клип с середины; until — обрезать (время ролика)."""
    return dict(T=T, path=path, g=g, rate=rate, af=af, off=off, until=until)


def clip_sfx(clip, gain=1.0, pre=0.1, skip=('Footstep',), until=None):
    """События журнала этого дубля на время ролика: как сыграла игра (клип, громкость), по тикам.
    Звук, начавшийся до входа в клип (не раньше pre с), играет с середины; в замедлении — чуть ниже."""
    lo = clip.segs[0][0]
    hi = max(b for _, b, _ in clip.segs)
    ev = []
    for t, sound, name, vol, path, _ in audio_map(clip.src):
        if sound in skip or path is None or t >= hi or t < lo - pre:
            continue
        if t < lo:
            ev.append(sfx(clip.at, path, vol * gain, off=lo - t, until=until))
            continue
        T = clip.out_time(t)
        rate = 0.82 if any(a <= T < b - 0.02 for a, b in clip.slow) else 1.0
        ev.append(sfx(T, path, vol * gain, rate, until=until))
    return ev


def mix_events(total, events):
    mix = np.zeros((int(round(total * SR)), 2), np.float32)
    for e in events:
        x = resample(dec(e['path'], e['af']), e['rate'])
        if e['off'] > 0:
            x = x[int(e['off'] * SR):]
        if e['until'] is not None:
            n = max(0, int((e['until'] - e['T']) * SR))
            x = x[:n].copy()
            f = min(len(x), int(0.03 * SR))
            if f:
                x[-f:] *= np.linspace(1, 0, f)[:, None]
        add(mix, e['T'], x, e['g'])
    return mix


def slow_env(total, windows, attack=0.08, release=0.02):
    """1 в замедлении, 0 в реальном времени; вход мягкий, выход (рывок) почти мгновенный."""
    n = int(round(total * SR))
    t = np.arange(n) / SR
    e = np.zeros(n, np.float32)
    for a, b in windows:
        m = (t >= a) & (t < b)
        e[m] = np.maximum(e[m], np.minimum(1.0, (t[m] - a) / attack))
        m2 = (t >= b) & (t < b + 6 * release)
        e[m2] = np.maximum(e[m2], np.exp(-(t[m2] - b) / release))
    return e[:, None]


# ================================================================ вывод
ENC = ['-c:v', 'libx264', '-preset', 'medium', '-crf', '17', '-pix_fmt', 'yuv420p', '-r', str(FPS),
       '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-color_range', 'tv']


def open_pipe(path):
    cmd = [FF, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '%dx%d' % (W, H),
           '-r', str(FPS), '-i', '-', '-vf', 'scale=out_color_matrix=bt709:out_range=tv,format=yuv420p',
           '-c:v', 'libx264', '-preset', 'medium', '-crf', '16', '-colorspace', 'bt709', '-color_primaries', 'bt709',
           '-color_trc', 'bt709', '-color_range', 'tv', path]
    return subprocess.Popen(cmd, stdin=subprocess.PIPE)


def render(total, base_fn, variants, only_frames=None):
    """base_fn(T) → float-массив кадра; variants: {имя: (путь, overlay_fn(canvas RGBA, T) | None)}."""
    pipes = {k: open_pipe(p) for k, (p, _) in variants.items()}
    n = int(round(total * FPS))
    for i in range(n):
        T = i / FPS
        base = Image.fromarray(np.clip(base_fn(T), 0, 255).astype(np.uint8)).convert('RGBA')
        for k, (_, fn) in variants.items():
            c = base.copy() if fn else base
            if fn:
                fn(c, T)
            pipes[k].stdin.write(c.convert('RGB').tobytes())
    for p in pipes.values():
        p.stdin.close()
        p.wait()
        if p.returncode:
            raise RuntimeError('ffmpeg')


def mux(video, wav, out):
    subprocess.run([FF, '-y', '-loglevel', 'error', '-i', video, '-i', wav, '-map', '0:v', '-map', '1:a', '-c:v', 'copy',
                    '-c:a', 'aac', '-b:a', '192k', '-shortest', '-movflags', '+faststart', out], check=True)


def with_endcard(video, wav, out, content_dur, xfade=0.0):
    """Контент + концовка A 3 с (наплыв xfade); звук — готовый wav на всю длину."""
    fc = ('[1:v]fps=30,format=yuv420p,settb=1/30,setsar=1[e];[0:v]fps=30,format=yuv420p,settb=1/30,setsar=1[c];')
    if xfade > 0:
        fc += '[c][e]xfade=transition=fade:duration=%.3f:offset=%.3f[v]' % (xfade, content_dur - xfade)
    else:
        fc += '[c][e]concat=n=2:v=1:a=0[v]'
    subprocess.run([FF, '-y', '-loglevel', 'error', '-i', video, '-i', ENDCARD, '-i', wav, '-filter_complex', fc,
                    '-map', '[v]', '-map', '2:a'] + ENC + ['-c:a', 'aac', '-b:a', '192k', '-shortest',
                                                           '-movflags', '+faststart', out], check=True)


def sheet(video, cols=8, every=0.25, w=150):
    out = os.path.splitext(video)[0] + '-sheet.jpg'
    return rk.contact_sheet(video, out, cols=cols, every=every, w=w)


# ================================================================ «Увернёшься от всех 4?»
DODGE_TOTAL = 12.0
TITLE = {'ru': 'Увернёшься\nот всех 4?', 'en': 'Can you dodge\nall 4?'}
ASK = {'ru': 'А ты бы\nувернулся?', 'en': 'Would you?'}


def dodge_clips():
    B = []
    # 1 — корнелом: круг уже под героем в первом кадре; рывок — герой на кромке круга.
    B.append(Clip('dodge-snarer-z09', 0.0, [(4.983, 5.40, 1.42), (5.40, 6.48, 1.08)],
                  [(4.9, 560, 960), (5.6, 520, 960)], zoom=1.16, punch=[(5.68, 0.06)], vign=0.35))
    # 2 — шипомёт: линия появляется, замедление, герой выходит, шипы бьют в пустую линию.
    B.append(Clip('dodge-thorncaster-z10', 2.5, [(5.04, 5.20, 0.16), (5.20, 5.56, 1.14), (5.56, 6.76, 1.20)],
                  [(5.0, 560, 980), (6.0, 580, 940)], zoom=1.12, punch=[(5.99, 0.05)], vign=0.35))
    # 3 — каменный вепрь: полоса, замедление, шаг из полосы; ожидание разгона ×2; вепрь проносится мимо.
    B.append(Clip('dodge-stonehoof-z10', 5.0,
                  [(3.22, 3.36, 0.14), (3.36, 3.80, 1.33), (3.80, 4.05, 0.25), (4.05, 4.45, 0.20), (4.45, 5.03, 0.58)],
                  [(3.2, 540, 880), (4.0, 520, 880), (4.8, 440, 900)], zoom=1.1, punch=[(4.86, 0.06)], vign=0.35))
    # 4 — вендиго, круговой взмах 360: провал — стоп-кадр на ударе, вспышка, отброс в замедлении.
    B.append(Clip('dodge-wendigo-turn-z10', 7.5,
                  [(2.86, 2.97, 0.11), (2.97, 3.50, 1.87), (3.50, 3.63, 0.13), (3.63, 3.63, 0.15),
                   (3.63, 3.90, 0.77), (3.90, 4.10, 0.22)],
                  [(2.8, 520, 960), (3.9, 540, 900), (4.1, 560, 880)], zoom=1.1,
                  punch=[(3.63, 0.09)], flash=[3.63], shake=[(3.63, 16)], flash_amt=0.42, vign=0.35))
    # Финал: почти стоп-кадр после удара, темнее — вопрос зрителю; дальше петля в кадр 1.
    B.append(Clip('dodge-wendigo-turn-z10', 10.75, [(4.10, 4.225, 1.25)],
                  [(4.1, 560, 880)], zoom=1.1, slowzoom=0.0,
                  dark=lambda r: 1 - 0.38 * min(1.0, r / 0.25)))
    return B


# Когда кружок получает галочку (время ролика) и когда провал.
def dodge_marks(B):
    return {0: B[0].out_time(5.72), 1: B[1].out_time(6.02), 2: B[2].out_time(4.88)}, B[3].out_time(3.63)


def dodge_overlay(lang, B):
    ticks, fail_at = dodge_marks(B)
    title, tpad = text_img(TITLE[lang], 'phil', 112 if lang == 'ru' else 104, spacing=0.02)
    ask, apad = text_fit(ASK[lang], 'phil', 112 if lang == 'ru' else 120, SAFE_R - SAFE_L - 40)
    title_top = 236
    row_top = title_top + (title.height - 2 * tpad) + 26

    def fn(c, T):
        put(c, title, tpad, SAFE_CX, title_top)
        k = 0 if T < 2.5 else 1 if T < 5.0 else 2 if T < 7.5 else 3
        st = ''
        for i in range(4):
            if i < k or (i in ticks and T >= ticks[i]):
                st += 'v'
            elif i == 3 and T >= fail_at:
                st += 'x'
            elif i == k:
                st += 'c'
            else:
                st += '.'
        img = counter_img(k + 1, st, 0)
        # Толчок значка на галочке/кресте.
        s = 1.0
        for t0 in list(ticks.values()) + [fail_at]:
            if 0 <= T - t0 < 0.25:
                s = 1 + 0.18 * (1 - (T - t0) / 0.25) ** 2
        if s != 1.0:
            img = img.resize((int(img.width * s), int(img.height * s)), Image.BICUBIC)
        dx = 0
        if 0 <= T - fail_at < 0.3:
            dx = int(10 * math.sin((T - fail_at) * 60) * (1 - (T - fail_at) / 0.3))
        c.alpha_composite(img, (int(SAFE_CX - img.width / 2 + dx), int(row_top - 30 - (img.height - img.height / s) / 2)))
        if T >= 10.78:
            a = min(1.0, (T - 10.78) / 0.1)
            put(c, ask, apad, SAFE_CX, 1080, a)
    return fn


def dodge_audio(B, music, with_end=False):
    total = DODGE_TOTAL + (3.0 if with_end else 0.0)
    C = AUD + '/Combat'
    ev = []
    for b in B[:4]:
        ev += clip_sfx(b, gain=2.2)
    # Кадр 1: сигнал угрозы игры (у корнелома он звучит за 0.5 с до круга, до начала дубля).
    ev.append(sfx(0.0, C + '/EnemyWarning/EnemyWarning.wav', 0.14 * 2.2))
    # Рывок из замедления — рывок героя (его же звук).
    for b in B[:3]:
        ev.append(sfx(b.slow[0][1] - 0.05, C + '/Pelag/Dash.wav', 0.30))
    # Провал: удар по телу поверх удара вендиго; в финале — стук сердца игры (низкое здоровье).
    fail = B[3].out_time(3.63)
    ev.append(sfx(fail, C + '/HitBody/body_hit_02.ogg', 0.40))
    ev.append(sfx(10.78, AUD + '/Game/Prepared/low_health_loop.ogg', 0.55))
    mix = mix_events(total, ev)
    if music:
        dry = dec(FOREST_HARD, None, HARD_FROM, total)
        lp = dec(FOREST_HARD, 'lowpass=f=420,lowpass=f=420', HARD_FROM, total)
        n = min(len(mix), len(dry), len(lp))
        e = slow_env(total, [w for b in B[:4] for w in b.slow] + [(10.75, 12.0)])[:n]
        bed = dry[:n] * (1 - e) + lp[:n] * e * 0.42
        g = np.ones((n, 1), np.float32) * 0.45
        if with_end:  # под концовкой музыка уходит за 1.2 с
            t = np.arange(n) / SR
            g[:, 0] *= np.clip(1 - (t - DODGE_TOTAL) / 1.2, 0, 1) ** 1.5
        mix[:n] += bed * g
    return mix


def build_dodge(frames_only=False):
    B = dodge_clips()
    for b in B[:4]:
        print('beat %s: %.2f..%.2f slow %s' % (b.src, b.at, b.at + b.dur, ['%.2f-%.2f' % w for w in b.slow]))
    ticks, fail = dodge_marks(B)
    print('ticks', {k: round(v, 2) for k, v in ticks.items()}, 'fail %.2f' % fail)

    def base(T):
        for b in reversed(B):
            if T >= b.at - 1e-6:
                return b.render(T)
        return B[0].render(T)

    tmp = WORK + '/dodge'
    os.makedirs(tmp, exist_ok=True)
    variants = {'ru': (tmp + '/v-ru.mp4', dodge_overlay('ru', B)), 'en': (tmp + '/v-en.mp4', dodge_overlay('en', B)),
                'clean': (tmp + '/v-clean.mp4', None)}
    if frames_only:
        for T in (0.0, 1.0, 1.8, 3.5, 6.2, 7.45, 9.0, 9.55, 11.5):
            c = Image.fromarray(np.clip(base(T), 0, 255).astype(np.uint8)).convert('RGBA')
            variants['ru'][1](c, T)
            c.convert('RGB').resize((360, 640), Image.LANCZOS).save(tmp + '/still_%05.2f.jpg' % T, quality=85)
        return
    render(DODGE_TOTAL, base, variants)
    wav_m = finalize(dodge_audio(B, True), tmp + '/a-music.wav')
    wav_s = finalize(dodge_audio(B, False), tmp + '/a-sfx.wav')
    wav_e = finalize(dodge_audio(B, True, with_end=True), tmp + '/a-end.wav', fout=0.3)
    outs = []
    for lang in ('ru', 'en'):
        v = variants[lang][0]
        o = OUT + '/TWR-reel-dodge-%s.mp4' % lang
        mux(v, wav_m, o); outs.append(o)
        o = OUT + '/TWR-reel-dodge-%s-sfx.mp4' % lang
        mux(v, wav_s, o); outs.append(o)
        o = OUT + '/TWR-reel-dodge-%s-endcard.mp4' % lang
        with_endcard(v, wav_e, o, DODGE_TOTAL); outs.append(o)
    o = OUT + '/TWR-reel-dodge-clean.mp4'
    mux(variants['clean'][0], wav_s, o); outs.append(o)
    return outs


# ================================================================ «Босс v2»
BOSS_CUT = 3.03          # конец нарезки героев
BOSS_CONTENT = 9.6       # конец спокойного кадра (дальше наплыв в концовку)
BOSS_XF = 0.4
THUMPS = [(5.0, 0.55), (6.2, 0.7), (7.3, 0.85), (8.35, 1.0)]  # шаги «чего-то большого»: время, сила
BOSS_LINE1 = {'ru': 'Кто хозяин этого леса?', 'en': 'Who rules this forest?'}
BOSS_LINE2 = {'ru': 'Опиши его в комментариях', 'en': 'Describe it in the comments'}


def boss_clips():
    C = []
    # Нарезка — по 0.7–0.8 с, склейка сразу после удара; от малого к большому.
    C.append(Clip('boss-hero-roots', 0.0, [(5.10, 5.84, 0.74)], [(5.1, 420, 820)], zoom=1.3, kb=(1.0, 1.04),
                  punch=[(5.70, 0.06)], slowzoom=0.0))
    C.append(Clip('boss-hero-charge', 0.74, [(4.03, 4.78, 0.75)], [(4.0, 520, 700), (4.78, 520, 720)], zoom=1.3,
                  kb=(1.0, 1.04), punch=[(4.68, 0.07)], shake=[(4.68, 8)], slowzoom=0.0))
    C.append(Clip('kill-wendigo-z07', 1.49, [(0.50, 1.26, 0.76)], [(0.5, 660, 660), (1.2, 580, 700)], zoom=1.25, kb=(1.0, 1.04),
                  punch=[(1.18, 0.07)], slowzoom=0.0))
    C.append(Clip('after-wendigo-howl', 2.25, [(0.85, 1.63, 0.78)], [(0.85, 580, 780)], zoom=1.15, kb=(1.0, 1.05),
                  punch=[(1.50, 0.08)], shake=[(1.50, 10)], slowzoom=0.0))
    # Тишина после боя: пустая поляна, медленный наезд к тёмной опушке слева сверху.
    calm = Clip('boss-calm-empty', BOSS_CUT, [(0.80, 0.80 + BOSS_CONTENT - BOSS_CUT, BOSS_CONTENT - BOSS_CUT)],
                [(0.8, 540, 960), (7.4, 500, 900)], zoom=1.03, kb=(1.0, 1.1), slowzoom=0.0, grade=False,
                wobble=tremble)
    return C, calm


def tremble(T):
    """Дрожь камеры: каждый «шаг» — затухающая дрожь, сильнее к концу (пиксели кадра)."""
    dx = dy = 0.0
    for t0, k in THUMPS:
        if T >= t0:
            e = math.exp(-(T - t0) / 0.32) * k
            dx += 6.0 * e * math.sin((T - t0) * 47 + t0)
            dy += 7.0 * e * math.sin((T - t0) * 61 + 1.3 * t0)
    return dx, dy


def _dust():
    rng = np.random.default_rng(7)
    small = rng.random((48, 27)).astype(np.float32)
    im = Image.fromarray((small * 255).astype(np.uint8)).resize((W // 2, H // 2), Image.BICUBIC)
    im = im.filter(ImageFilter.GaussianBlur(18))
    a = np.asarray(im).astype(np.float32) / 255
    a = np.clip((a - 0.45) * 3.0, 0, 1)
    return np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((W, H + 200), Image.BICUBIC)).astype(np.float32) / 255


DUST = None


def boss_base_factory(C, calm):
    global DUST
    if DUST is None:
        DUST = _dust()
    grad = np.clip((np.arange(H, dtype=np.float32) - 1080) / 840, 0, 1)[:, None, None] ** 1.3

    def base(T):
        if T < BOSS_CUT:
            for c in reversed(C):
                if T >= c.at - 1e-6:
                    return c.render(T)
        env = sum(math.exp(-(T - t0) / 0.7) * k * min(1.0, (T - t0) / 0.15) for t0, k in THUMPS if T >= t0)
        arr = calm.render(T)
        # Пыль поднимается от шага: мягкая дымка, едва заметная, дрейфует вверх.
        if env > 0.01:
            off = int(200 - min(200, (T - BOSS_CUT) * 24))
            d = DUST[off:off + H, :, None] * min(0.13, 0.11 * env)
            arr = arr + (np.array([196, 182, 150], np.float32) - arr) * d
        # Лёгкая виньетка и нижний градиент под подпись.
        v = min(1.0, (T - BOSS_CUT) / 1.5)
        arr = arr * (1 - 0.30 * v * VIGN) * (1 - 0.30 * grad)
        return arr
    return base


def boss_overlay(lang):
    l1, p1 = text_img(BOSS_LINE1[lang], 'phil', 66, stroke=0)
    l2, p2 = text_img(BOSS_LINE2[lang], 'philr', 44, stroke=0)
    top1 = 1238
    top2 = top1 + (l1.height - 2 * p1) + 22

    def fn(c, T):
        if T >= 6.45:
            put(c, l1, p1, SAFE_CX, top1, min(1.0, (T - 6.45) / 0.5) ** 1.2)
        if T >= 7.25:
            put(c, l2, p2, SAFE_CX, top2, 0.88 * min(1.0, (T - 7.25) / 0.5) ** 1.2)
    return fn


def boss_audio(C, total, fade_to=None):
    ev = []
    for c in C:
        # Склейка в тишину — жёсткая: хвосты боя обрезаются на склейке.
        ev += clip_sfx(c, gain=2.2, pre=0.35, until=BOSS_CUT)
    A = AUD
    # Спокойный кадр: сначала почти тишина, затем лес игры; птицы срываются; шаги-гул.
    ev.append(sfx(BOSS_CUT + 1.15, A + '/Camp/Prepared/camp_birds_takeoff.ogg', 0.55))
    # Замедление втрое (как плёнка), потом только низ: тяжёлый шаг, не трейлерный «брам».
    slow3 = 'aresample=48000,asetrate=16320,aresample=48000,lowpass=f=380,lowpass=f=380'
    for i, (t0, k) in enumerate(THUMPS):
        ev.append(sfx(t0 - 0.03, A + '/Combat/Mobs/Stonehoof/collision_0%d.wav' % (1 + i % 3), 1.25 * k, af=slow3))
        ev.append(sfx(t0, A + '/Combat/Mobs/Wendigo/land_0%d.wav' % (1 + i % 3), 1.1 * k, af=slow3))
        ev.append(sfx(t0 + 0.05, A + '/Camp/Prepared/camp_leaves_0%d.ogg' % (1 + i % 3), 0.35 * k))
    ev.append(sfx(6.1, A + '/Camp/Prepared/camp_wind_gust_02.ogg', 0.45))
    mix = mix_events(total, ev)
    amb = dec(A + '/Camp/Prepared/camp_evening_forest.ogg', None, 12.0, total)
    n = min(len(mix), len(amb))
    t = np.arange(n) / SR
    g = np.clip((t - BOSS_CUT - 0.25) / 0.8, 0, 1) * 0.42
    if fade_to:
        g *= np.clip(1 - (t - fade_to[0]) / (fade_to[1] - fade_to[0]), 0, 1)
    mix[:n] += amb[:n] * g[:, None]
    return mix


def build_boss(frames_only=False):
    C, calm = boss_clips()
    base = boss_base_factory(C, calm)
    tmp = WORK + '/boss'
    os.makedirs(tmp, exist_ok=True)
    variants = {'ru': (tmp + '/v-ru.mp4', boss_overlay('ru')), 'en': (tmp + '/v-en.mp4', boss_overlay('en')),
                'clean': (tmp + '/v-clean.mp4', None)}
    if frames_only:
        for T in (0.3, 1.1, 1.9, 2.8, 3.2, 5.1, 7.4, 8.4, 9.5):
            c = Image.fromarray(np.clip(base(T), 0, 255).astype(np.uint8)).convert('RGBA')
            variants['ru'][1](c, T)
            c.convert('RGB').resize((360, 640), Image.LANCZOS).save(tmp + '/still_%05.2f.jpg' % T, quality=85)
        return
    render(BOSS_CONTENT, base, variants)
    total = BOSS_CONTENT + 3.0 - BOSS_XF
    wav = finalize(boss_audio(C, total, fade_to=(9.3, 11.6)), tmp + '/a-end.wav', fout=0.8)
    wav_c = finalize(boss_audio(C, BOSS_CONTENT, fade_to=(9.0, 9.6)), tmp + '/a-clean.wav', fout=0.3)
    outs = []
    for lang in ('ru', 'en'):
        o = OUT + '/TWR-reel-boss-v2-%s.mp4' % lang
        with_endcard(variants[lang][0], wav, o, BOSS_CONTENT, BOSS_XF)
        outs.append(o)
    o = OUT + '/TWR-reel-boss-v2-clean.mp4'
    mux(variants['clean'][0], wav_c, o)
    outs.append(o)
    return outs


if __name__ == '__main__':
    what = sys.argv[1] if len(sys.argv) > 1 else 'all'
    frames = '--frames' in sys.argv
    outs = []
    if what in ('dodge', 'all'):
        outs += build_dodge(frames) or []
    if what in ('boss', 'all'):
        outs += build_boss(frames) or []
    if '--no-sheet' not in sys.argv:
        for o in outs:
            if o.endswith(('-ru.mp4', '-en.mp4', '-clean.mp4')):
                print('sheet', sheet(o))
    for o in outs:
        print('out', o)
