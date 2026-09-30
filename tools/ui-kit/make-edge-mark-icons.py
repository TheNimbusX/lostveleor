"""Знаки тлеющих меток у края экрана (владелец 30.09, выбор 2a на доске concepts-2026-09-30-hud-polish:
«круг со значком и шевроном у края — Вендиго · 14 м, Шипомёт, Камнекопыт…»).

Значков врагов на листе icons-B-ink нет, а генерацию без наших кадров не делаем — поэтому знаки
нарисованы здесь мазками кисти по той же манере: кремовая тушь, рваный край, сухая кисть. Каждый
знак — белая маска (RGB белый, A — тушь), квадрат 256 px с полями; цвет даёт метка (кремовый,
красный у угрозы). Рисунок крупный и простой: в метке знак ≈ 28 px при 1080p, читается силуэт.

Файлы Assets/UI/EdgeMarks/*.png перезаписываются на месте — .meta и ссылки в префабе
WorldEdgeMarksWc сохраняются. Любой знак можно заменить рисованным вручную с тем же именем.

  wendigo      — Лесной вендиго: рогатый череп
  thorncaster  — Шипомёт: ветка с шипами
  stonehoof    — Камнекопыт: след раздвоенного копыта
  bud          — Плюй-плод: плод с листом и брызгой
  rootsnarer   — Корнехват: хватающие корни
  splitter     — Расщепень (и детёныш): расколотое семя
  guardian     — Лесной хранитель: лист с прожилкой
  rootswarm    — Корнеполз: извилистый корешок
  chevron      — шеврон метки, смотрит вправо (+x); метка поворачивает его к цели

Запуск: python tools/ui-kit/make-edge-mark-icons.py [папка]
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "razlom", "Assets", "UI", "EdgeMarks")
SIZE, PAD = 256, 20
WORK = 1024  # рисуем вчетверо крупнее и уменьшаем: край мягкий без лесенки


def spline(points, samples=24):
    """Catmull-Rom через точки: ровный ход кисти без изломов на стыках."""
    if len(points) < 3:
        a, b = np.array(points[0], float), np.array(points[-1], float)
        return [tuple(a + (b - a) * t) for t in np.linspace(0.0, 1.0, samples)]
    p = [np.array(points[0], float)] + [np.array(q, float) for q in points] + [np.array(points[-1], float)]
    out = []
    for i in range(1, len(p) - 2):
        p0, p1, p2, p3 = p[i - 1], p[i], p[i + 1], p[i + 2]
        for t in np.linspace(0.0, 1.0, samples, endpoint=False):
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                                    + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)))
    out.append(tuple(p[-2]))
    return out


class Glyph:
    """Холст знака: координаты 0..1, y вверх; мазки — оттиски круглой кисти вдоль пути."""

    def __init__(self):
        self.ink = Image.new("L", (WORK, WORK), 0)
        self.draw = ImageDraw.Draw(self.ink)
        self.holes = Image.new("L", (WORK, WORK), 0)
        self.hole_draw = ImageDraw.Draw(self.holes)

    @staticmethod
    def px(point):
        return point[0] * WORK, (1.0 - point[1]) * WORK

    def stroke(self, points, width, start=0.35, end=0.25, samples=28):
        """Мазок: нажим растёт от start к полной ширине и сходит к end (доли ширины)."""
        path = spline(points, samples)
        n = len(path)
        for i, point in enumerate(path):
            t = i / max(1, n - 1)
            press = min(1.0, start + (1.0 - start) * min(1.0, t / 0.28))
            press = min(press, end + (1.0 - end) * min(1.0, (1.0 - t) / 0.3))
            r = width * press * WORK * 0.5
            x, y = self.px(point)
            self.draw.ellipse((x - r, y - r, x + r, y + r), fill=255)

    def fill(self, points):
        self.draw.polygon([self.px(p) for p in spline(points + [points[0]], 12)], fill=255)

    def disc(self, center, radius):
        x, y = self.px(center)
        r = radius * WORK
        self.draw.ellipse((x - r, y - r, x + r, y + r), fill=255)

    def hole(self, center, radius):
        x, y = self.px(center)
        r = radius * WORK
        self.hole_draw.ellipse((x - r, y - r, x + r, y + r), fill=255)

    def cut(self, points, width):
        for point in spline(points, 20):
            x, y = self.px(point)
            r = width * WORK * 0.5
            self.hole_draw.ellipse((x - r, y - r, x + r, y + r), fill=255)

    def finish(self, seed):
        """Тушь: рваный край и сухая кисть, потом уменьшение в квадрат с полями."""
        rng = np.random.default_rng(seed)
        mask = np.asarray(self.ink, np.float32) / 255.0
        mask *= 1.0 - np.asarray(self.holes, np.float32) / 255.0
        # Рваный край: размытая маска, порог сдвинут крупным и мелким шумом.
        soft = ndimage.gaussian_filter(mask, 10.0)
        coarse = ndimage.gaussian_filter(rng.standard_normal(mask.shape).astype(np.float32), 9.0)
        fine = ndimage.gaussian_filter(rng.standard_normal(mask.shape).astype(np.float32), 2.2)
        coarse /= max(1e-6, np.abs(coarse).max())
        fine /= max(1e-6, np.abs(fine).max())
        edge = soft + coarse * 0.22 + fine * 0.12
        alpha = np.clip((edge - 0.46) / 0.06, 0.0, 1.0)
        # Сухая кисть: у края тушь редеет штрихами, середина мазка плотная.
        streak = ndimage.gaussian_filter(rng.standard_normal(mask.shape).astype(np.float32), (1.0, 9.0))
        streak /= max(1e-6, np.abs(streak).max())
        rim = np.clip(1.0 - (soft - 0.45) / 0.5, 0.25, 1.0)
        alpha *= 1.0 - np.clip((streak - 0.3) / 0.25, 0.0, 1.0) * rim * 0.85
        ys, xs = np.nonzero(alpha > 0.05)
        y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
        crop = alpha[y0:y1, x0:x1]
        side = max(crop.shape)
        square = np.zeros((side, side), np.float32)
        oy, ox = (side - crop.shape[0]) // 2, (side - crop.shape[1]) // 2
        square[oy:oy + crop.shape[0], ox:ox + crop.shape[1]] = crop
        small = Image.fromarray((square * 255 + .5).astype(np.uint8), "L").resize((SIZE - 2 * PAD,) * 2, Image.LANCZOS)
        canvas = Image.new("L", (SIZE, SIZE), 0)
        canvas.paste(small, (PAD, PAD))
        white = Image.new("L", (SIZE, SIZE), 255)
        return Image.merge("RGBA", (white, white, white, canvas))


def mirror(points):
    return [(1.0 - x, y) for x, y in points]


def wendigo():
    g = Glyph()
    # Череп: вытянутая морда вниз, глазницы.
    g.fill([(0.40, 0.60), (0.60, 0.60), (0.585, 0.44), (0.535, 0.20), (0.465, 0.20), (0.415, 0.44)])
    g.hole((0.455, 0.49), 0.034)
    g.hole((0.545, 0.49), 0.034)
    g.cut([(0.5, 0.36), (0.5, 0.25)], 0.018)
    for side in (lambda p: p, mirror):
        g.stroke(side([(0.43, 0.60), (0.36, 0.70), (0.26, 0.80), (0.17, 0.93)]), 0.075, 0.9, 0.2)
        g.stroke(side([(0.36, 0.70), (0.25, 0.68), (0.14, 0.72)]), 0.055, 0.8, 0.15)
        g.stroke(side([(0.29, 0.77), (0.31, 0.88), (0.30, 0.97)]), 0.05, 0.8, 0.15)
        g.stroke(side([(0.41, 0.585), (0.31, 0.57), (0.25, 0.53)]), 0.05, 0.9, 0.3)
    return g.finish(11)


def thorncaster():
    g = Glyph()
    g.stroke([(0.50, 0.06), (0.49, 0.30), (0.50, 0.52)], 0.10, 0.9, 0.8)
    g.stroke([(0.50, 0.46), (0.40, 0.62), (0.26, 0.80), (0.20, 0.92)], 0.075, 0.9, 0.15)
    g.stroke([(0.50, 0.50), (0.62, 0.66), (0.76, 0.80), (0.82, 0.93)], 0.075, 0.9, 0.15)
    g.stroke([(0.50, 0.50), (0.51, 0.72), (0.50, 0.97)], 0.07, 0.9, 0.12)
    # Шипы — короткие мазки, сходящие на нет.
    thorns = [((0.48, 0.20), (0.36, 0.25)), ((0.51, 0.32), (0.63, 0.37)), ((0.34, 0.70), (0.24, 0.66)),
              ((0.68, 0.72), (0.79, 0.68)), ((0.51, 0.78), (0.61, 0.83)), ((0.43, 0.58), (0.36, 0.52))]
    for a, b in thorns:
        g.stroke([a, b], 0.05, 1.0, 0.05, 12)
    return g.finish(23)


def stonehoof():
    g = Glyph()
    # Раздвоенное копыто: половинки сходятся к носкам наверху, ниже — два прибылых пальца.
    half = [(0.46, 0.94), (0.46, 0.30), (0.41, 0.22), (0.31, 0.26), (0.22, 0.46), (0.23, 0.72), (0.34, 0.92)]
    g.fill(half)
    g.fill(mirror(half))
    g.disc((0.25, 0.10), 0.055)
    g.disc((0.75, 0.10), 0.055)
    return g.finish(37)


def bud():
    g = Glyph()
    g.disc((0.44, 0.42), 0.25)
    g.hole((0.67, 0.44), 0.07)
    g.stroke([(0.44, 0.64), (0.47, 0.78), (0.40, 0.92)], 0.05, 1.0, 0.3)
    g.fill([(0.46, 0.76), (0.58, 0.88), (0.74, 0.90), (0.66, 0.78)])
    g.disc((0.83, 0.47), 0.055)
    g.disc((0.93, 0.52), 0.032)
    return g.finish(41)


def rootsnarer():
    g = Glyph()
    g.stroke([(0.50, 0.97), (0.50, 0.78), (0.50, 0.60)], 0.12, 0.8, 1.0)
    g.stroke([(0.50, 0.62), (0.36, 0.44), (0.20, 0.30), (0.12, 0.34), (0.15, 0.44)], 0.085, 1.0, 0.1)
    g.stroke([(0.50, 0.62), (0.64, 0.44), (0.80, 0.30), (0.88, 0.34), (0.85, 0.44)], 0.085, 1.0, 0.1)
    g.stroke([(0.50, 0.62), (0.52, 0.40), (0.47, 0.18), (0.52, 0.05)], 0.08, 1.0, 0.1)
    return g.finish(53)


def splitter():
    g = Glyph()
    left = [(0.47, 0.92), (0.47, 0.72), (0.41, 0.60), (0.46, 0.46), (0.41, 0.30), (0.46, 0.10),
            (0.28, 0.16), (0.17, 0.40), (0.19, 0.68), (0.30, 0.88)]
    g.fill(left)
    right = [(x + 0.08, y) for x, y in mirror(left)]
    g.fill([(x - 0.02, y) for x, y in right])
    return g.finish(67)


def guardian():
    g = Glyph()
    g.fill([(0.50, 0.95), (0.72, 0.74), (0.78, 0.50), (0.66, 0.24), (0.50, 0.12), (0.34, 0.24),
            (0.22, 0.50), (0.28, 0.74)])
    g.cut([(0.50, 0.86), (0.50, 0.50), (0.50, 0.18)], 0.03)
    g.cut([(0.50, 0.58), (0.64, 0.70)], 0.024)
    g.cut([(0.50, 0.42), (0.36, 0.54)], 0.024)
    g.stroke([(0.50, 0.14), (0.50, 0.03)], 0.06, 1.0, 0.5)
    return g.finish(79)


def rootswarm():
    g = Glyph()
    g.stroke([(0.18, 0.18), (0.36, 0.30), (0.40, 0.52), (0.58, 0.64), (0.72, 0.62), (0.80, 0.74)], 0.13, 0.3, 0.9)
    g.disc((0.82, 0.78), 0.09)
    g.stroke([(0.37, 0.33), (0.24, 0.40)], 0.04, 1.0, 0.1, 12)
    g.stroke([(0.52, 0.61), (0.52, 0.76)], 0.04, 1.0, 0.1, 12)
    return g.finish(83)


def chevron():
    g = Glyph()
    g.stroke([(0.22, 0.10), (0.52, 0.34), (0.78, 0.50)], 0.20, 0.7, 1.0)
    g.stroke([(0.78, 0.50), (0.52, 0.66), (0.22, 0.90)], 0.20, 1.0, 0.7)
    return g.finish(97)


GLYPHS = {
    "wendigo": wendigo, "thorncaster": thorncaster, "stonehoof": stonehoof, "bud": bud,
    "rootsnarer": rootsnarer, "splitter": splitter, "guardian": guardian, "rootswarm": rootswarm,
    "chevron": chevron,
}


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else OUT
    os.makedirs(out, exist_ok=True)
    for name, make in GLYPHS.items():
        path = os.path.join(out, name + ".png")
        make().save(path)
        print(path)


if __name__ == "__main__":
    main()
