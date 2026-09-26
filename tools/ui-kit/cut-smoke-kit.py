"""Нарезка листов материала «Дым и свет» (владелец, 25 сентября 2026) в спрайты пака.

Листы — ART/UI/smoke-kit-2026-09-25:
  sheet-smoke.png — белый дым и мазки на чёрном: маски, цвет даёт тема (RGB белый, A — яркость);
  sheet-light.png — оранжевый свет на чёрном: для аддитивного шейдера (RGB — оттенок в полную силу, A — яркость);
  menu-crack.png  — трещина главного меню: крупные ветви оставлены, отдельные нарисованные искры убраны
                    (в меню летят свои, живые);
  menu-plate.png  — ночной фон меню, копируется как есть.

Запуск: python tools/ui-kit/cut-smoke-kit.py
"""
import os

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "ART", "UI", "smoke-kit-2026-09-25")
KIT = os.path.join(ROOT, "razlom", "Assets", "UI", "Kit", "Smoke")
MENU = os.path.join(ROOT, "razlom", "Assets", "Resources", "UI", "MainMenu")

SMOKE_NAMES = ["smoke_band_1", "smoke_band_2", "smoke_plate", "smoke_blot_1", "smoke_blot_2",
               "smoke_ring", "brush_stroke_1", "brush_stroke_2"]
LIGHT_NAMES = ["light_ring", "light_arc", "light_thread_gem", "light_thread", "light_embers",
               "light_gem", "light_glow"]


def load(name):
    return np.asarray(Image.open(os.path.join(SRC, name)).convert("RGB")).astype(np.float32) / 255.0


def pieces(strength, threshold, join):
    """Прямоугольники отдельных элементов листа, по строкам сверху вниз и слева направо."""
    solid = ndimage.gaussian_filter(strength, 2.0) > threshold
    solid = ndimage.binary_dilation(solid, iterations=join)
    labels, count = ndimage.label(solid)
    boxes = []
    for index, box in enumerate(ndimage.find_objects(labels), start=1):
        area = int((labels[box] == index).sum())
        if area < 4000:
            continue
        boxes.append((box, index))
    # Строки — по середине элемента: элементы одной строки разной высоты, поэтому
    # новая строка начинается, только когда середина ниже середины первого в строке на 200 px.
    boxes.sort(key=lambda b: (b[0][0].start + b[0][0].stop) * .5)
    rows, row_center = [], None
    for b in boxes:
        center = (b[0][0].start + b[0][0].stop) * .5
        if row_center is None or center - row_center > 200:
            rows.append([])
            row_center = center
        rows[-1].append(b)
    return labels, [b for row in rows for b in sorted(row, key=lambda b: b[0][1].start)]


def edge_fade(alpha, width=10):
    """Край кадра — в ноль: у спрайта не должно быть жёсткой границы прямоугольника."""
    h, w = alpha.shape
    ramp_y = np.clip(np.minimum(np.arange(h), np.arange(h)[::-1]) / float(width), 0.0, 1.0)
    ramp_x = np.clip(np.minimum(np.arange(w), np.arange(w)[::-1]) / float(width), 0.0, 1.0)
    return alpha * ramp_y[:, None] * ramp_x[None, :]


def crop(image, labels, box, index, pad):
    ys, xs = box
    y0, y1 = max(ys.start - pad, 0), min(ys.stop + pad, image.shape[0])
    x0, x1 = max(xs.start - pad, 0), min(xs.stop + pad, image.shape[1])
    part = image[y0:y1, x0:x1].copy()
    # Только свой элемент: соседние куски, попавшие в рамку, гасятся.
    own = ndimage.binary_dilation(labels[y0:y1, x0:x1] == index, iterations=6)
    own = ndimage.gaussian_filter(own.astype(np.float32), 4.0)
    return part * own[..., None]


def save_mask(rgb, name):
    lum = rgb.mean(axis=2)
    alpha = np.clip((lum - 0.035) / 0.9, 0.0, 1.0)
    alpha = edge_fade(alpha)
    out = np.dstack([np.ones_like(alpha), np.ones_like(alpha), np.ones_like(alpha), alpha])
    Image.fromarray((out * 255 + .5).astype(np.uint8), "RGBA").save(os.path.join(KIT, name + ".png"))
    print(name, alpha.shape[::-1])


def light_rgba(rgb, floor=0.03):
    peak = rgb.max(axis=2)
    alpha = np.clip((peak - floor) / (1.0 - floor), 0.0, 1.0)
    hue = rgb / np.maximum(peak, 1e-4)[..., None]
    return np.dstack([np.clip(hue, 0.0, 1.0), edge_fade(alpha)])


def save_light(rgb, name, folder=KIT):
    out = light_rgba(rgb)
    Image.fromarray((out * 255 + .5).astype(np.uint8), "RGBA").save(os.path.join(folder, name + ".png"))
    print(name, out.shape[1::-1])


def save_map_shape():
    """Мягкая форма карты для шейдера: округлый клуб, края плавно тают в дым.

    Владелец 26 сентября: не «квадрат с рваными краями», а округлый клуб — суперэллипс 2,4 вместо
    3,2 и перо почти вдвое шире; край гуляет крупной волной, мелкая рвань приглушена. HudMinimap
    (ShapePower, ShapeRim) прижимает метки к краю по той же форме — степень менять вместе.
    Перо кончается до края кадра даже с шумом: у маски нет жёсткой границы квадрата.
    """
    n = 512
    y, x = np.mgrid[0:n, 0:n] / (n - 1) * 2 - 1
    r = (np.abs(x) ** 2.4 + np.abs(y) ** 2.4) ** (1 / 2.4)
    rng = np.random.default_rng(7)
    broad = ndimage.gaussian_filter(rng.standard_normal((n, n)), 18)
    fine = ndimage.gaussian_filter(rng.standard_normal((n, n)), 6)
    d = r + broad / np.abs(broad).max() * .05 + fine / np.abs(fine).max() * .012
    a = 1 - np.clip((d - .6) / .32, 0, 1)
    a = a * a * (3 - 2 * a)
    a = edge_fade(a, 6)
    out = np.dstack([np.ones_like(a)] * 3 + [a])
    Image.fromarray((out * 255 + .5).astype(np.uint8), "RGBA").save(os.path.join(KIT, "map_shape.png"))
    print("map_shape")


def save_soft_blot():
    """Мягкое тёмное пятно без краёв: плотная подложка под текстом подсказок, поверх — дым для фактуры."""
    n = 256
    y, x = np.mgrid[0:n, 0:n] / (n - 1) * 2 - 1
    r = np.sqrt(x * x + y * y)
    rng = np.random.default_rng(11)
    broad = ndimage.gaussian_filter(rng.standard_normal((n, n)), 10)
    r = r + broad / np.abs(broad).max() * .05
    a = 1 - np.clip((r - .5) / .48, 0, 1)
    a = a * a * (3 - 2 * a)
    out = np.dstack([np.ones_like(a)] * 3 + [a])
    Image.fromarray((out * 255 + .5).astype(np.uint8), "RGBA").save(os.path.join(KIT, "soft_blot.png"))
    print("soft_blot")


def main():
    os.makedirs(KIT, exist_ok=True)

    smoke = load("sheet-smoke.png")
    labels, boxes = pieces(smoke.mean(axis=2), 0.06, 12)
    assert len(boxes) == len(SMOKE_NAMES), "дым: ждали %d кусков, нашли %d" % (len(SMOKE_NAMES), len(boxes))
    for (box, index), name in zip(boxes, SMOKE_NAMES):
        save_mask(crop(smoke, labels, box, index, 24), name)

    light = load("sheet-light.png")
    labels, boxes = pieces(light.max(axis=2), 0.08, 34)
    assert len(boxes) == len(LIGHT_NAMES), "свет: ждали %d кусков, нашли %d" % (len(LIGHT_NAMES), len(boxes))
    for (box, index), name in zip(boxes, LIGHT_NAMES):
        part = crop(light, labels, box, index, 28)
        save_light(part, name)
        if name == "light_embers":
            # Отдельные искры для частиц: четыре самых крупных из россыпи.
            bright = ndimage.gaussian_filter(part.max(axis=2), 1.0) > 0.12
            spark_labels, count = ndimage.label(bright)
            sizes = ndimage.sum(bright, spark_labels, range(1, count + 1))
            order = np.argsort(sizes)[::-1][:4]
            for n, label in enumerate(order, start=1):
                box2 = ndimage.find_objects((spark_labels == label + 1).astype(np.int32))[0]
                save_light(crop(part, spark_labels, box2, label + 1, 14), "ember_%d" % n)

    # Трещина меню: оставляем связные ветви, одиночные искры гасим.
    crack = load("menu-crack.png")
    bright = crack.max(axis=2) > 0.18
    crack_labels, count = ndimage.label(ndimage.binary_dilation(bright, iterations=2))
    sizes = ndimage.sum(bright, crack_labels, range(1, count + 1))
    keep = np.isin(crack_labels, np.nonzero(sizes > 2500)[0] + 1)
    halo = ndimage.gaussian_filter(ndimage.binary_dilation(keep, iterations=22).astype(np.float32), 9.0)
    save_light(crack * np.clip(halo * 1.4, 0.0, 1.0)[..., None], "menu_crack", MENU)

    save_map_shape()
    save_soft_blot()

    Image.open(os.path.join(SRC, "menu-plate.png")).convert("RGB").save(os.path.join(MENU, "menu_night.png"))
    print("menu_night")


if __name__ == "__main__":
    main()
