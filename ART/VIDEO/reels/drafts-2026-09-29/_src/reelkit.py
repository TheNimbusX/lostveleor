"""Сборка вертикальных роликов из кадров съёмки: клипы, текст, аудио, склейка с концовкой."""
import functools, json, math, os, subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H, FPS = 1080, 1920, 30
REPO = r"C:/Users/d.grab/Desktop/the-game"
FF = REPO + "/artifacts/tools/python/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe"
CAP = REPO + "/artifacts/capture/reels-drafts"
SCRATCH = os.path.dirname(os.path.abspath(__file__))
FONTS = {
    'phil': REPO + "/razlom/Assets/Resources/UI/Fonts/Philosopher-Bold.ttf",
    'nunito': REPO + "/razlom/Assets/Resources/UI/Fonts/Nunito-Bold.ttf",
}
IVORY = (247, 239, 222)
INK = (20, 14, 10)
EMBER = (255, 138, 60)
# Безопасная зона TikTok/Reels: без нижних 25 % и правых 140 px, сверху ~200 px под интерфейс.
SAFE_L, SAFE_R, SAFE_T, SAFE_B = 60, W - 140, 210, int(H * 0.75)
SAFE_CX = (SAFE_L + SAFE_R) // 2

SOURCES = {'old0903': (SCRATCH + "/old0903", 30)}


def src_info(name):
    if name in SOURCES:
        return SOURCES[name]
    return (CAP + "/" + name + "/video_frames", 60)


@functools.lru_cache(maxsize=48)
def load(name, n):
    d, _ = src_info(name)
    files = count(name)
    n = max(1, min(files, n))
    return Image.open(os.path.join(d, 'frame_%04d.jpg' % n)).convert('RGB')


@functools.lru_cache(maxsize=None)
def count(name):
    d, _ = src_info(name)
    return len([f for f in os.listdir(d) if f.endswith('.jpg')])


def ease_out(x):
    x = max(0.0, min(1.0, x))
    return 1 - (1 - x) ** 3


# ---------------------------------------------------------------- текст
@functools.lru_cache(maxsize=None)
def text_block(text, font, size, color=IVORY, stroke=None, align='center', spacing=0.12, glow=None, pill=False):
    """RGBA-блок текста: светлая заливка, тёмная обводка и мягкая тень под ней."""
    if pill:
        return pill_block(text, font, size, color)
    f = ImageFont.truetype(FONTS[font], size)
    stroke = max(3, min(18, int(size * 0.075))) if stroke is None else stroke
    lines = text.split('\n')
    asc, desc = f.getmetrics()
    lh = int((asc + desc) * (1 + spacing))
    widths = [f.getlength(l) for l in lines]
    pad = stroke + 40 + (int(size * 0.18 * 3) if glow else 0)  # свечению нужен запас, иначе видны края блока
    bw = int(max(widths)) + 2 * pad
    bh = lh * len(lines) + 2 * pad
    shadow = Image.new('L', (bw, bh), 0)
    fill = Image.new('RGBA', (bw, bh), (0, 0, 0, 0))
    ds, df = ImageDraw.Draw(shadow), ImageDraw.Draw(fill)
    for i, (l, lw) in enumerate(zip(lines, widths)):
        x = pad + ((bw - 2 * pad - lw) / 2 if align == 'center' else 0)
        y = pad + i * lh
        ds.text((x, y + size * 0.06), l, font=f, fill=255, stroke_width=stroke + 4, stroke_fill=255)
        df.text((x, y), l, font=f, fill=color + (255,), stroke_width=stroke, stroke_fill=INK + (255,))
    shadow = shadow.filter(ImageFilter.GaussianBlur(max(6, size * 0.12)))
    out = Image.new('RGBA', (bw, bh), (0, 0, 0, 0))
    out.putalpha(shadow.point(lambda v: int(v * 0.62)))
    if glow:
        g = Image.new('RGBA', (bw, bh), glow + (0,))
        g.putalpha(fill.getchannel('A').filter(ImageFilter.GaussianBlur(size * 0.18)).point(lambda v: int(v * 0.9)))
        out = Image.alpha_composite(out, g)
    out = Image.alpha_composite(out, fill)
    return out, pad


def pill_block(text, font, size, color):
    """Плашка: тёмная скруглённая подложка с тонкой светлой кромкой и текст без обводки."""
    f = ImageFont.truetype(FONTS[font], size)
    asc, desc = f.getmetrics()
    tw = int(f.getlength(text))
    ph, pw = int(size * 0.55), int(size * 0.9)
    bw, bh = tw + 2 * pw, asc + desc + 2 * ph - int(size * 0.2)
    pad = 24
    img = Image.new('RGBA', (bw + 2 * pad, bh + 2 * pad), (0, 0, 0, 0))
    sh = Image.new('L', img.size, 0)
    ImageDraw.Draw(sh).rounded_rectangle((pad, pad + 6, pad + bw, pad + bh + 6), radius=bh // 2, fill=170)
    img.putalpha(sh.filter(ImageFilter.GaussianBlur(10)))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((pad, pad, pad + bw, pad + bh), radius=bh // 2, fill=INK + (235,),
                        outline=(214, 170, 112, 255), width=3)
    d.text((pad + pw, pad + ph - int(size * 0.12)), text, font=f, fill=color + (255,))
    return img, pad


def place_text(canvas, ov, t):
    """ov: text, font, size, at, until, x (центр или левый край), y (верх текста), align, pop, fin, fout."""
    at, until = ov['at'], ov['until']
    if t < at or t >= until:
        return
    fin, fout = ov.get('fin', 0.12), ov.get('fout', 0.12)
    a = min(1.0, (t - at) / fin if fin > 0 else 1.0, (until - t) / fout if fout > 0 else 1.0)
    size = ov['size']
    maxw = ov.get('maxw', SAFE_R - SAFE_L)
    while True:
        img, pad = text_block(ov['text'], ov.get('font', 'phil'), size, ov.get('color', IVORY),
                              None, ov.get('align', 'center'), ov.get('spacing', 0.12), ov.get('glow'),
                              ov.get('pill', False))
        if img.width - 2 * pad <= maxw or size < 30:
            break
        size -= 4  # не влезает в безопасную зону — уменьшаем кегль
    ov['size'] = size
    s = 1.0
    if ov.get('pop'):
        s = 1.0 + ov['pop'] * (1 - ease_out((t - at) / 0.22))
    if s != 1.0:
        img = img.resize((int(img.width * s), int(img.height * s)), Image.BICUBIC)
        pad = int(pad * s)
    if a < 1.0:
        alpha = img.getchannel('A').point(lambda v: int(v * a))
        img = img.copy(); img.putalpha(alpha)
    if ov.get('align', 'center') == 'center':
        x = int(ov.get('x', SAFE_CX) - img.width / 2)
    else:
        x = int(ov['x'] - pad)
    if 'cy' in ov:
        y = int(ov['cy'] - img.height / 2)
    else:
        y = int(ov['y'] - pad - (img.height - 2 * pad) * (s - 1) / (2 * s))
    canvas.alpha_composite(img, (x, y)) if canvas.mode == 'RGBA' else canvas.paste(img, (x, y), img)


# ---------------------------------------------------------------- клипы
def render_clip(canvas, c, t):
    r = t - c['at']
    name = c['src']
    _, sfps = src_info(name)
    n = int(round(c['from'] + r * c.get('speed', 1.0) * sfps))
    img = load(name, n)
    x0, y0, x1, y1 = c.get('crop', (0, 0, img.width, img.height))
    rx, ry, rw, rh = c.get('rect', (0, 0, W, H))
    # Кадр под соотношение сторон места: режем по центру (или по фокусу) лишнее.
    cw, ch = x1 - x0, y1 - y0
    target = rw / rh
    if cw / ch > target:
        nw = ch * target
        fx = c.get('fx', (x0 + x1) / 2)
        x0 = min(max(x0, fx - nw / 2), x1 - nw); x1 = x0 + nw
    else:
        nh = cw / target
        fy = c.get('fy', (y0 + y1) / 2)
        y0 = min(max(y0, fy - nh / 2), y1 - nh); y1 = y0 + nh
    z = 1.0
    if 'kb' in c:
        z0, z1 = c['kb']
        z *= z0 + (z1 - z0) * min(1.0, r / c['dur'])
    for p in c.get('punch', []):
        if r >= p:
            z *= 1 + 0.055 * math.exp(-(r - p) / 0.09)
    if z != 1.0:
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        hw, hh = (x1 - x0) / (2 * z), (y1 - y0) / (2 * z)
        x0, x1, y0, y1 = cx - hw, cx + hw, cy - hh, cy + hh
    frame = img.resize((rw, rh), Image.LANCZOS, box=(x0, y0, x1, y1))
    if 'blur' in c:
        rad = c['blur'](r)
        if rad > 0.3:
            frame = frame.filter(ImageFilter.GaussianBlur(rad))
    if 'dark' in c:
        k = c['dark'](r)
        if k < 0.999:
            arr = np.asarray(frame).astype(np.float32)
            # Темним с лёгким холодным сдвигом, чтобы тени не были грязно-зелёными.
            arr = arr * np.array([k * 0.92, k * 0.95, k * 1.05])
            frame = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    for p in c.get('flash', []):
        if 0 <= r - p < 0.12:
            a = 0.22 * (1 - (r - p) / 0.12)
            frame = Image.blend(frame, Image.new('RGB', frame.size, (255, 236, 210)), a)
    canvas.paste(frame, (rx, ry))


def render_rect(canvas, o, t):
    if t < o['at'] or t >= o['until']:
        return
    x, y, w, h = o['rect']
    layer = Image.new('RGBA', (w, h), o['color'] + (int(255 * o.get('alpha', 1.0)),))
    canvas.alpha_composite(layer, (x, y))


def render_video(spec, out_path):
    """spec: {'dur': сек, 'clips': [...], 'overlays': [...]} → беззвучный H.264 30 fps."""
    n = int(round(spec['dur'] * FPS))
    cmd = [FF, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '%dx%d' % (W, H),
           '-r', str(FPS), '-i', '-', '-vf', 'scale=out_color_matrix=bt709:out_range=tv,format=yuv420p',
           '-c:v', 'libx264', '-preset', 'medium', '-crf', '16', '-colorspace', 'bt709',
           '-color_primaries', 'bt709', '-color_trc', 'bt709', '-color_range', 'tv', out_path]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)
    for i in range(n):
        t = i / FPS
        canvas = Image.new('RGBA', (W, H), (0, 0, 0, 255))
        for c in spec['clips']:
            if c['at'] <= t < c['at'] + c['dur']:
                render_clip(canvas, c, t)
        for o in spec['overlays']:
            if o.get('kind') == 'rect':
                render_rect(canvas, o, t)
            elif o.get('kind') == 'fn':
                o['fn'](canvas, t)
            else:
                place_text(canvas, o, t)
        proc.stdin.write(canvas.convert('RGB').tobytes())
    proc.stdin.close()
    proc.wait()
    if proc.returncode != 0:
        raise RuntimeError('ffmpeg failed: ' + out_path)


# ---------------------------------------------------------------- аудио
SR = 48000


def decode(path, start=0.0, dur=None):
    cmd = [FF, '-loglevel', 'error', '-ss', '%.3f' % start, '-i', path]
    if dur:
        cmd += ['-t', '%.3f' % dur]
    cmd += ['-ac', '2', '-ar', str(SR), '-f', 'f32le', '-']
    raw = subprocess.run(cmd, capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).reshape(-1, 2).copy()


def build_audio(total, music, sfx, out_wav, music_gain=1.0, fade_out=1.2):
    """music: (путь, старт); sfx: [(t, путь, громкость)]. Громкость итога — loudnorm −14 LUFS."""
    mix = np.zeros((int(total * SR), 2), np.float32)
    if music:
        m = decode(music[0], music[1], total)[:len(mix)] * music_gain
        mix[:len(m)] += m
    for t, path, g in sfx:
        s = decode(path) * g
        i = int(t * SR)
        if i >= len(mix):
            continue
        e = min(len(mix), i + len(s))
        mix[i:e] += s[:e - i]
    # Короткий вход и мягкий уход в конце — петля не щёлкает.
    fi = int(0.02 * SR); mix[:fi] *= np.linspace(0, 1, fi)[:, None]
    fo = int(fade_out * SR); mix[-fo:] *= np.linspace(1, 0, fo)[:, None] ** 1.5
    tmp = out_wav + '.pre.wav'
    write_wav(tmp, mix)
    # loudnorm в два прохода, линейно.
    r = subprocess.run([FF, '-hide_banner', '-i', tmp, '-af', 'loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json',
                        '-f', 'null', '-'], capture_output=True, text=True)
    js = r.stderr[r.stderr.rfind('{'):r.stderr.rfind('}') + 1]
    m = json.loads(js)
    af = ('loudnorm=I=-14:TP=-1.5:LRA=11:measured_I=%s:measured_TP=%s:measured_LRA=%s:measured_thresh=%s:'
          'offset=%s:linear=true' % (m['input_i'], m['input_tp'], m['input_lra'], m['input_thresh'], m['target_offset'])
          + ',alimiter=limit=0.8:attack=2:release=40:level=false')
    subprocess.run([FF, '-y', '-loglevel', 'error', '-i', tmp, '-af', af, '-ar', str(SR), out_wav], check=True)
    os.remove(tmp)


def write_wav(path, data):
    import wave
    pcm = (np.clip(data, -1, 1) * 32767).astype('<i2')
    with wave.open(path, 'wb') as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())


# ---------------------------------------------------------------- склейка
def assemble(content, endcard, end_from, end_dur, wav, out_sound, out_silent, xfade=0.0, content_dur=0.0):
    """Контент + концовка (срез end_from..end_from+end_dur) → со звуком и без; xfade — наплыв в концовку."""
    fc = ('[1:v]trim=start=%.3f:duration=%.3f,setpts=PTS-STARTPTS,fps=30,format=yuv420p,settb=1/30[e];'
          '[0:v]fps=30,format=yuv420p,settb=1/30[c];' % (end_from, end_dur))
    if xfade > 0:
        fc += '[c][e]xfade=transition=fade:duration=%.3f:offset=%.3f[v]' % (xfade, content_dur - xfade)
    else:
        fc += '[c][e]concat=n=2:v=1:a=0[v]'
    base = [FF, '-y', '-loglevel', 'error', '-i', content, '-i', endcard]
    enc = ['-c:v', 'libx264', '-preset', 'slow', '-crf', '18', '-pix_fmt', 'yuv420p', '-r', '30',
           '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709', '-color_range', 'tv',
           '-movflags', '+faststart']
    subprocess.run(base + ['-i', wav, '-filter_complex', fc, '-map', '[v]', '-map', '2:a'] + enc +
                   ['-c:a', 'aac', '-b:a', '192k', '-shortest', out_sound], check=True)
    subprocess.run(base + ['-filter_complex', fc, '-map', '[v]'] + enc + ['-an', out_silent], check=True)


def contact_sheet(video, out, cols=6, every=0.5, w=240):
    """Контактный лист готового ролика: кадр каждые every с, подпись времени."""
    tmp = out + '.d'
    os.makedirs(tmp, exist_ok=True)
    for f in os.listdir(tmp):
        os.remove(os.path.join(tmp, f))
    subprocess.run([FF, '-loglevel', 'error', '-i', video, '-vf', 'fps=1/%g' % every, os.path.join(tmp, 'f_%03d.png')],
                   check=True)
    fs = sorted(os.listdir(tmp))
    ims = []
    for i, f in enumerate(fs):
        im = Image.open(os.path.join(tmp, f)).convert('RGB')
        im = im.resize((w, int(im.height * w / im.width)), Image.LANCZOS)
        ImageDraw.Draw(im).text((4, 4), '%.1fs' % (i * every), fill=(255, 255, 0))
        ims.append(im)
    rows = (len(ims) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * w, rows * ims[0].height), 'black')
    for i, im in enumerate(ims):
        sheet.paste(im, ((i % cols) * w, (i // cols) * ims[0].height))
    sheet.save(out, quality=88)
    for f in fs:
        os.remove(os.path.join(tmp, f))
    os.rmdir(tmp)
    return out
