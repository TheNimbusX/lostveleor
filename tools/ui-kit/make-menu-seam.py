"""Живой шов разлома на фоне главного меню «Дым» (владелец, 26 сентября 2026: светлый рассвет с
отдельной трещиной поверх читался хуже — выбран минималистичный фон menu_smoke: сланцевый дым,
справа от центра нарисован тонкий рваный шов огня).

Трещина теперь нарисована в самом фоне, отдельной трещины поверх нет (две трещины двоились).
Чтобы нарисованный шов жил, скрипт вынимает из фона его свет в аддитивные слои «Дыма и света» —
тот же кадр, те же пропорции, чёрный фон, только тёплый свет (RGB, альфа не нужна: шейдер
Razlom/UI Ink со светом прибавляет tex.rgb × tex.a, у RGB-картинки a = 1). Яркость — в sRGB-цвете,
а не в линейной альфе: у тусклых хвостов зарева 8 бит альфы дали бы ступени.

Выход — razlom/Assets/Resources/UI/MainMenu:
  menu_smoke.png       — сам фон: исходник ART/UI/concepts-2026-09-26-pass/menu-bg/6-smoke-4k.png
                         (4096×2294), высота обрезана по центру до кратной 4 (4096×2292): иначе
                         Unity не сожмёт его в BC7 (MainMenuArtImport) и оставит 38 МБ без сжатия;
  menu_smoke_glow.png  — свет шва 2048 px: ядро (жёлто-белые нити) в полную силу, рядом — ближнее
                         зарево того же огня; MainMenuRift мерцает им и вспыхивает «треском»;
  menu_smoke_halo.png  — дальнее зарево 1024 px (оно мягкое, больше не нужно): огонь шва и
                         подсвеченный им дым, размытые широко; MainMenuRift медленно дышит им.
Оба слоя MainMenuWcBuilder кладёт детьми фона на весь его прямоугольник — совпадают с нарисованным
швом на любых пропорциях экрана.

Печатает ломаную шва в долях фона (x слева, y сверху) — по ней MainMenuWcBuilder ставит угли
(MainMenuWcBuilder.Seam); после смены фона перенести числа туда.

Запуск: python tools/ui-kit/make-menu-seam.py
"""
import os

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "ART", "UI", "concepts-2026-09-26-pass", "menu-bg", "6-smoke-4k.png")
MENU = os.path.join(ROOT, "razlom", "Assets", "Resources", "UI", "MainMenu")
PLATE = os.path.join(MENU, "menu_smoke.png")

GLOW_WIDTH = 2048
HALO_WIDTH = 1024
# Точек ломаной шва (отрезков на одну меньше — по излучателю углей на отрезок).
SEAM_POINTS = 10


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def to_linear(c):
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= .0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - .055)


def blur(rgb, sigma):
    return np.dstack([ndimage.gaussian_filter(rgb[..., c], sigma) for c in range(3)])


def shrink(rgb, size):
    """Уменьшение в линейном свете: площадь пикселя усредняется честно (без потемнения ярких нитей)."""
    return np.dstack([np.asarray(Image.fromarray(rgb[..., c].astype(np.float32), "F").resize(size, Image.LANCZOS))
                      for c in range(3)])


def load_plate():
    """Фон, высота кратна 4. Всегда из исходника в ART, если он есть: запуск можно повторять."""
    src = SRC if os.path.exists(SRC) else PLATE
    image = Image.open(src).convert("RGB")
    w, h = image.size
    cut = h % 4
    if cut:
        top = cut // 2
        image = image.crop((0, top, w, h - (cut - top)))
    if src != PLATE or cut:
        image.save(PLATE)
        print("menu_smoke", image.size, "(из %s, обрезано %d строк)" % (os.path.basename(src), cut))
    return np.asarray(image).astype(np.float32) / 255.0


def save_rgb(light, name, size):
    small = np.clip(shrink(light, size), 0.0, None)
    # Полная сила — у самых ярких 0,2 % пикселей (как у огня лого): несколько точек-искр не
    # решают яркость всего слоя.
    top = small.max(axis=2)
    small = small / np.percentile(top[top > .01], 99.8)
    out = to_srgb(small)
    Image.fromarray((out * 255 + .5).astype(np.uint8), "RGB").save(os.path.join(MENU, name))
    print(name, size)


def seam_line(core):
    """Шов — самая длинная вертикальная нить огня: вытянутое по y размытие гасит отдельные искры и
    отростки. Возвращает x шва для каждой строки и силу шва в строке."""
    h, w = core.shape
    response = ndimage.gaussian_filter(core, (60, 6))
    # Шов справа от центра — искать в полосе 0,5…0,85 ширины, мимо искр по краям.
    lo, hi = int(w * .5), int(w * .85)
    xs = response[:, lo:hi].argmax(axis=1).astype(np.float32) + lo
    xs = ndimage.median_filter(xs, 121, mode="nearest")
    return ndimage.gaussian_filter1d(xs, 50, mode="nearest"), response[:, lo:hi].max(axis=1)


def main():
    srgb = load_plate()
    h, w = srgb.shape[:2]
    linear = to_linear(srgb)

    # ---- огонь: тёплый, яркий и насыщенный (сланцевый дым холодный — у него синий выше красного)
    peak = srgb.max(axis=2)
    saturation = (peak - srgb.min(axis=2)) / np.maximum(peak, 1e-4)
    warm = srgb[..., 0] - srgb[..., 2]
    fire = smoothstep(.08, .32, warm) * smoothstep(.32, .78, peak) * smoothstep(.22, .55, saturation)
    # Ядро — жёлто-белые нити самого шва: очень ярко, и зелёный уже высоко.
    core = fire * smoothstep(.8, .97, peak) * smoothstep(.5, .75, srgb[..., 1])

    # ---- свет шва: ядро в полную силу и ближнее зарево того же огня (по линейному свету фона)
    emit = linear * fire[..., None]
    lit_core = linear * core[..., None]
    glow = lit_core * 1.0 + blur(emit, 3.0) * .55 + blur(emit, 12.0) * .45
    gsize = (GLOW_WIDTH, int(round(h * GLOW_WIDTH / float(w))))
    save_rgb(glow, "menu_smoke_glow.png", gsize)

    # ---- дальнее зарево: огонь и подсвеченный им дым, размыты широко; ядро прибавлено, чтобы
    # зарево стояло вокруг шва, а не вокруг облаков справа
    halo = blur(emit + lit_core * 2.0, 40.0) * .6 + blur(emit + lit_core * 2.0, 120.0) * .8
    hsize = (HALO_WIDTH, int(round(h * HALO_WIDTH / float(w))))
    save_rgb(halo, "menu_smoke_halo.png", hsize)

    # ---- ломаная шва для углей
    xs, strength = seam_line(core)
    print("seam (x, y from top), strength %.3f..%.3f:" % (strength.min(), strength.max()))
    points = []
    for y in np.linspace(0, h - 1, SEAM_POINTS):
        yi = int(round(y))
        points.append("new Vector2(%.3ff, %.3ff)" % (xs[yi] / float(w), yi / float(h - 1)))
    for i in range(0, len(points), 4):
        print("            " + ", ".join(points[i:i + 4]) + ",")


if __name__ == "__main__":
    main()
