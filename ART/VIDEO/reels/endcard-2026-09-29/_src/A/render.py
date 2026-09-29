# End card A "Stone and embers" - 1080x1920, 30 fps. Pure numpy/scipy compositing.
# Logo pixels = premultiplied Lanczos downscale of the master PNG; only light is added on top.
import numpy as np, json, os, sys, math
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as ndi

HERE = os.path.dirname(os.path.abspath(__file__))
FW, FH, FPS = 1080, 1920, 30
DUR = 5.0
NF = int(round(DUR * FPS))
LOGO_C = (534.0, 812.0)          # frame position of the logo content centre (x, y)
FONT = os.path.join(HERE, 'Philosopher-Bold.ttf')
SEED = 7

# ---------------------------------------------------------------- helpers
def clamp01(x): return np.clip(x, 0.0, 1.0)
def c01(x): return min(max(x, 0.0), 1.0)
def eo3(x): x = c01(x); return 1 - (1 - x) ** 3
def eio(x): x = c01(x); return 0.5 - 0.5 * math.cos(math.pi * x)
def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - 2 * t)
def ss(e0, e1, x):
    t = c01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t)
def screen(dst, src):
    return 1.0 - (1.0 - dst) * (1.0 - np.clip(src, 0, 1))

A = {}

def load_assets():
    if A: return
    meta = json.load(open(os.path.join(HERE, 'logo_meta.json')))
    A['meta'] = meta
    for tag in ('s1', 'sbig'):
        z = np.load(os.path.join(HERE, f'logo_{tag}.npz'))
        A[tag] = {k: z[k] for k in z.files}
    L1 = A['s1']
    h1, w1 = L1['a'].shape
    sx1 = meta['s1']['scale_x']; sy1 = meta['s1']['scale_y']
    cm = meta['content_center_master']
    cc1 = np.array([cm[1] * sy1, cm[0] * sx1])                  # (row, col) in s1 local
    oy = int(round(LOGO_C[1] - cc1[0])); ox = int(round(LOGO_C[0] - cc1[1]))
    A['off1'] = (oy, ox)
    A['C'] = np.array([oy + cc1[0], ox + cc1[1]])                  # exact frame centre (row, col)
    A['cc1'] = cc1
    A['kbig'] = np.array([meta['sbig']['scale_y'] / sy1, meta['sbig']['scale_x'] / sx1])
    # crack polyline in frame coords (s = 1)
    cx = np.array(meta['crack_x']) * sx1 + ox
    cy = np.array(meta['crack_y']) * sy1 + oy
    A['crack'] = (cx, cy)
    # region of the frame that the logo + glow can touch
    A['reg'] = (max(oy - 200, 0), min(oy + h1 + 200, FH))
    build_background()
    build_noise()
    build_text()
    build_particles()

# ---------------------------------------------------------------- background
def build_background():
    yy, xx = np.mgrid[0:FH, 0:FW].astype(np.float32)
    # warm-dark radial pool behind the logo, near black at the edges
    d = np.sqrt(((xx - 540) / 760.0) ** 2 + ((yy - 900) / 1050.0) ** 2)
    pool = np.exp(-d * d * 2.2)
    outer = np.array([0.018, 0.014, 0.012]); inner = np.array([0.070, 0.050, 0.038])
    bg = outer + (inner - outer) * pool[..., None]
    # vignette
    vd = np.sqrt(((xx - 540) / 620.0) ** 2 + ((yy - 930) / 1100.0) ** 2)
    vig = 1.0 - 0.55 * sstep(0.55, 1.45, vd)
    A['bg'] = (bg * vig[..., None]).astype(np.float32)
    A['vig'] = vig.astype(np.float32)
    # glow pool around the crack for "the crack lights the air" (used with glow level)
    cx, cy = A['crack']
    mcy = np.interp(xx[0], cx, cy, left=cy[0], right=cy[-1])
    gd = np.sqrt(((xx - 540) / 520.0) ** 2 + ((yy - mcy[None, :]) / 260.0) ** 2)
    A['airglow'] = np.exp(-gd * gd * 1.6).astype(np.float32)

def fbm(shape, rng, octaves, base_sigma, persistence=0.55):
    out = np.zeros(shape, np.float32); amp = 1.0; tot = 0; sig = base_sigma
    for _ in range(octaves):
        n = ndi.gaussian_filter(rng.standard_normal(shape).astype(np.float32), sig, mode='wrap')
        n /= n.std() + 1e-6
        out += amp * n; tot += amp; amp *= persistence; sig /= 2.0
    out /= tot
    return (out - out.min()) / (out.max() - out.min())

def aniso_fbm(shape, rng, octaves, sy, sx, persistence=0.55):
    out = np.zeros(shape, np.float32); amp = 1.0; tot = 0
    for _ in range(octaves):
        n = ndi.gaussian_filter(rng.standard_normal(shape).astype(np.float32), (sy, sx), mode='wrap')
        n /= n.std() + 1e-6
        out += amp * n; tot += amp; amp *= persistence; sy /= 2.0; sx /= 2.0
    return out / tot

def warped(shape, rng, sy, sx, warp):
    base = aniso_fbm(shape, rng, 5, sy, sx)
    wy = aniso_fbm(shape, rng, 3, sy * 1.5, sx * 1.5); wx = aniso_fbm(shape, rng, 3, sy * 1.5, sx * 1.5)
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]].astype(np.float32)
    n = ndi.map_coordinates(base, [yy + wy * warp, xx + wx * warp * 1.6], order=1, mode='wrap')
    return (n - n.min()) / (n.max() - n.min())

def build_noise():
    rng = np.random.default_rng(SEED)
    # half-res noise with margin for drift; stretched sideways so the mist lies in soft banks
    A['n1'] = warped((1100, 700), rng, 34, 80, 26)
    A['n2'] = warped((1100, 700), rng, 24, 60, 20)
    A['n3'] = fbm((1100, 700), rng, 4, 16)   # dust puff texture

def sample_half(n, ox, oy):
    """bilinear crop (half res 960x540) at fractional offset, upscaled to full frame"""
    ix, iy = int(math.floor(ox)), int(math.floor(oy)); fx, fy = ox - ix, oy - iy
    c = n[iy:iy + 961, ix:ix + 541]
    c = (c[:-1, :-1] * (1 - fx) * (1 - fy) + c[:-1, 1:] * fx * (1 - fy) + c[1:, :-1] * (1 - fx) * fy + c[1:, 1:] * fx * fy)
    return np.array(Image.fromarray(c.astype(np.float32), 'F').resize((FW, FH), Image.BICUBIC))

# ---------------------------------------------------------------- text
TEXT_COL = np.array([0.965, 0.915, 0.805], np.float32)
AMBER = np.array([0.95, 0.60, 0.26], np.float32)
SS = 4

def text_layer(txt, size, tracking_em):
    font = ImageFont.truetype(FONT, size * SS)
    adv = []
    for ch in txt:
        adv.append(font.getlength(ch))
    track = tracking_em * size * SS
    # kerning-free advance + tracking (spaces keep their advance)
    widths = sum(adv) + track * (len(txt) - 1)
    asc, desc = font.getmetrics()
    Wc = int(widths + 8 * SS); Hc = int(asc + desc + 8 * SS)
    im = Image.new('L', (Wc, Hc), 0); dr = ImageDraw.Draw(im)
    x = 4 * SS
    for ch, a in zip(txt, adv):
        dr.text((x, 4 * SS), ch, font=font, fill=255)
        x += a + track
    bbox = im.getbbox()
    # crop to ink with margin aligned to SS
    x0 = (bbox[0] // SS) * SS - 2 * SS; x1 = ((bbox[2] + SS - 1) // SS) * SS + 2 * SS
    y0 = (bbox[1] // SS) * SS - 2 * SS; y1 = ((bbox[3] + SS - 1) // SS) * SS + 2 * SS
    im = im.crop((x0, y0, x1, y1))
    small = im.reduce(SS)
    return np.array(small).astype(np.float32) / 255.0

def build_text():
    t1 = text_layer('COMING SOON', 74, 0.13)
    t2 = text_layer('ON STEAM', 44, 0.34)
    A['t1'] = t1; A['t2'] = t2
    logo_bottom = LOGO_C[1] + 0.5 * 948 * A['meta']['s1']['scale_y']
    y1 = int(round(logo_bottom + 76))
    y2 = int(round(y1 + t1.shape[0] + 16))
    A['t1_pos'] = (y1, int(round(540 - t1.shape[1] / 2)))
    A['t2_pos'] = (y2, int(round(540 - t2.shape[1] / 2)))
    # ornament: thin amber lines + small diamonds flanking ON STEAM (drawn supersampled)
    A['orn_y'] = y2 + t2.shape[0] / 2.0
    A['orn_gap'] = t2.shape[1] / 2.0 + 20
    print('text', A['t1_pos'], t1.shape, A['t2_pos'], t2.shape, file=sys.stderr)

def ornament(progress):
    """returns alpha (FHxFW region rows) of the lines/diamonds; progress 0..1 grows the lines outward"""
    y = A['orn_y']; gap = A['orn_gap']
    L = 150.0 * progress
    h0 = int(y - 12); h1 = int(y + 12)
    Wr = FW * SS; Hr = (h1 - h0) * SS
    im = Image.new('L', (Wr, Hr), 0); dr = ImageDraw.Draw(im)
    yc = (y - h0) * SS
    th = 1.2 * SS
    dsz = 5.2 * SS * ss(0.0, 0.5, progress)
    for sgn in (-1, 1):
        xi = 540 + sgn * gap
        # diamond at inner end
        if dsz > 0.5:
            cxp = xi * SS
            dr.polygon([(cxp - dsz, yc), (cxp, yc - dsz), (cxp + dsz, yc), (cxp, yc + dsz)], fill=255)
        # line from diamond outward, faded by gradient afterwards
        if L > 1:
            xa = (xi + sgn * 9) * SS; xb = (xi + sgn * (9 + L)) * SS
            dr.rectangle([min(xa, xb), yc - th, max(xa, xb), yc + th], fill=255)
    a = np.array(im.reduce(SS)).astype(np.float32) / 255.0
    xs = np.arange(FW, dtype=np.float32)
    dist = np.abs(xs - 540) - gap - 9
    fade = np.where(dist > 0, np.clip(1 - dist / max(L, 1.0), 0, 1) ** 1.4, 1.0)
    a *= fade[None, :]
    return h0, a

# ---------------------------------------------------------------- particles
def build_particles():
    rng = np.random.default_rng(SEED + 1)
    em = []
    for i in range(58):
        z = rng.uniform(0.45, 1.25)
        em.append(dict(
            t0=rng.uniform(-5.5, 4.0), life=rng.uniform(3.8, 6.8),
            x=rng.uniform(-20, 1100), y=rng.uniform(1250, 2080),
            vy=-rng.uniform(70, 135) * z, vx=rng.uniform(4, 22) * z,
            wa=rng.uniform(6, 26) * z, wf=rng.uniform(0.2, 0.6), wp=rng.uniform(0, 6.28),
            f1=rng.uniform(1.5, 3.8), f2=rng.uniform(0.6, 1.4), fp=rng.uniform(0, 6.28),
            core=rng.uniform(1.1, 2.1) * z, glow=rng.uniform(6.0, 11.0) * z,
            br=rng.uniform(0.45, 1.0) * (0.45 + 0.55 * z), hue=rng.uniform(0, 1)))
    for i in range(5):
        em.append(dict(
            t0=rng.uniform(-3.0, 2.5), life=rng.uniform(4.0, 5.5),
            x=rng.uniform(40, 1040), y=rng.uniform(1500, 2000),
            vy=-rng.uniform(150, 210), vx=rng.uniform(10, 30),
            wa=rng.uniform(20, 40), wf=rng.uniform(0.25, 0.45), wp=rng.uniform(0, 6.28),
            f1=rng.uniform(1.0, 2.0), f2=rng.uniform(0.4, 0.9), fp=rng.uniform(0, 6.28),
            core=rng.uniform(2.4, 3.2), glow=rng.uniform(12.0, 16.0),
            br=rng.uniform(0.55, 0.75), hue=rng.uniform(0, 0.5)))
    A['embers'] = em
    # ash motes: tiny, dim, slow
    ash = []
    for i in range(22):
        ash.append(dict(x=rng.uniform(0, FW), y=rng.uniform(0, FH), vx=rng.uniform(-8, 8), vy=rng.uniform(-14, 6),
                        wa=rng.uniform(4, 14), wf=rng.uniform(0.1, 0.35), wp=rng.uniform(0, 6.28),
                        s=rng.uniform(0.6, 1.0), a=rng.uniform(0.10, 0.24)))
    A['ash'] = ash
    # sparks released from the crack as the light passes
    cx, cy = A['crack']
    sp = []
    for i in range(9):
        te = 0.98 + (1.05 * (i + rng.uniform(0.1, 0.9)) / 9.0)
        u = crack_front(te)
        x = np.interp(u, np.linspace(0, 1, len(cx)), cx); y = np.interp(u, np.linspace(0, 1, len(cy)), cy)
        sp.append(dict(t0=te, x=x, y=y + rng.uniform(-3, 3), vx=rng.uniform(-25, 45), vy=-rng.uniform(55, 150),
                       life=rng.uniform(0.7, 1.35), core=rng.uniform(0.8, 1.25), glow=rng.uniform(3.0, 5.0),
                       br=rng.uniform(0.7, 1.0)))
    A['sparks'] = sp
    # foreground bokeh: few, soft, outside the title band
    bk = []
    for (x, y, rad, a, vy) in [(80, 1560, 40, 0.075, -30), (990, 1760, 34, 0.065, -36), (1000, 380, 46, 0.045, -16)]:
        bk.append(dict(x=x, y=y, r=rad, a=a, vy=vy, ph=rng.uniform(0, 6.28)))
    A['bokeh'] = bk

def ember_color(age01, hue):
    # hot yellow-orange when young, deep red when cooling
    young = np.array([1.0, 0.72 + 0.12 * hue, 0.34 + 0.12 * hue]); old = np.array([0.95, 0.30, 0.10])
    k = ss(0.35, 1.0, age01)
    return young * (1 - k) + old * k

def splat(buf, x, y, col, amp, sig, vx=0.0, vy=0.0, y0=0):
    """additive gaussian splat with light motion blur along velocity (4 taps over half a frame)"""
    r = int(math.ceil(sig * 3.2 + abs(vx) / FPS * 0.5 + abs(vy) / FPS * 0.5)) + 1
    xi, yi = int(round(x)), int(round(y - y0))
    H, W = buf.shape[:2]
    xa, xb = max(xi - r, 0), min(xi + r + 1, W); ya, yb = max(yi - r, 0), min(yi + r + 1, H)
    if xa >= xb or ya >= yb: return
    gy, gx = np.mgrid[ya:yb, xa:xb].astype(np.float32)
    acc = np.zeros_like(gx)
    for k in range(4):
        o = (k / 3.0 - 0.5) * 0.5 / FPS
        px, py = x + vx * o, (y - y0) + vy * o
        acc += np.exp(-((gx - px) ** 2 + (gy - py) ** 2) / (2 * sig * sig))
    acc *= amp / 4.0
    buf[ya:yb, xa:xb] += acc[..., None] * col[None, None, :]

def crack_front(t):
    return -0.07 + 1.16 * eio((t - 0.85) / 1.35)

# ---------------------------------------------------------------- per-frame
def logo_local(L, t, f):
    """returns premult rgb, alpha and emission field (local space of layer L)"""
    rgb = L['rgb']; a = L['a']; core = L['core']; cool = L['cool']; u = L['u']
    ign = sstep(-0.035, 0.03, f - u)                     # 1 behind the front
    w_cool = np.clip(cool / np.maximum(a, 1e-3), 0, 1) * (1 - ign) * 0.82
    lum = rgb[..., 0] * 0.3 + rgb[..., 1] * 0.59 + rgb[..., 2] * 0.11
    cool_col = lum[..., None] * 0.40 * np.array([1.0, 0.70, 0.56], np.float32)
    rgb2 = rgb * (1 - w_cool[..., None]) + cool_col * w_cool[..., None]
    # travelling head (sharp leading edge, softer tail) + afterglow behind it
    d = u - f
    head = np.where(d > 0, np.exp(-(d / 0.016) ** 2), np.exp(d / 0.06))
    run = ss(0.8, 0.95, t) * (1 - ss(2.15, 2.35, t))
    after_fade = 1 - ss(2.1, 3.1, t)
    after = ign * np.exp(-np.maximum(f - u, 0) / 0.28) * 0.34 * after_fade
    e_head = core * head * run
    e_after = core * after
    hot_col = np.array([1.0, 0.86, 0.62], np.float32); warm_col = np.array([1.0, 0.58, 0.24], np.float32)
    add = e_head[..., None] * hot_col * 1.6 + e_after[..., None] * warm_col
    rgb3 = screen(rgb2, add)
    rgb3 = np.minimum(rgb3, np.maximum(a[..., None], add.max(-1, keepdims=True)))
    # emission field for bloom (includes the resting crack glow once ignited)
    rest = glow_level(t)
    field = e_head * 1.2 + e_after + core * ign * rest
    return rgb3, a, field

def glow_level(t):
    # resting crack glow used for bloom: swell after the light reaches the end, then a calm breathing hold
    swell = 0.36 * math.exp(-((t - 2.30) / 0.42) ** 2)
    base = 0.16 * ss(1.0, 2.2, t)
    breathe = 0.025 * math.sin(2 * math.pi * (t - 2.2) / 2.4) * ss(2.6, 3.2, t)
    return base + swell + breathe

def place_logo(t, f):
    """composite logo layer into the logo region; returns (rgb_premult, alpha, field) for rows reg"""
    r0, r1 = A['reg']; R = r1 - r0
    s = 1.0 + (A['meta']['scale_start'] - 1.0) * (1 - eo3((t - 0.05) / 1.6))
    xf = ss(1.35, 1.65, t)   # crossfade transformed -> exact
    out_rgb = np.zeros((R, FW, 3), np.float32); out_a = np.zeros((R, FW), np.float32); out_f = np.zeros((R, FW), np.float32)
    if xf < 1.0:
        rgb, a, fld = logo_local(A['sbig'], t, f)
        k = A['kbig']; C = A['C']; cc1 = A['cc1']
        mat = k / s
        off = cc1 * k - (C - np.array([r0, 0])) * (k / s)
        def tf(ch):
            return ndi.affine_transform(ch, mat, off, output_shape=(R, FW), order=3, mode='constant', cval=0.0, prefilter=True)
        tr = np.stack([tf(rgb[..., i]) for i in range(3)], -1)
        ta = tf(a); tfl = tf(fld)
        w = 1 - xf
        out_rgb += tr * w; out_a += ta * w; out_f += tfl * w
    if xf > 0.0:
        rgb, a, fld = logo_local(A['s1'], t, f)
        oy, ox = A['off1']; h, w_ = a.shape
        ya = oy - r0
        out_rgb[ya:ya + h, ox:ox + w_] += rgb * xf
        out_a[ya:ya + h, ox:ox + w_] += a * xf
        out_f[ya:ya + h, ox:ox + w_] += fld * xf
    out_a = np.clip(out_a, 0, 1); out_rgb = np.clip(out_rgb, 0, 1); out_f = np.clip(out_f, 0, None)
    return out_rgb, out_a, out_f

def bloom(field):
    small = np.array(Image.fromarray(field.astype(np.float32), 'F').resize((field.shape[1] // 2, field.shape[0] // 2), Image.BILINEAR))
    b = (ndi.gaussian_filter(small, 3.0) * 0.55 + ndi.gaussian_filter(small, 11.0) * 0.55 + ndi.gaussian_filter(small, 30.0) * 0.45)
    return np.array(Image.fromarray(b, 'F').resize((field.shape[1], field.shape[0]), Image.BICUBIC))

def render(fi):
    load_assets()
    t = fi / FPS
    f = crack_front(t)
    fade_all = eo3(t / 0.45)
    # --- background + mist
    img = A['bg'].copy()
    m1 = sample_half(A['n1'], 60 + 8.0 * t, 60 + 3.0 * t)
    m2 = sample_half(A['n2'], 90 - 6.5 * t, 70 + 7.0 * t)
    yy = np.linspace(0, 1, FH, dtype=np.float32)[:, None]
    prof = 0.30 + 0.70 * sstep(0.50, 0.97, yy) + 0.45 * np.exp(-((yy - 0.43) / 0.09) ** 2)
    dens = (sstep(0.48, 0.86, m1) * 0.65 + sstep(0.52, 0.90, m2) * 0.55) * prof * A['vig']
    mist_col = np.array([0.30, 0.245, 0.205], np.float32)
    ma = np.clip(dens * 0.40, 0, 0.55)[..., None]
    img = img * (1 - ma) + mist_col * ma
    gl = glow_level(t) + 0.35 * ss(0.85, 1.1, t) * (1 - ss(2.0, 2.4, t))
    air = A['airglow'] * (0.035 + 0.30 * dens) * gl
    img = screen(img, air[..., None] * np.array([1.0, 0.50, 0.22], np.float32))
    # dust puff behind the logo while it emerges
    pk = math.exp(-((t - 0.55) / 0.38) ** 2) * 0.30
    if pk > 0.004:
        rad = 260 + 420 * eo3((t - 0.05) / 1.6)
        yyf, xxf = np.mgrid[0:FH, 0:FW].astype(np.float32)
        d = np.sqrt(((xxf - LOGO_C[0]) / (rad * 1.35)) ** 2 + ((yyf - LOGO_C[1]) / (rad * 0.62)) ** 2)
        tex = sample_half(A['n3'], 80 + 30 * t, 80 - 12 * t)
        pa = np.clip((1 - sstep(0.35, 1.0, d)) * sstep(0.35, 0.8, tex) * pk, 0, 1)[..., None]
        img = img * (1 - pa) + np.array([0.33, 0.27, 0.22], np.float32) * pa
    # --- background embers + ash
    eb = np.zeros((FH, FW, 3), np.float32)
    for e in A['embers']:
        age = t - e['t0']
        if age < 0 or age > e['life']: continue
        a01 = age / e['life']
        x = e['x'] + e['vx'] * age + e['wa'] * math.sin(2 * math.pi * e['wf'] * age + e['wp'])
        y = e['y'] + e['vy'] * age
        if y < -30 or y > FH + 30 or x < -30 or x > FW + 30: continue
        vx = e['vx'] + e['wa'] * 2 * math.pi * e['wf'] * math.cos(2 * math.pi * e['wf'] * age + e['wp'])
        env = ss(0.0, 0.12, a01) * (1 - ss(0.62, 1.0, a01))
        flick = 0.72 + 0.28 * math.sin(2 * math.pi * e['f1'] * t + e['fp']) * math.sin(2 * math.pi * e['f2'] * t + 1.3 * e['fp'])
        amp = e['br'] * env * flick
        col = ember_color(a01, e['hue'])
        splat(eb, x, y, col, amp * 1.05, e['core'], vx, e['vy'])
        splat(eb, x, y, col * np.array([1.0, 0.75, 0.6]), amp * 0.13, e['glow'])
    for s_ in A['ash']:
        x = (s_['x'] + s_['vx'] * t + s_['wa'] * math.sin(2 * math.pi * s_['wf'] * t + s_['wp'])) % FW
        y = (s_['y'] + s_['vy'] * t) % FH
        splat(eb, x, y, np.array([0.55, 0.49, 0.43], np.float32), s_['a'], s_['s'])
    img = screen(img, eb) * fade_all + 0.0
    # --- logo
    r0, r1 = A['reg']
    lalpha = ss(0.08, 0.80, t)
    lbright = 0.25 + 0.75 * eo3((t - 0.12) / 0.9)
    lrgb, la, fld = place_logo(t, f)
    reg = img[r0:r1]
    reg = lrgb * (lalpha * lbright) + reg * (1 - la[..., None] * lalpha)
    # bloom from the crack
    bl = bloom(fld) * lalpha
    reg = screen(reg, bl[..., None] * np.array([1.0, 0.56, 0.24], np.float32) * 0.9)
    # travelling light: soft halo riding the head along the crack
    sb = np.zeros_like(reg)
    hk = ss(0.8, 0.95, t) * (1 - ss(2.05, 2.3, t))
    if hk > 0 and -0.02 < f < 1.02:
        cx, cy = A['crack']; uu = np.linspace(0, 1, len(cx))
        hx = float(np.interp(f, uu, cx)); hy = float(np.interp(f, uu, cy))
        splat(sb, hx, hy, np.array([1.0, 0.80, 0.52], np.float32), 0.55 * hk, 7.0, y0=r0)
        splat(sb, hx, hy, np.array([1.0, 0.55, 0.22], np.float32), 0.16 * hk, 26.0, y0=r0)
    # sparks out of the crack
    for s_ in A['sparks']:
        age = t - s_['t0']
        if age < 0 or age > s_['life']: continue
        a01 = age / s_['life']
        drag = math.exp(-1.6 * age)
        x = s_['x'] + s_['vx'] * (1 - drag) / 1.6
        y = s_['y'] + s_['vy'] * (1 - drag) / 1.6 - 18 * age * age
        env = ss(0, 0.08, a01) * (1 - ss(0.4, 1.0, a01))
        col = ember_color(a01 * 0.8, 0.8)
        splat(sb, x, y, col, s_['br'] * env * 1.2, s_['core'], s_['vx'] * drag, s_['vy'] * drag, y0=r0)
        splat(sb, x, y, col, s_['br'] * env * 0.22, s_['glow'], y0=r0)
    reg = screen(reg, sb)
    img[r0:r1] = reg
    # --- text
    for key, t0, dur in (('t1', 2.0, 0.75), ('t2', 2.22, 0.75)):
        p = eo3((t - t0) / dur)
        if p <= 0: continue
        tl = A[key]; y0, x0 = A[key + '_pos']
        dy = 11.0 * (1 - p)
        yf = y0 + dy; yi = int(math.floor(yf)); fr = yf - yi
        h, w = tl.shape
        lay = np.zeros((h + 1, w), np.float32)
        lay[:h] += tl * (1 - fr); lay[1:h + 1] += tl * fr
        al = lay * p
        # soft shadow for legibility over mist/embers
        sh = ndi.gaussian_filter(np.pad(al, 14), 5.0)
        sy, sx = yi - 14 + 3, x0 - 14
        img[sy:sy + sh.shape[0], sx:sx + sh.shape[1]] *= (1 - 0.62 * sh)[..., None]
        seg = img[yi:yi + h + 1, x0:x0 + w]
        img[yi:yi + h + 1, x0:x0 + w] = seg * (1 - al[..., None]) + TEXT_COL * al[..., None]
    op = eo3((t - 2.35) / 0.85)
    if op > 0:
        h0, oa = ornament(op)
        oa = oa * ss(0, 0.25, op)
        seg = img[h0:h0 + oa.shape[0]]
        img[h0:h0 + oa.shape[0]] = seg * (1 - oa[..., None]) + AMBER * oa[..., None]
        # tiny glow on the ornament
        img[h0 - 20:h0 + oa.shape[0] + 20] = screen(img[h0 - 20:h0 + oa.shape[0] + 20],
            ndi.gaussian_filter(np.pad(oa, ((20, 20), (0, 0))), 4.0)[..., None] * AMBER * 0.35)
    # --- foreground bokeh
    fb = np.zeros((FH, FW, 3), np.float32)
    for b in A['bokeh']:
        x = b['x'] + 6 * math.sin(0.5 * t + b['ph']); y = b['y'] + b['vy'] * t
        r = b['r']; ri = int(r + 4)
        xa, xb = max(int(x) - ri, 0), min(int(x) + ri + 1, FW); ya, yb = max(int(y) - ri, 0), min(int(y) + ri + 1, FH)
        if xa >= xb or ya >= yb: continue
        gy, gx = np.mgrid[ya:yb, xa:xb].astype(np.float32)
        d = np.sqrt((gx - x) ** 2 + (gy - y) ** 2) / r
        disc = (1 - sstep(0.55, 1.0, d)) * (0.8 + 0.2 * sstep(0.3, 0.8, d))
        fb[ya:yb, xa:xb] += disc[..., None] * np.array([1.0, 0.55, 0.22], np.float32) * b['a'] * (0.8 + 0.2 * math.sin(1.7 * t + b['ph']))
    img = screen(img, fb * fade_all)
    # --- grain + quantize
    rng = np.random.default_rng(1000 + fi)
    g = rng.standard_normal((FH, FW)).astype(np.float32) * (1.25 / 255.0)
    gc = rng.standard_normal((FH, FW, 3)).astype(np.float32) * (0.45 / 255.0)
    img = img + g[..., None] + gc
    return (np.clip(img, 0, 1) * 255.0 + 0.5).astype(np.uint8)

def work(fi):
    out = os.path.join(HERE, 'frames', f'f{fi:04d}.png')
    Image.fromarray(render(fi)).save(out, compress_level=1)
    return fi

if __name__ == '__main__':
    os.makedirs(os.path.join(HERE, 'frames'), exist_ok=True)
    args = sys.argv[1:]
    if args and args[0] == 'test':
        load_assets()
        for tt in [float(x) for x in args[1:]]:
            fi = int(round(tt * FPS))
            Image.fromarray(render(fi)).save(os.path.join(HERE, f'test_{fi:04d}.png'))
            print('wrote', fi)
    else:
        from multiprocessing import Pool
        frames = list(range(NF))
        with Pool(10) as p:
            for fi in p.imap_unordered(work, frames):
                pass
        print('done', NF)
