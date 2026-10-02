"""Перекраска иконок форм Вихря (02.10): ChatGPT сделал все четыре в одной бирюзе.

Красим только насыщенную сине-бирюзовую воду; белая пена, серый клинок, бронза
и красная кисть остаются. Цвета — как у форм в игре:
  Буря     — «шторм»: стально-синий, темнее и приглушённее;
  Водоворот — «бездна»: сине-фиолетовое ядро → бирюзовый край (по радиусу);
  Волны    — «отмель»: светлая аква / мята.
python recolor_forms.py  (из этой папки)
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'chatgpt-results')
OUT = os.path.join(HERE, 'final')


def rgb_to_hsv(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a.max(-1); mn = a.min(-1); d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rr = (mx == r) & m; gg = (mx == g) & m & ~rr; bb = m & ~rr & ~gg
    h[rr] = ((g - b)[rr] / d[rr]) % 6
    h[gg] = (b - r)[gg] / d[gg] + 2
    h[bb] = (r - g)[bb] / d[bb] + 4
    h = h / 6.0
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return h, s, mx


def hsv_to_rgb(h, s, v):
    i = np.floor(h * 6) % 6; f = h * 6 - np.floor(h * 6)
    p = v * (1 - s); q = v * (1 - f * s); t = v * (1 - (1 - f) * s)
    out = np.zeros(h.shape + (3,))
    for k, (r, g, b) in enumerate([(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)]):
        sel = i == k
        out[..., 0][sel] = r[sel]; out[..., 1][sel] = g[sel]; out[..., 2][sel] = b[sel]
    return out


def water_weight(h, s, v):
    deg = h * 360
    hue_w = np.clip(1 - np.abs(deg - 195) / 45, 0, 1)          # 150–240° — вода
    sat_w = np.clip((s - 0.12) / 0.25, 0, 1)                   # белая пена не трогается
    return hue_w * sat_w * np.clip((v - 0.22) / 0.2, 0, 1)   # тёмный фон иконки не красим


def recolor(name, hue_fn, sat_mul, val_fn):
    img = np.asarray(Image.open(os.path.join(SRC, name + '.png')).convert('RGB')).astype(np.float64) / 255
    h, s, v = rgb_to_hsv(img)
    w = water_weight(h, s, v)
    H, W = h.shape
    yy, xx = np.mgrid[0:H, 0:W]
    r = np.sqrt(((xx - W / 2) / (W / 2)) ** 2 + ((yy - H / 2) / (H / 2)) ** 2)   # 0 центр → 1 край
    nh = hue_fn(r) / 360.0
    ns = np.clip(s * sat_mul, 0, 1)
    nv = np.clip(v * val_fn(r), 0, 1)
    new = hsv_to_rgb(nh, ns, nv)
    out = img * (1 - w[..., None]) + new * w[..., None]
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray((out * 255 + .5).astype(np.uint8)).save(os.path.join(OUT, name + '.png'))
    return os.path.join(OUT, name + '.png')


paths = [
    # 02.10, «надо сильнее развести по цветам»: шторм — серо-стальной, бездна — фиолет → тёмно-синий, отмель — морская зелень.
    recolor('whirlwind-storm', lambda r: np.full_like(r, 212.0), 0.22, lambda r: np.full_like(r, 0.86)),   # «более стальную»: почти серый металл
    recolor('whirlwind-maelstrom', lambda r: 288 - 22 * np.clip(r / 0.85, 0, 1), 1.0,   # «более фиолет»: фиолет до самого края
            lambda r: 0.55 + 0.40 * np.clip(r / 0.85, 0, 1)),
    recolor('whirlwind-foamwaves', lambda r: np.full_like(r, 150.0), 0.9, lambda r: np.full_like(r, 1.1)),
]
base = Image.open(os.path.join(SRC, 'whirlwind-base.png')).convert('RGB')
base.save(os.path.join(OUT, 'whirlwind-base.png'))
tiles = [base] + [Image.open(p) for p in paths]
sheet = Image.new('RGB', (4 * 420, 420), (20, 24, 34))
for i, t in enumerate(tiles):
    sheet.paste(t.resize((400, 400)), (i * 420 + 10, 10))
sheet.save(os.path.join(HERE, 'sheet-final.jpg'), quality=90)
print('ok', OUT)
