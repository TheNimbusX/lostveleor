"""Знак элиты на полосе здоровья над врагом (владелец 29.09, G8: концепты новой полосы отвергнуты —
«даже то что щас лучше»; остаётся нынешний вид «Дыма и света», в полосу — цифры и маленький понятный
знак элиты в том же языке). Знак — рогатый череп сухой кистью, как белые знаки забега
(Assets/UI/RunIcons): рваный край, штрихи вдоль мазка.

  EliteBarMark.png    — сам знак: белая маска 128×128 (цвет — тёплый свет огонька, HealthBars.OrbColor);
  EliteBarMarkInk.png — чернильная подложка под знаком: тот же силуэт, раздутый и размытый
                        (цвет — дым дорожки, HealthBars.BackColor), чтобы знак читался и на красной
                        заливке, и на светлой траве.

Сияние вокруг знака — готовый EnemyBarGlow.png (make-enemy-bars.py). Все — белые маски, стороны —
степени двойки, края прозрачные (как у остальных EnemyBar*).

Запуск: python tools/ui-kit/make-elite-bar-mark.py [папка]  (без папки — прямо в Resources/UI/HUD)
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "razlom", "Assets", "Resources", "UI", "HUD")

SIZE = 128
SS = 4  # рисуем вчетверо крупнее и уменьшаем: гладкий край без лесенки
N = SIZE * SS


def grid():
    y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    return x / SS, y / SS  # координаты в пикселях итогового холста


def ellipse(x, y, cx, cy, rx, ry):
    """Мягкая эллиптическая маска: 1 внутри, 0 снаружи, край — полпикселя итогового холста."""
    d = np.sqrt(((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2)
    return np.clip((1.0 - d) * min(rx, ry) + .5, 0.0, 1.0)


def slanted(x, y, cx, cy, rx, ry, angle):
    """Эллипс, повёрнутый на angle радиан: глазницы сведены к носу — череп хмурится, а не удивляется."""
    ca, sa = np.cos(angle), np.sin(angle)
    u = (x - cx) * ca + (y - cy) * sa
    v = -(x - cx) * sa + (y - cy) * ca
    return ellipse(u, v, 0.0, 0.0, rx, ry)


def horn(x, y, p0, p1, p2, w0, w1, steps=90):
    """Рог: квадратичная кривая Безье с сужением от w0 у черепа до w1 на острие."""
    out = np.zeros_like(x)
    for t in np.linspace(0.0, 1.0, steps):
        px = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t ** 2 * p2[0]
        py = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t ** 2 * p2[1]
        r = (w0 + (w1 - w0) * t ** .8) * .5
        out = np.maximum(out, ellipse(x, y, px, py, r, r))
    return out


def skull():
    x, y = grid()
    c = SIZE * .5
    # Череп: свод и челюсть, чуть уже книзу.
    head = np.maximum(ellipse(x, y, c, 74, 23, 21), ellipse(x, y, c, 90, 15, 12))
    # Рога: от висков вбок и вверх, острия расходятся — силуэт читается и в 30 пикселях.
    left = horn(x, y, (c - 16, 66), (c - 50, 64), (c - 44, 20), 17, 3)
    right = horn(x, y, (c + 16, 66), (c + 50, 64), (c + 44, 20), 17, 3)
    shape = np.maximum(head, np.maximum(left, right))
    # Глазницы и нос — дырки насквозь: у знака должно быть лицо, а не пятно.
    eyes = np.maximum(slanted(x, y, c - 9, 76, 7.0, 4.6, .45), slanted(x, y, c + 9, 76, 7.0, 4.6, -.45))
    nose = ellipse(x, y, c, 87, 2.6, 3.6)
    shape = shape * (1.0 - np.maximum(eyes, nose))
    return shape


def dry_brush(alpha, seed):
    """
    Сухая кисть: край рвётся шумом, внутри — редкие просветы-штрихи вдоль мазка (горизонтальные,
    как у знаков забега). Силуэт при этом остаётся целым — знак маленький.
    """
    rng = np.random.default_rng(seed)
    h, w = alpha.shape
    # Рваный край: сдвигаем порог альфы шумом, вытянутым вдоль x.
    edge_noise = ndimage.gaussian_filter(rng.standard_normal((h, w)), (.8 * SS, 3.0 * SS))
    edge_noise /= np.abs(edge_noise).max() + 1e-6
    soft = ndimage.gaussian_filter(alpha, .9 * SS)
    ragged = np.clip((soft - .5 + edge_noise * .38) * 6.0 + .5, 0.0, 1.0)
    # Просветы щетины внутри: тонкие горизонтальные нити пониже плотности.
    streak = ndimage.gaussian_filter(rng.standard_normal((h, w)), (.35 * SS, 9.0 * SS))
    streak = (streak - streak.mean()) / (streak.std() + 1e-6)
    gaps = np.clip((streak - 1.15) * 1.1, 0.0, .7)
    return np.clip(ragged * (1.0 - gaps), 0.0, 1.0)


def down(alpha):
    image = Image.fromarray(alpha.astype(np.float32), "F").resize((SIZE, SIZE), Image.LANCZOS)
    return np.clip(np.asarray(image), 0.0, 1.0)


def edge_fade(alpha, width=3):
    h, w = alpha.shape
    ry = np.clip(np.minimum(np.arange(h), np.arange(h)[::-1]) / float(width), 0.0, 1.0)
    rx = np.clip(np.minimum(np.arange(w), np.arange(w)[::-1]) / float(width), 0.0, 1.0)
    return alpha * ry[:, None] * rx[None, :]


def save(alpha, name, out):
    rgba = np.dstack([np.ones(alpha.shape + (3,), np.float32), alpha])
    path = os.path.join(out, name + ".png")
    Image.fromarray((np.clip(rgba, 0.0, 1.0) * 255 + .5).astype(np.uint8), "RGBA").save(path)
    print(path)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else OUT
    os.makedirs(out, exist_ok=True)
    shape = skull()
    mark = edge_fade(down(dry_brush(shape, seed=2909)))
    # Подложка: силуэт без дырок, раздут на ~4 пикселя и размыт — чернильное пятно под знаком.
    solid = np.maximum(shape, ndimage.binary_fill_holes(shape > .5).astype(np.float32))
    ink = ndimage.grey_dilation(solid, size=(9 * SS, 9 * SS))
    ink = ndimage.gaussian_filter(ink, 2.6 * SS)
    ink = edge_fade(down(np.clip(ink * 1.25, 0.0, 1.0)), 6)
    save(mark, "EliteBarMark", out)
    save(ink, "EliteBarMarkInk", out)


if __name__ == "__main__":
    main()
