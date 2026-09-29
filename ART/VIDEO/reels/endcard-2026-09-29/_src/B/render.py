# End card B "gameplay -> logo slam" for Reels/TikTok, 1080x1920 @30.
# Logo = exact master PNG (only scale/alpha/additive light in its own fracture).
import sys, os, json, math, subprocess, argparse
import numpy as np
from PIL import Image, ImageFilter, ImageFont, ImageDraw
from scipy.ndimage import gaussian_filter, zoom as ndzoom
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'pytools'))
import imageio_ffmpeg
FF = imageio_ffmpeg.get_ffmpeg_exe()
HERE = os.path.dirname(os.path.abspath(__file__))

ap = argparse.ArgumentParser()
ap.add_argument('--out', default=os.path.join(HERE, 'out_B.mp4'))
ap.add_argument('--frames', default='')          # e.g. "0,40,75" -> only dump PNGs
ap.add_argument('--png', default='')             # dir for dumped pngs
ap.add_argument('--seq', default='')             # v4: dump every frame as lossless PNG into this dir (no encode)
args = ap.parse_args()

# ---------------------------------------------------------------- constants
FPS, DUR = 30, 5.0
N = int(round(FPS * DUR))
OW, OH = 1080, 1920
SRC = r"C:\Users\d.grab\Desktop\the-game\artifacts\mobsv2-check\wendigo-turn\forest_wendigo_turn_1080p60.mp4"
LOGO = r"C:\Users\d.grab\Desktop\the-game\ART\UI\logo-final\TWR-logo-painted-stone-master.png"
FONT = os.path.join(HERE, 'Philosopher-Bold.ttf')

# timeline (seconds, output time)
FREEZE_SRC = 3.4333          # frozen source moment: Wendigo wind-up over the full lava telegraph
T_SLOW0, T_SLOWD = 0.95, 0.45  # slow-down start, duration (speed 1 -> 0)
S0 = FREEZE_SRC - T_SLOW0 - T_SLOWD / 2
T_TREAT0, T_TREAT1 = 1.08, 1.55   # darken / blur / desat ramp
T_LOGO0 = 1.20               # logo starts to fall in
T_IMP = 1.20 + 4 / 30        # impact frame (1.333)
T_TEXT0, T_TEXTD = 2.20, 0.55
T_LINE0, T_LINED = 2.32, 0.55

LOGO_CX, LOGO_CY = 524, 800   # centre of logo artwork bbox in output
LOGO_W = 830                  # logo artwork bbox width in output
TEXT = "COMING SOON ON STEAM"
TEXT_PX = int(os.environ.get('B_TEXT_PX', '56'))       # v4: 52 -> 56 for phone readability
TRACK_FROM, TRACK_TO = 0.17, 0.13                        # v4: tracking settle (em), keeps the line inside x 90-940
BLUR_MAX = float(os.environ.get('B_BLUR', '22'))        # v4: 14 -> 22, the lava honeycomb becomes a soft ember pool
SCRIM = float(os.environ.get('B_SCRIM', '0.42'))        # v4: soft dark band behind the text line
GRAIN = 1.1 / 255.0                                      # v4: fine luma grain against banding in the dark blur
TEXT_CY = 1014                # optical centre of caps
LINE_Y = 1074
Q_Y1 = 960                    # where the lava telegraph centre sits in the frozen frame

def ss(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)
def sss(e0, e1, x):
    return float(ss(e0, e1, np.float64(x)))
def ease_out_cubic(u): u = min(max(u, 0.0), 1.0); return 1 - (1 - u) ** 3
def ease_out_quart(u): u = min(max(u, 0.0), 1.0); return 1 - (1 - u) ** 4
def ease_in_out(u): u = min(max(u, 0.0), 1.0); return u * u * (3 - 2 * u)

# ---------------------------------------------------------------- time mapping
def src_time(t):
    if t < T_SLOW0:
        return S0 + t
    u = min(t - T_SLOW0, T_SLOWD)
    return S0 + T_SLOW0 + (u - u * u / (2 * T_SLOWD))

# camera track: content displacement relative to FREEZE_SRC
cam = json.load(open(os.path.join(HERE, 'camtrack.json')))
cam_t = np.array(sorted(float(k) for k in cam))
cam_xy = np.array([cam[f"{k:.4f}"] for k in cam_t])
P_FREEZE = np.array([840.0, 580.0])  # lava telegraph centre in source px at FREEZE_SRC
def cam_at(ts):
    return np.array([np.interp(ts, cam_t, cam_xy[:, 0]), np.interp(ts, cam_t, cam_xy[:, 1])])
def world_point(ts):
    return P_FREEZE + cam_at(ts) - cam_at(FREEZE_SRC)

def shake(t):
    k = int(round((t - T_IMP) * FPS))
    return {0: (0, 13), 1: (-9, -7), 2: (5, 4)}.get(k, (0, 0))

def punch(t):  # bg zoom punch after impact
    if t < T_IMP: return 0.0
    return math.exp(-(t - T_IMP) / 0.12)

def crop_box(t):
    ts = src_time(t)
    P = world_point(ts)
    e = ease_in_out(t / 1.40)
    h = 1080 + (880 - 1080) * e
    h -= 22 * ss(1.4, DUR, t)          # very slow push during hold
    h *= (1 - 0.035 * punch(t))
    w = h * 9 / 16
    q = np.array([540 + (LOGO_CX - 540) * e, 958 + (Q_Y1 - 958) * e])  # where P lands in output
    sc = h / OH
    left = P[0] - q[0] * sc
    top = P[1] - q[1] * sc
    ox, oy = shake(t)
    left -= ox * sc; top -= oy * sc
    left = min(max(left, 0), 1920 - w); top = min(max(top, 0), 1080 - h)
    return (left, top, left + w, top + h)

# ---------------------------------------------------------------- source frames
need = sorted({int(round((src_time(i / FPS) - S0) * 60)) for i in range(N)})
print('source frames needed', len(need), need[0], need[-1])
frames = {}
nbytes = 1920 * 1080 * 3
p = subprocess.Popen([FF, '-v', 'error', '-ss', f'{S0:.4f}', '-i', SRC, '-frames:v', str(need[-1] + 1),
                      '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], stdout=subprocess.PIPE)
for i in range(need[-1] + 1):
    buf = p.stdout.read(nbytes)
    if len(buf) < nbytes: break
    if i in need:
        frames[i] = Image.frombuffer('RGB', (1920, 1080), buf, 'raw', 'RGB', 0, 1).copy()
p.wait()
print('decoded', len(frames))

# v4: remove the Wendigo HP bar ("5000 / 5000") from every source frame, not only the blurred freeze.
# Bar tracked per source frame (hpbar.py, NCC > 0.92); fill = same frame's ground at p + V, so the
# patch is world-locked and moves with the camera; per-channel gain matched on the seam ring.
_hb = json.load(open(os.path.join(HERE, 'hpbar.json')))
HB_V = (260, -90)
HB_TW, HB_TH = _hb['size']; HB_PX, HB_PY = _hb['pad']
def _hb_mask(feather=10):
    H, W = HB_TH + 2 * HB_PY, HB_TW + 2 * HB_PX
    m = np.zeros((H + 40, W + 40), np.float32)
    m[24:20 + H - 4, 24:20 + W - 4] = 1
    m = gaussian_filter(m, feather / 2.0)
    return np.clip((m - 0.02) / 0.6, 0, 1)
HB_M = _hb_mask()
def remove_hpbar(im, idx):
    x, y = _hb['pos'][str(idx)]
    f = np.asarray(im).astype(np.float32)
    X0, Y0 = x - HB_PX - 20, y - HB_PY - 20
    H, W = HB_M.shape
    dst = f[Y0:Y0 + H, X0:X0 + W]
    src = f[Y0 + HB_V[1]:Y0 + HB_V[1] + H, X0 + HB_V[0]:X0 + HB_V[0] + W]
    ring = (HB_M > 0.02) & (HB_M < 0.5)
    g = (dst[ring].mean(0) + 1) / (src[ring].mean(0) + 1)
    out = f.copy()
    out[Y0:Y0 + H, X0:X0 + W] = dst * (1 - HB_M[..., None]) + src * g * HB_M[..., None]
    return Image.fromarray(np.clip(out + 0.5, 0, 255).astype(np.uint8))
for _k in list(frames):
    frames[_k] = remove_hpbar(frames[_k], _k)
print('hp bar removed from', len(frames), 'source frames')

# ---------------------------------------------------------------- logo prep
master = np.asarray(Image.open(LOGO)).astype(np.float32) / 255.0
ma = master[..., 3]
ys, xs = np.where(ma > 8 / 255)
bx0, bx1, by0, by1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
S_FINAL = LOGO_W / (bx1 - bx0)
K = 1.25                               # working canvas supersample vs final
sK = S_FINAL * K
PAD = 90                               # master px padding for bloom
cx0, cx1, cy0, cy1 = bx0 - PAD, bx1 + PAD, by0 - PAD, by1 + PAD
_cm = os.path.join(HERE, 'crack_master.npy')
crack_m = (np.load(_cm) if os.path.exists(_cm) else np.load(os.path.join(HERE, 'crack_master.npz'))['m']).astype(np.float32)
path = np.load(os.path.join(HERE, 'crack_path.npy'))
crack_x0, crack_x1 = 233, 3308

def resize_f(arr, size, resample=Image.LANCZOS):
    return np.asarray(Image.fromarray(arr.astype(np.float32), 'F').resize(size, resample))

cw = int(round((cx1 - cx0) * sK)); ch = int(round((cy1 - cy0) * sK))
sub = master[cy0:cy1, cx0:cx1]
prem = np.dstack([sub[..., c] * sub[..., 3] for c in range(3)] + [sub[..., 3]])
LK = np.dstack([resize_f(prem[..., c], (cw, ch)) for c in range(4)])
LK = np.clip(LK, 0, 1)
LK[..., :3] = np.minimum(LK[..., :3], LK[..., 3:4])
CK = np.clip(resize_f(crack_m[cy0:cy1, cx0:cx1], (cw, ch)), 0, 1)
del master, sub, prem, crack_m
# x (in canvas px) of crack ends
kx0 = (crack_x0 - cx0) * sK; kx1 = (crack_x1 - cx0) * sK
# logo bbox centre inside canvas
lcx = ((bx0 + bx1) / 2 - cx0) * sK; lcy = ((by0 + by1) / 2 - cy0) * sK
colx = np.arange(cw, dtype=np.float32)[None, :]
print('logo canvas', cw, ch, 'final scale', S_FINAL)

def flare_field(t):
    """returns per-column multiplier for crack emission (0 = original art)."""
    if t < T_IMP - 1e-6: return None
    d = t - T_IMP
    kg = 1.0 * math.exp(-d / 0.22)                         # global flash of the crack
    # travelling hot spot left -> right
    u = d / 0.60
    ah = 0.9 * (1 - ss(0.75, 1.0, np.float64(u))) * ss(0.0, 0.08, np.float64(u)) if u < 1 else 0.0
    xh = kx0 + (kx1 - kx0) * ease_in_out(u) if u < 1 else kx1
    F = kg + ah * np.exp(-((colx - xh) / (150 * K)) ** 2)
    if F.max() < 0.01: return None
    return F

CORE_COL = np.array([1.0, 0.66, 0.32], np.float32)
BLOOM_COL = np.array([1.0, 0.50, 0.16], np.float32)

def logo_canvas(t):
    """premultiplied RGBA at K canvas incl. fracture flare (additive light has alpha 0)."""
    F = flare_field(t)
    if F is None:
        return LK
    cm = CK * F
    small = ndzoom(cm, 0.25, order=1)
    b1 = gaussian_filter(small, 8 * K / 4)
    b2 = gaussian_filter(small, 26 * K / 4)
    bloom = ndzoom(0.6 * b1 + 0.45 * b2, (ch / small.shape[0], cw / small.shape[1]), order=1)
    bloom = bloom[:ch, :cw]
    if bloom.shape != cm.shape:
        bb = np.zeros_like(cm); bb[:bloom.shape[0], :bloom.shape[1]] = bloom; bloom = bb
    out = LK.copy()
    out[..., :3] += cm[..., None] * CORE_COL * 0.85 * LK[..., 3:4] + bloom[..., None] * BLOOM_COL
    return out

def logo_scale_alpha(t):
    if t < T_LOGO0: return None, 0.0
    if t <= T_IMP:
        u = (t - T_LOGO0) / (T_IMP - T_LOGO0)
        s = 1.15 + (0.972 - 1.15) * u * u
        a = min(1.0, u * 1.6)
        return s, a
    d = t - T_IMP
    s = 1 - 0.028 * math.exp(-d / 0.10) * math.cos(2 * math.pi * d / 0.30)
    if d > 0.9: s = 1.0
    return s, 1.0

def place_logo(canvas, s):
    """resample canvas to scale s (final=1) -> (premult rgba array, x0, y0) in output coords (no shake)."""
    f = s / K
    w = max(1, int(round(cw * f))); h = max(1, int(round(ch * f)))
    arr = np.dstack([resize_f(canvas[..., c], (w, h)) for c in range(4)])
    x0 = LOGO_CX - lcx * (w / cw); y0 = LOGO_CY - lcy * (h / ch)
    return arr, int(round(x0)), int(round(y0))

def composite_premult(dst, src, x0, y0, opacity=1.0, add_only=False):
    H, W = src.shape[:2]
    X0, Y0 = max(x0, 0), max(y0, 0); X1, Y1 = min(x0 + W, OW), min(y0 + H, OH)
    if X1 <= X0 or Y1 <= Y0: return
    s = src[Y0 - y0:Y1 - y0, X0 - x0:X1 - x0]
    d = dst[Y0:Y1, X0:X1]
    d *= (1 - s[..., 3:4] * opacity)
    d += s[..., :3] * opacity

# static final logo (scale 1) + its shadow
LOGO1, LX1, LY1 = place_logo(LK, 1.0)

# ---------------------------------------------------------------- fx textures
tex_ring = np.asarray(Image.open(os.path.join(HERE, 'tex', 'cfxr_ring_ripple_dissolve.png')).convert('L')).astype(np.float32) / 255
ring_prof = tex_ring[16, :]          # 256 samples along the ring
smoke = np.asarray(Image.open(os.path.join(HERE, 'tex', 'cfxr_smoke_cloud_x4_blurred.png'))).astype(np.float32) / 255
puffs_src = [smoke[y:y + 256, x:x + 256] for y in (0, 256) for x in (0, 256)]
rng = np.random.default_rng(7)
NP = 16
PUFFS = []
for i in range(NP):
    th = i * 2 * math.pi / NP + rng.uniform(-0.12, 0.12)
    PUFFS.append(dict(th=th, var=int(rng.integers(0, 4)), rot=rng.uniform(0, 360), spin=rng.uniform(-25, 25),
                      sz=rng.uniform(0.85, 1.2), sp=rng.uniform(0.9, 1.1), dl=rng.uniform(0.0, 0.05)))

YY, XX = np.mgrid[0:OH, 0:OW].astype(np.float32)

def draw_ring(img, t, ox, oy):
    if t < T_IMP: return
    u = (t - T_IMP) / 0.42
    if u >= 1: return
    e = ease_out_cubic(u)
    rx = 430 + 620 * e; ry = rx * 0.44
    cx, cy = LOGO_CX + ox, LOGO_CY + 40 + oy
    x0, x1 = int(max(cx - rx - 60, 0)), int(min(cx + rx + 60, OW))
    y0, y1 = int(max(cy - ry - 60, 0)), int(min(cy + ry + 60, OH))
    X = XX[y0:y1, x0:x1] - cx; Y = YY[y0:y1, x0:x1] - cy
    d = np.sqrt((X / rx) ** 2 + (Y / ry) ** 2)
    w = (16 * (1 - u) + 4) / ry
    th = (np.arctan2(Y / ry, X / rx) / (2 * math.pi) * 3) % 1.0
    tv = np.interp(th * 255, np.arange(256), ring_prof)
    prof = np.exp(-((d - 1) / w) ** 2)
    I = RING_I * (1 - u) ** 1.6 * (0.35 + 0.65 * tv) * prof
    img[y0:y1, x0:x1] += I[..., None] * np.array([1.0, 0.80, 0.55], np.float32)

RING_I = 0.40
DUST_COL = np.array([0.40, 0.33, 0.26], np.float32)
DUST = []
for side in (-1, 1):
    for j in range(3, 7):
        DUST.append(dict(side=side, j=j, var=int(rng.integers(0, 4)), rot=rng.uniform(0, 360), spin=rng.uniform(-30, 30),
                         sz=rng.uniform(0.8, 1.15), sp=rng.uniform(0.85, 1.1), dl=rng.uniform(0.0, 0.04), dy=rng.uniform(-12, 12)))
DUST_OP = 0.22
def draw_dust(img, t, ox, oy):
    if t < T_IMP: return
    for pf in DUST:
        d = t - T_IMP - pf['dl']
        if d < 0: continue
        u = d / (0.75 * pf['sp'])
        if u >= 1: continue
        e = ease_out_quart(u)
        j, side = pf['j'], pf['side']
        px = LOGO_CX + ox + side * (60 + j * 46 + (40 + 16 * j) * e)
        py = LOGO_CY + 118 + oy + pf['dy'] - (10 + 5 * j) * e + 12 * (j - 2) * e
        size = int((120 + 150 * e) * pf['sz'])
        op = DUST_OP * min(1.0, d / 0.05) * (1 - u) ** 1.5
        src = puffs_src[pf['var']]
        im = Image.fromarray((src * 255).astype(np.uint8), 'RGBA').rotate(pf['rot'] + pf['spin'] * u, resample=Image.BILINEAR)
        im = im.resize((size, int(size * 0.8)), Image.BILINEAR).filter(ImageFilter.GaussianBlur(2 + 4 * e))
        a = np.asarray(im).astype(np.float32) / 255
        rgb = a[..., :3] * DUST_COL; al = a[..., 3:4] * op
        hh, ww = a.shape[:2]
        x0 = int(px - ww / 2); y0 = int(py - hh / 2)
        X0, Y0 = max(x0, 0), max(y0, 0); X1, Y1 = min(x0 + ww, OW), min(y0 + hh, OH)
        if X1 <= X0 or Y1 <= Y0: continue
        s_rgb = rgb[Y0 - y0:Y1 - y0, X0 - x0:X1 - x0]; s_a = al[Y0 - y0:Y1 - y0, X0 - x0:X1 - x0]
        dd = img[Y0:Y1, X0:X1]
        dd *= (1 - s_a); dd += s_rgb * s_a

# ---------------------------------------------------------------- text prep
SUPER = 2
font = ImageFont.truetype(FONT, TEXT_PX * SUPER)
cap_top, cap_bot = font.getbbox('H')[1], font.getbbox('H')[3]

def text_layer(track_em):
    """premult RGBA float, full output size canvas region; returns arr, x0, y0 (output coords)."""
    adv = [font.getlength(c) for c in TEXT]
    tr = track_em * TEXT_PX * SUPER
    width = sum(adv) + tr * (len(TEXT) - 1)
    Wc, Hc = int(width + 80 * SUPER), int((cap_bot - cap_top) + 80 * SUPER)
    m = Image.new('L', (Wc, Hc), 0); d = ImageDraw.Draw(m)
    x = 40 * SUPER; y = 40 * SUPER - cap_top
    for c, a in zip(TEXT, adv):
        d.text((x, y), c, font=font, fill=255)
        x += a + tr
    m = m.resize((Wc // SUPER, Hc // SUPER), Image.LANCZOS)
    A = np.asarray(m).astype(np.float32) / 255
    h, w = A.shape
    # subtle vertical warm gradient on ivory
    g = np.linspace(0, 1, h)[:, None, None]
    top = np.array([0.965, 0.935, 0.875], np.float32); bot = np.array([0.905, 0.835, 0.715], np.float32)
    capy0 = 40 / 1; capy1 = 40 + (cap_bot - cap_top) / SUPER
    gg = np.clip((np.arange(h)[:, None, None] - capy0) / (capy1 - capy0), 0, 1)
    col = top * (1 - gg) + bot * gg
    rgb = col * A[..., None]
    x0 = LOGO_CX - w / 2; y0 = TEXT_CY - (40 + (cap_bot - cap_top) / SUPER / 2)
    return np.dstack([rgb, A]), x0, y0

def shadow_of(alpha, sigma, strength):
    return np.clip(gaussian_filter(alpha, sigma) * strength, 0, 1)

AMBER = np.array([0.93, 0.56, 0.20], np.float32)
def ornament_layer(t):
    """line + diamond, premult RGBA over a local canvas; returns arr, x0, y0."""
    u = (t - T_LINE0) / T_LINED
    if u <= 0: return None
    e = ease_out_cubic(u)
    Wc, Hc = 700, 60
    L = np.zeros((Hc, Wc), np.float32)
    cx, cy = Wc / 2, Hc / 2
    half = 250 * e
    xs = np.arange(Wc, dtype=np.float32) - cx
    # line: 2px, fades towards the ends, gap around the diamond
    along = np.clip(1 - np.abs(xs) / max(half, 1e-3), 0, 1) ** 0.8 * (np.abs(xs) <= half)
    gap = ss(14, 22, np.abs(xs))
    yv = np.arange(Hc, dtype=np.float32) - cy
    line = np.exp(-(yv[:, None] / 1.1) ** 2) * (along * gap)[None, :]
    # diamond (rotated square), radius 7 px
    dsz = 7.5 * ease_out_cubic(min(1.0, u * 1.8))
    Xg, Yg = np.meshgrid(xs, yv)
    dia = ss(dsz + 0.8, dsz - 0.8, np.abs(Xg) + np.abs(Yg)) if dsz > 0.5 else np.zeros_like(Xg)
    L = np.maximum(line, dia)
    glow = gaussian_filter(L, 5) * 0.9
    a = np.clip(L, 0, 1) * min(1.0, u * 2.2)
    rgb = a[..., None] * AMBER + (glow * min(1.0, u * 2.2))[..., None] * AMBER * 0.6
    return np.dstack([rgb, a]), int(LOGO_CX - cx), int(LINE_Y - cy)

# ---------------------------------------------------------------- per-frame
VIGN_C = (540, 900)
RR = np.sqrt(((XX - VIGN_C[0]) / 780) ** 2 + ((YY - VIGN_C[1]) / 1150) ** 2)
VIGN = ss(0.40, 1.15, RR)
LOGO_AREA = np.exp(-(((XX - LOGO_CX) / 620) ** 2 + ((YY - 900) / 330) ** 2))
TEXT_BAND = np.exp(-((YY - (TEXT_CY + 28)) / 95) ** 2 - ((XX - LOGO_CX) / 560) ** 4)   # v4 scrim behind text + line
del RR

HPBAR = (715, 298, 930, 348)     # enemy HP bar in the frozen source frame (x0,y0,x1,y1)
def patch_hpbar(im, ts, w):
    """hide the Wendigo HP bar under the frozen/blurred frame by cloning grass from just above it."""
    off = cam_at(ts) - cam_at(FREEZE_SRC)
    x0, y0, x1, y1 = [int(round(v)) for v in (HPBAR[0] + off[0], HPBAR[1] + off[1], HPBAR[2] + off[0], HPBAR[3] + off[1])]
    pad = 18
    X0, Y0, X1, Y1 = x0 - pad, y0 - pad, x1 + pad, y1 + pad
    dy = (Y1 - Y0) + 4
    if Y0 - dy < 0: return im
    a = np.asarray(im).astype(np.float32)
    src = a[Y0 - dy:Y1 - dy, X0:X1]
    hh, ww = Y1 - Y0, X1 - X0
    my = ss(0, pad, np.arange(hh)) * ss(0, pad, hh - 1 - np.arange(hh))
    mx = ss(0, pad, np.arange(ww)) * ss(0, pad, ww - 1 - np.arange(ww))
    m = (my[:, None] * mx[None, :] * w)[..., None]
    a[Y0:Y1, X0:X1] = a[Y0:Y1, X0:X1] * (1 - m) + src * m
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))

def bg_frame(t):
    ts = src_time(t)
    idx = int(round((ts - S0) * 60))
    im = frames[idx]
    p = sss(T_TREAT0, T_TREAT1, t)
    box = crop_box(t)
    im = im.resize((OW, OH), Image.LANCZOS, box=box)
    if p > 0:
        im = im.filter(ImageFilter.GaussianBlur(BLUR_MAX * p))
    a = np.asarray(im).astype(np.float32) / 255
    if p > 0:
        lum = a @ np.array([0.299, 0.587, 0.114], np.float32)
        a = a + (lum[..., None] - a) * (0.22 * p)
        # tone curve: crush mids (grass) harder than highlights so the lava reads as an ember glow
        a = np.power(np.clip(a, 0, 1), 1 + 0.5 * p) * (1 - 0.47 * p)
        a *= (1 + (np.array([0.92, 0.98, 1.06], np.float32) - 1) * p)
        a *= (1 - 0.62 * p * VIGN)[..., None]
        a *= (1 - 0.12 * p * LOGO_AREA)[..., None]
        a *= (1 - SCRIM * p * TEXT_BAND)[..., None]
    if t >= T_IMP:
        fl = 0.10 * math.exp(-(t - T_IMP) / 0.10)
        a += (fl * LOGO_AREA)[..., None] * np.array([1.0, 0.72, 0.42], np.float32)
    return a

def render(i):
    t = i / FPS
    ox, oy = shake(t)
    img = bg_frame(t)
    draw_dust(img, t, ox, oy)
    draw_ring(img, t, ox, oy)
    s, al = logo_scale_alpha(t)
    if s is not None and al > 0:
        # motion blur while falling in (180 deg shutter, 5 sub-samples)
        subs = [t - k * (0.5 / FPS) / 4 for k in range(5)] if t <= T_IMP + 1e-6 else [t]
        acc = None
        for ts_ in subs:
            s_, a_ = logo_scale_alpha(ts_)
            if s_ is None: s_, a_ = 1.15, 0.0
            if abs(s_ - 1.0) < 1e-4 and flare_field(ts_) is None:
                arr, x0, y0 = LOGO1, LX1, LY1
            else:
                arr, x0, y0 = place_logo(logo_canvas(ts_), s_)
            arr = arr * a_
            if acc is None:
                acc = np.zeros((OH, OW, 4), np.float32)
            X0, Y0 = max(x0 + ox, 0), max(y0 + oy, 0)
            X1, Y1 = min(x0 + ox + arr.shape[1], OW), min(y0 + oy + arr.shape[0], OH)
            acc[Y0:Y1, X0:X1] += arr[Y0 - y0 - oy:Y1 - y0 - oy, X0 - x0 - ox:X1 - x0 - ox]
        acc /= len(subs)
        # soft drop shadow for separation
        sh = shadow_of(acc[..., 3], 9, 0.55)
        sh = np.roll(sh, 10, axis=0)
        img *= (1 - sh)[..., None]
        img *= (1 - acc[..., 3:4])
        img += acc[..., :3]
    # text
    if t >= T_TEXT0:
        u = (t - T_TEXT0) / T_TEXTD
        e = ease_out_cubic(u)
        track = TRACK_FROM + (TRACK_TO - TRACK_FROM) * e
        arr, x0, y0 = text_layer(track)
        y0 += 11 * (1 - e)
        a_ = min(1.0, max(0.0, u * 1.25)) ** 1.2
        # sub-pixel vertical placement: resample by fractional shift
        fy = y0 - math.floor(y0); fx = x0 - math.floor(x0)
        if fy > 1e-3 or fx > 1e-3:
            from scipy.ndimage import shift as ndshift
            arr = np.dstack([ndshift(arr[..., c], (fy, fx), order=1, mode='constant') for c in range(4)])
        xi, yi = int(math.floor(x0)) + ox, int(math.floor(y0)) + oy
        sh = shadow_of(arr[..., 3], 4, 0.85)
        shl = np.zeros((arr.shape[0], arr.shape[1], 4), np.float32); shl[..., 3] = sh * a_
        composite_premult(img, shl, xi, yi + 3)
        composite_premult(img, arr, xi, yi, opacity=a_)
    orn = ornament_layer(t)
    if orn is not None:
        arr, x0, y0 = orn
        composite_premult(img, arr, x0 + ox, y0 + oy)
    # v4: fine film grain (luma + a little chroma) instead of the +-0.5 dither, so x264 keeps the dark blur smooth
    rg = np.random.default_rng(5000 + i)
    g = rg.standard_normal(img.shape[:2]).astype(np.float32) * GRAIN
    gc = rg.standard_normal(img.shape).astype(np.float32) * (0.45 / 255.0)
    img = np.clip(img, 0, 1) + g[..., None] + gc
    return np.clip(img * 255 + 0.5, 0, 255).astype(np.uint8)

if args.frames:
    os.makedirs(args.png, exist_ok=True)
    for tok in args.frames.split(','):
        i = int(tok)
        Image.fromarray(render(i)).save(os.path.join(args.png, f'f_{i:03d}.png'))
        print('frame', i)
    sys.exit(0)

if args.seq:
    os.makedirs(args.seq, exist_ok=True)
    for i in range(N):
        Image.fromarray(render(i)).save(os.path.join(args.seq, f'f{i:04d}.png'), compress_level=1)
        if i % 15 == 0: print('frame', i, flush=True)
    print('done seq', args.seq)
    sys.exit(0)

enc = subprocess.Popen([FF, '-v', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{OW}x{OH}', '-r', str(FPS),
                        '-i', '-', '-an', '-c:v', 'libx264', '-preset', 'slow', '-crf', '15', '-profile:v', 'high',
                        '-level', '4.2', '-pix_fmt', 'yuv420p',
                        '-vf', 'scale=out_color_matrix=bt709:out_range=tv',
                        '-color_primaries', 'bt709', '-color_trc', 'bt709', '-colorspace', 'bt709', '-color_range', 'tv',
                        '-movflags', '+faststart', args.out], stdin=subprocess.PIPE)
for i in range(N):
    enc.stdin.write(render(i).tobytes())
    if i % 15 == 0: print('frame', i, flush=True)
enc.stdin.close(); enc.wait()
print('done', args.out)
