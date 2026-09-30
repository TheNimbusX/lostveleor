# -*- coding: utf-8 -*-
"""
Значки строки эффектов над портретом (этап 4, выбор владельца 30.09: кадр 1a).

Значки вырезаются прямо из утверждённого кадра
ART/UI/concepts-2026-09-30-hud-polish/1a-fight-rings-notches.png — те восемь, что стоят
в строке над портретом: корни, оглушение, защита от контроля, замедление, Живица (капля),
Порыв (волна), артефакт (камень), Blaze (пламя). Девятый — «Ясный настой» — та же капля,
перекрашенная в прозрачно-голубой: в кадре его нет, а язык значков должен остаться одним.

Как вырезается: фон внутри круга — ровный тёмный; альфа — насколько пиксель отличается
от этого фона, цвет — «раскрытый» из смеси с фоном. Кольцо таймера отрезается кругом
радиуса 26 px (в кадре внутренний край кольца — 28). Мелкие отдельные крапинки
(артефакты генерации) убираются. Результат — 128×128 RGBA в Assets/UI/Kit/Watercolor
(импорт — UiKitImport: спрайт, mip-уровни, без сжатия).

Запуск: python tools/ui-kit/cut-effect-icons.py  (из корня репозитория)
"""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE = os.path.join(ROOT, "ART", "UI", "concepts-2026-09-30-hud-polish", "1a-fight-rings-notches.png")
TARGET = os.path.join(ROOT, "razlom", "Assets", "UI", "Kit", "Watercolor")

# Центры кругов строки в кадре 2688×1520 (найдены по контрасту кольца и тёмного диска).
CENTRES = [530, 613, 697, 780, 863, 946, 1030, 1114]
CENTRE_Y = 1169
NAMES = ["root", "stun", "immune", "slow", "resin", "surge", "artifact", "blaze"]
RADIUS = 26.0
HALF = 28
SIZE = 128


def cut(src, cx):
    patch = src[CENTRE_Y - HALF:CENTRE_Y + HALF + 1, cx - HALF:cx + HALF + 1]
    ys, xs = np.mgrid[-HALF:HALF + 1, -HALF:HALF + 1]
    r = np.sqrt(xs ** 2 + ys ** 2)
    lum = patch.mean(axis=2)
    ring = (r >= 20) & (r <= 25)
    dark = patch[ring][lum[ring] < np.percentile(lum[ring], 40)]
    bg = np.median(dark, axis=0)
    distance = np.sqrt(((patch - bg) ** 2).sum(axis=2))
    alpha = np.clip((distance - 16.0) / 44.0, 0.0, 1.0)
    alpha *= np.clip((RADIUS - r) / 1.5, 0.0, 1.0)
    # Крапинки: связные пятна меньше 10 px без опоры на сам значок.
    solid = alpha > 0.15
    labels, count = ndimage.label(solid)
    if count > 1:
        sizes = ndimage.sum(solid, labels, range(1, count + 1))
        keep = np.zeros(count + 1, bool)
        keep[1:] = sizes >= 10
        mask = keep[labels]
        # Мягкий край вокруг оставленных пятен — расширение на 1 px.
        mask = ndimage.binary_dilation(mask, iterations=1)
        alpha *= mask
    alpha[alpha < 0.06] = 0.0
    a = alpha[..., None]
    colour = np.where(a > 0.02, (patch - (1.0 - a) * bg) / np.maximum(a, 0.02), patch)
    colour = np.clip(colour, 0, 255)
    rgba = np.dstack([colour, alpha * 255.0]).astype(np.uint8)
    image = Image.fromarray(rgba, "RGBA").resize((SIZE, SIZE), Image.LANCZOS)
    return image.filter(ImageFilter.UnsharpMask(radius=2, percent=60, threshold=2))


def clear_drop(drop):
    """«Ясный настой»: капля Живицы, перекрашенная в прозрачно-голубой."""
    pixels = np.asarray(drop).astype(np.float32)
    lum = pixels[..., :3].mean(axis=2, keepdims=True) / 255.0
    tint = np.array([0.70, 0.90, 1.0], np.float32)
    colour = np.clip((0.35 + 0.8 * lum) * tint * 255.0, 0, 255)
    return Image.fromarray(np.dstack([colour, pixels[..., 3]]).astype(np.uint8), "RGBA")


def main():
    if not os.path.exists(SOURCE):
        sys.exit("нет кадра " + SOURCE)
    src = np.asarray(Image.open(SOURCE).convert("RGB")).astype(np.float32)
    made = {}
    for cx, name in zip(CENTRES, NAMES):
        made[name] = cut(src, cx)
    made["clear"] = clear_drop(made["resin"])
    for name, image in made.items():
        path = os.path.join(TARGET, "wc_effect_" + name + ".png")
        image.save(path)
        print(path)


if __name__ == "__main__":
    main()
