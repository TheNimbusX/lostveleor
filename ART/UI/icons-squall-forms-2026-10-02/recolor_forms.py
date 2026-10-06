"""Перекраска иконок форм Шквала — по образцу ART/UI/icons-whirlwind-forms-2026-10-02/recolor_forms.py.

ChatGPT рисует все формы одной бирюзой (у Вихря так и вышло), поэтому формы разводятся по цветам здесь:
красится только насыщенная сине-бирюзовая вода; белая пена, сабля, бронза и тёмный фон остаются.

PALETTE — ПРЕДЛОЖЕНИЕ, владелец ещё не утверждал (02.10). Цвета должны совпасть с цветами эффектов форм
Шквала в игре (их выбирает сборка VFX) и не повторять формы Вихря на экране выбора формы: база Вихря и Шквала —
бирюза ~190°; Буря — сталь 212° (насыщ. ×0,22); Водоворот — фиолет 288→266°; Пенные волны — морская зелень 150°.
Без оранжевого и красного (цвета телеграфов врагов).

    python recolor_forms.py          → final/*.png и sheet-final.jpg
    python recolor_forms.py --keep   → final/ без перекраски (если владелец оставит бирюзу)
Вход: chatgpt-results/squall-hunt.png, squall-foamtrail.png, squall-elusive.png (+ squall-base.png, если делали).
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'chatgpt-results')
OUT = os.path.join(HERE, 'final')

# имя файла: (оттенок от радиуса 0 центр → 1 край, множитель насыщенности, множитель яркости от радиуса)
PALETTE = {
    # Охота — «кобальт/глубина»: насыщенный тёмно-синий, отличим от серой стали Бури.
    'squall-hunt': (lambda r: np.full_like(r, 226.0), 1.0, lambda r: np.full_like(r, 0.82)),
    # Пенный след — «лагуна»: зеленее базы, синее морской зелени Волн; пены больше (ярче).
    'squall-foamtrail': (lambda r: np.full_like(r, 172.0), 0.85, lambda r: np.full_like(r, 1.06)),
    # Неуловимый — «мираж»: бледная прозрачная лазурь, светлее и бледнее всего набора.
    'squall-elusive': (lambda r: np.full_like(r, 200.0), 0.40, lambda r: np.full_like(r, 1.15)),
}


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


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + '.png')
    Image.fromarray((np.clip(img, 0, 1) * 255 + .5).astype(np.uint8)).save(path)
    return path


def recolor(name, hue_fn, sat_mul, val_fn, keep):
    img = np.asarray(Image.open(os.path.join(SRC, name + '.png')).convert('RGB')).astype(np.float64) / 255
    if keep:
        return save(img, name)
    h, s, v = rgb_to_hsv(img)
    w = water_weight(h, s, v)
    H, W = h.shape
    yy, xx = np.mgrid[0:H, 0:W]
    r = np.sqrt(((xx - W / 2) / (W / 2)) ** 2 + ((yy - H / 2) / (H / 2)) ** 2)
    new = hsv_to_rgb(hue_fn(r) / 360.0, np.clip(s * sat_mul, 0, 1), np.clip(v * val_fn(r), 0, 1))
    return save(img * (1 - w[..., None]) + new * w[..., None], name)


def main():
    keep = '--keep' in sys.argv
    tiles = []
    os.makedirs(OUT, exist_ok=True)
    base = os.path.join(SRC, 'squall-base.png')
    if os.path.exists(base):
        # База не перекрашивается: бирюза, как база Вихря.
        image = Image.open(base).convert('RGB')
        image.save(os.path.join(OUT, 'squall-base.png'))
        tiles.append(image)
    for name, (hue_fn, sat_mul, val_fn) in PALETTE.items():
        if not os.path.exists(os.path.join(SRC, name + '.png')):
            print('нет', name)
            continue
        tiles.append(Image.open(recolor(name, hue_fn, sat_mul, val_fn, keep)))
    if not tiles:
        sys.exit('chatgpt-results пуст')
    sheet = Image.new('RGB', (len(tiles) * 420, 420), (20, 24, 34))
    for i, t in enumerate(tiles):
        sheet.paste(t.resize((400, 400)), (i * 420 + 10, 10))
    sheet.save(os.path.join(HERE, 'sheet-final.jpg'), quality=90)
    print('ok', OUT)


if __name__ == '__main__':
    main()
