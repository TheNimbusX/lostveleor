"""Круги карты тушью (выбор владельца 30.09, концепты перехода a5/a6): кольцо и заполненный круг кистью.

Карта пути на дымной завесе (Game.View/SmokeRouteMap) рисует узлы тушью: пройденные арены — заполненные
круги, развилка впереди — пустые кольца. Отдельного рисованного листа для них нет, поэтому оба спрайта
сворачиваются в круг из мазка brush_stroke_1 набора «Дым и свет» (Assets/UI/Kit/Smoke): волокна кисти те
же, что у дорог карты и полос HUD. Кисть ложится с нажимом, обходит круг чуть больше оборота и
отрывается — начало и хвост мазка перекрываются, как у кольца, нарисованного одним движением.

Маски белые (RGB = 1, A — плотность краски), цвет даёт карта. Край кадра — в ноль.

Запуск: python tools/ui-kit/make-inkmap-kit.py
  ink_ring.png — тонкое кольцо (пустой круг развилки);
  ink_disc.png — круг, заполненный тушью, с рваной кромкой кисти (пройденный узел, загоревшийся узел).
"""
import os

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
KIT = os.path.join(ROOT, "razlom", "Assets", "UI", "Kit", "Smoke")
SIZE = 320

# Полоса краски мазка brush_stroke_1 (1433×200): строки 55–139 сверху, середина 97; тело — столбцы 97–1270.
BAND_MID, BAND_HALF = 97.0, 42.0


def load_stroke():
    image = Image.open(os.path.join(KIT, "brush_stroke_1.png")).convert("RGBA")
    return np.asarray(image)[..., 3].astype(np.float32) / 255.0


def edge_fade(alpha, width=6):
    h, w = alpha.shape
    ramp_y = np.clip(np.minimum(np.arange(h), np.arange(h)[::-1]) / float(width), 0.0, 1.0)
    ramp_x = np.clip(np.minimum(np.arange(w), np.arange(w)[::-1]) / float(width), 0.0, 1.0)
    return alpha * ramp_y[:, None] * ramp_x[None, :]


def wrapped(stroke, radius, thickness, turns=1.12, start=1.9, u0=.05, u1=.93, wobble=.012, seed=3):
    """Мазок, свёрнутый в кольцо: радиус и толщина краски — в долях полукадра.

    Кисть идёт по часовой стрелке от угла start (радианы), проходит turns оборота; нажим к середине
    мазка сильнее, радиус чуть гуляет — рука, а не циркуль.
    """
    h, w = stroke.shape
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    x = (x + .5) / SIZE * 2 - 1
    y = (y + .5) / SIZE * 2 - 1
    r = np.sqrt(x * x + y * y)
    theta = np.arctan2(-y, x)
    # Угол вдоль хода кисти (по часовой от начала), 0..2π.
    along0 = np.mod(start - theta, 2 * np.pi)
    total = turns * 2 * np.pi
    rng = np.random.default_rng(seed)
    phase = rng.uniform(0, 2 * np.pi, 3)
    out = np.zeros_like(r)
    for lap in range(int(np.ceil(turns)) + 1):
        along = along0 + lap * 2 * np.pi
        inside = along <= total
        s = np.clip(along / total, 0, 1)
        centre = radius * (1 + wobble * (np.sin(along * 1.5 + phase[0]) + .6 * np.sin(along * 3.7 + phase[1])))
        press = .78 + .22 * np.sin(np.pi * s) + .05 * np.sin(along * 5 + phase[2])
        half = thickness * .5 * press
        rows = BAND_MID + (r - centre) / np.maximum(half, 1e-4) * BAND_HALF
        cols = (u0 + (u1 - u0) * s) * (w - 1)
        sample = ndimage.map_coordinates(stroke, [rows.ravel(), cols.ravel()], order=1, mode="constant", cval=0.0)
        sample = sample.reshape(r.shape) * inside
        out = np.maximum(out, sample)
    return out


def save(alpha, name):
    alpha = edge_fade(np.clip(alpha, 0, 1))
    rgba = np.dstack([np.ones_like(alpha)] * 3 + [alpha])
    Image.fromarray((rgba * 255 + .5).astype(np.uint8), "RGBA").save(os.path.join(KIT, name + ".png"))
    print(name, alpha.shape[::-1])
    return alpha


def main():
    stroke = load_stroke()

    ring = wrapped(stroke, radius=.78, thickness=.11, turns=1.1, start=2.1)
    save(ring, "ink_ring")

    # Круг тушью: плотная середина с мягкой неровной кромкой и толстый мазок по краю — волокна кисти
    # по окружности, рваный наружный край. Внутри краска чуть неровная (волокна второго оборота).
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    x = (x + .5) / SIZE * 2 - 1
    y = (y + .5) / SIZE * 2 - 1
    r = np.sqrt(x * x + y * y)
    rng = np.random.default_rng(17)
    broad = ndimage.gaussian_filter(rng.standard_normal((SIZE, SIZE)), 12)
    broad /= np.abs(broad).max()
    core = 1 - np.clip((r + broad * .025 - .7) / .05, 0, 1)
    rim = wrapped(stroke, radius=.75, thickness=.26, turns=1.08, start=1.4, seed=5)
    # Разводы внутри: шум, вытянутый по кругу (кисть заливала круг кругами), — чуть светлее и темнее.
    theta = np.arctan2(y, x)
    streak = ndimage.map_coordinates(ndimage.gaussian_filter(rng.standard_normal((64, 512)), (1.2, 6), mode="wrap"),
                                     [r.ravel() * 48, (theta.ravel() + np.pi) / (2 * np.pi) * 511], order=1, mode="wrap")
    streak = streak.reshape(r.shape)
    streak /= np.abs(streak).max()
    texture = .93 + .07 * streak
    disc = np.maximum(core * texture, rim)
    save(disc, "ink_disc")


if __name__ == "__main__":
    main()
