"""Лого главного меню из исходника высокого разрешения и слой его огня (владелец, 26 сентября 2026:
«логотип сильно поднять бы качество, чуть сгладить и анимировать огоньки — искры в нём»).

Исходник — ART/UI/logo-final/TWR-logo-painted-stone.png (3088×959, вырезан по альфе). Выход —
razlom/Assets/Resources/UI/MainMenu:
  logo_stone_hd.png   — само лого 2048 px по ширине: почищена альфа (вокруг букв висела почти
                        невидимая оранжевая муть 1–3 %, на светлом фоне она грязнит), край букв
                        слегка сглажен, зерно генерации на камне приглушено краесохраняющим
                        размытием; уменьшение — Lanczos по предумноженному цвету (без тёмной каймы).
                        Мипмапы (Kaiser) включает MainMenuArtImport: лого всегда рисуется мельче
                        своего размера, без мипов край рябил лесенкой;
  logo_stone_glow.png — огонь лого для аддитивного света «Дыма и света» (RGB — оттенок в полную
                        силу, A — яркость, как у save_light в cut-smoke-kit.py): трещина через
                        «REMAINS» и нарисованные искры, вокруг — мягкое зарево. Отсветы на гранях
                        букв берутся слабее и гаснут с удалением от трещины, чтобы мерцание шло
                        от трещины, а не от всего слова. Холст шире лого на GLOW_PAD с каждой
                        стороны — зареву есть куда разойтись; MainMenuWcBuilder расширяет слой
                        на ту же долю.

Печатает ломаную трещины в долях лого (x слева, y сверху) — по ней MainMenuWcBuilder
ставит угли (MainMenuWcBuilder.LogoCrack); после смены исходника перенести числа туда.

Запуск: python tools/ui-kit/make-menu-logo.py
"""
import os

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "ART", "UI", "logo-final", "TWR-logo-painted-stone.png")
MENU = os.path.join(ROOT, "razlom", "Assets", "Resources", "UI", "MainMenu")

LOGO_WIDTH = 2048
GLOW_WIDTH = 1024
# Поле зарева вокруг лого, доля ширины лого (с каждой стороны; по высоте — те же пиксели).
GLOW_PAD = 48.0 / 1024.0


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def bilateral(rgb, weight, radius=2, sigma_space=1.5, sigma_colour=.08):
    """Краесохраняющее размытие: соседи с похожим цветом усредняются, трещины и грани остаются."""
    h, w, _ = rgb.shape
    pad = np.pad(rgb, ((radius, radius), (radius, radius), (0, 0)), mode="edge")
    wpad = np.pad(weight, radius, mode="constant")
    total = np.zeros_like(rgb)
    norm = np.zeros((h, w), np.float32)
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            shifted = pad[radius + dy:radius + dy + h, radius + dx:radius + dx + w]
            ws = np.exp(-(dx * dx + dy * dy) / (2 * sigma_space ** 2))
            diff = ((shifted - rgb) ** 2).sum(axis=2)
            k = ws * np.exp(-diff / (2 * sigma_colour ** 2)) * wpad[radius + dy:radius + dy + h, radius + dx:radius + dx + w]
            total += shifted * k[..., None]
            norm += k
    return total / np.maximum(norm, 1e-6)[..., None]


def resize(channel, size):
    return np.asarray(Image.fromarray(channel.astype(np.float32), "F").resize(size, Image.LANCZOS))


def resize_premultiplied(rgb, alpha, size):
    """Уменьшение по предумноженному цвету: прозрачный фон не подмешивает к краю свой цвет."""
    a = np.clip(resize(alpha, size), 0.0, 1.0)
    colour = np.dstack([resize(rgb[..., c] * alpha, size) for c in range(3)])
    colour = np.clip(colour / np.maximum(a, 1e-4)[..., None], 0.0, 1.0)
    return colour, a


def crack_line(fire):
    """Трещина — самая длинная горизонтальная нить огня: вытянутое по x размытие гасит
    вертикальные отсветы на гранях букв. Возвращает y трещины для каждого столбца."""
    response = ndimage.gaussian_filter(fire, (3, 40))
    ys = response.argmax(axis=0).astype(np.float32)
    ys = ndimage.median_filter(ys, 61, mode="nearest")
    return ndimage.gaussian_filter1d(ys, 30, mode="nearest"), response.max(axis=0)


def main():
    src = np.asarray(Image.open(SRC).convert("RGBA")).astype(np.float32) / 255.0
    rgb, alpha = src[..., :3], src[..., 3]
    h, w = alpha.shape

    # ---- огонь: насыщенный, яркий, тёплый (камень — кремовый, у него насыщенность ниже)
    peak = rgb.max(axis=2)
    saturation = (peak - rgb.min(axis=2)) / np.maximum(peak, 1e-4)
    warm = rgb[..., 0] - rgb[..., 2]
    fire = smoothstep(.55, .85, saturation) * smoothstep(.5, .85, peak) * smoothstep(.45, .8, warm) * alpha

    line, strength = crack_line(fire)
    rows = np.arange(h, dtype=np.float32)[:, None]
    distance = np.abs(rows - line[None, :])
    on_crack = np.exp(-(distance / 45.0) ** 2)
    # Нарисованные искры: маленькие пятна огня на прозрачном (вне плотных букв).
    letters = ndimage.binary_dilation(alpha > .85, iterations=3)
    specks, count = ndimage.label((fire > .3) & ~letters)
    sizes = ndimage.sum(np.ones_like(fire), specks, range(1, count + 1))
    small = np.isin(specks, np.nonzero(sizes < 4000)[0] + 1)
    sparks = ndimage.binary_dilation(small, iterations=4).astype(np.float32)
    # Отсветы граней: слабее и гаснут с удалением от трещины.
    rims = .28 * np.exp(-distance / 260.0)
    weight = np.maximum(np.maximum(on_crack, sparks), rims)
    glow_mask = fire * weight

    # ---- лого: альфа без мути, край мягче, камень ровнее
    clean = alpha * smoothstep(.02, .08, alpha)
    soft = ndimage.gaussian_filter(clean, 1.0)
    # У плотных букв край — ровная сглаженная ступень; полупрозрачное зарево остаётся как было.
    near_letters = ndimage.gaussian_filter(ndimage.binary_dilation(alpha > .85, iterations=4).astype(np.float32), 1.5)
    edge = smoothstep(.2, .8, soft)
    clean = clean * (1.0 - near_letters) + edge * near_letters
    smooth = bilateral(rgb, (alpha > .02).astype(np.float32))
    stone = rgb + (smooth - rgb) * .6 * (1.0 - fire[..., None])

    size = (LOGO_WIDTH, int(round(h * LOGO_WIDTH / float(w))))
    colour, a = resize_premultiplied(stone, clean, size)
    out = np.dstack([colour, a])
    Image.fromarray((out * 255 + .5).astype(np.uint8), "RGBA").save(os.path.join(MENU, "logo_stone_hd.png"))
    print("logo_stone_hd", size)

    # ---- огонь лого: ядро слабее (оно уже нарисовано в самом лого), зарево в два радиуса
    pad = int(round(GLOW_PAD * w))
    light = np.pad(rgb * glow_mask[..., None], ((pad, pad), (pad, pad), (0, 0)))
    near = np.dstack([ndimage.gaussian_filter(light[..., c], 5.0) for c in range(3)])
    far = np.dstack([ndimage.gaussian_filter(light[..., c], 22.0) for c in range(3)])
    glow = light * .35 + near * 1.6 + far * 3.2
    gh, gw = glow.shape[:2]
    gsize = (int(round(GLOW_WIDTH * (1 + 2 * GLOW_PAD))), int(round(gh * GLOW_WIDTH * (1 + 2 * GLOW_PAD) / float(gw))))
    glow = np.dstack([resize(glow[..., c], gsize) for c in range(3)])
    glow = np.clip(glow, 0.0, None)
    top = glow.max(axis=2)
    bright = np.clip(top / np.percentile(top[top > .02], 99.5), 0.0, 1.0)
    hue = np.clip(glow / np.maximum(top, 1e-4)[..., None], 0.0, 1.0)
    # Край холста — в ноль (как edge_fade в cut-smoke-kit.py).
    ramp_y = np.clip(np.minimum(np.arange(gsize[1]), np.arange(gsize[1])[::-1]) / 10.0, 0.0, 1.0)
    ramp_x = np.clip(np.minimum(np.arange(gsize[0]), np.arange(gsize[0])[::-1]) / 10.0, 0.0, 1.0)
    bright = bright * ramp_y[:, None] * ramp_x[None, :]
    out = np.dstack([hue, bright])
    Image.fromarray((out * 255 + .5).astype(np.uint8), "RGBA").save(os.path.join(MENU, "logo_stone_glow.png"))
    print("logo_stone_glow", gsize, "pad %.4f" % GLOW_PAD)

    # ---- ломаная трещины для углей: где огонь трещины заметен, 6 точек
    visible = np.nonzero(strength > .3)[0]
    x0, x1 = visible.min(), visible.max()
    print("crack (x, y from top):")
    for x in np.linspace(x0, x1, 6):
        xi = int(round(x))
        print("  (%.3f, %.3f)" % (xi / float(w), line[min(xi, w - 1)] / float(h)))


if __name__ == "__main__":
    main()
