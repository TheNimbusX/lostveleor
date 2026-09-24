"""
Пак UI «Ночная акварель» (P4), утверждён владельцем 22 сентября 2026.
Эталон вида: ART/UI/concepts-2026-09-22/final-2/kit-sheet-2.png (база),
kit-sheet-3-controls.png и kit-sheet-4-hud.png.

ПОЧЕМУ КОДОМ, А НЕ НАРЕЗКОЙ ЛИСТА. Весь стиль геометрический: волосяные
линии, ромбы, одно скругление. Вырезанные из сгенерированного листа детали
получаются с кривыми линиями разной толщины — ровно та несостыковка, которую
заметил владелец. Здесь каждая деталь строится из одних констант.

ПОЧЕМУ ВСЁ БЕЛОЕ. Цвет задаёт Unity (Image.color, тема UiTheme), а не
картинка: оранжевый, серый обычной редкости, бирюза редкой, заливка панелей
меняются в инспекторе без перерисовки. Двухтонные детали (камень редкости)
нарисованы в оттенках серого — тинт умножается и сохраняет грани.

МАСШТАБ. Рисуется в 2×: 2 пикселя спрайта = 1 единица Canvas (эталон 1920×1080).
Импорт с 200 px на единицу (пятое число в slices.txt), так что волосяная линия
в 2 px спрайта — ровно 1 px на 1080p и 2 px на 4K. Сглаживание: рисуем в
SS раз крупнее и сводим усреднением блоков — честное покрытие пикселя.

ОДНИ КОНСТАНТЫ НА ВСЁ: радиус R, толщина линии LINE. Заливка и рамка одной
формы: внешний край рамки совпадает с краем заливки, поэтому слои
«заливка + рамка + свечение» всегда стыкуются.

Запуск: python tools/ui-kit/build_watercolor.py
Выход:  razlom/Assets/UI/Kit/Watercolor/*.png + slices.txt
        artifacts/ui-kit/watercolor-preview.png — сборка образцов для проверки стыков.
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "razlom", "Assets", "UI", "Kit", "Watercolor")
PREVIEW = os.path.join(ROOT, "artifacts", "ui-kit")

SS = 8          # сглаживание
PPU = 200       # 2 px спрайта на единицу Canvas
R = 28          # радиус окон и карточек, px спрайта (= 14 единиц Canvas) — как у эталона
R_S = 16        # радиус ячеек, клавиш, строк (= 8 единиц Canvas)
LINE = 2        # волосяная линия, px спрайта (= 1 единица Canvas)
LINE_BOLD = 4   # рамка редкости и выбора (= 2 единицы Canvas)

slices = []


def big(w, h):
    return Image.new("L", (w * SS, h * SS), 0)


def shrink(mask):
    return mask.reduce(SS)


def rrect(d, x0, y0, x1, y1, r, fill=255):
    """Скруглённый прямоугольник в координатах спрайта (px), края — по границам пикселей."""
    d.rounded_rectangle([x0 * SS, y0 * SS, x1 * SS - 1, y1 * SS - 1], radius=max(0, r) * SS, fill=fill)


def save(name, mask, border=(0, 0, 0, 0), gray=None):
    """border: лево, низ, право, верх — порядок Unity."""
    a = np.asarray(mask, dtype=np.uint8)
    rgb = np.full(a.shape + (3,), 255, np.uint8) if gray is None else np.repeat(np.asarray(gray, np.uint8)[..., None], 3, 2)
    Image.fromarray(np.dstack([rgb, a]), "RGBA").save(os.path.join(OUT, name + ".png"))
    slices.append((name,) + tuple(border))


# ---------------------------------------------------------------- панели, ячейки, кнопки
def fill(name, w=64, h=64, r=R):
    m = big(w, h); rrect(ImageDraw.Draw(m), 0, 0, w, h, r)
    save(name, shrink(m), (r + 4,) * 4)


def frame(name, stroke, w=64, h=64, r=R):
    """Рамка той же формы, что заливка: внешний край совпадает с краем заливки."""
    m = big(w, h); d = ImageDraw.Draw(m)
    rrect(d, 0, 0, w, h, r, 255)
    rrect(d, stroke, stroke, w - stroke, h - stroke, r - stroke, 0)
    save(name, shrink(m), (r + 4,) * 4)


def glow(name, margin=48, r=R, blur=16, strength=1.0, inner=(64, 64)):
    """Свечение наружу. Image со свечением шире элемента на margin/2 единиц с каждой стороны."""
    w, h = inner[0] + margin * 2, inner[1] + margin * 2
    m = Image.new("L", (w, h), 0); d = ImageDraw.Draw(m)
    d.rounded_rectangle([margin, margin, w - margin - 1, h - margin - 1], radius=r, fill=255)
    g = m.filter(ImageFilter.GaussianBlur(blur))
    # Внутри элемента свечение не нужно: его закрывает заливка, а в прозрачных
    # ячейках оно бы замутнило предмет. Оставляем только внешнюю часть.
    g = np.asarray(g, np.float32) * strength
    inner = np.asarray(m, np.float32) / 255.0
    g = g * (1 - inner)
    save(name, Image.fromarray(np.clip(g, 0, 255).astype(np.uint8)), (margin + r, min(margin + r, h // 2 - 1), margin + r, min(margin + r, h // 2 - 1)))


def inner_glow(name, size=128, r=R, depth=18):
    """Мягкая подсветка изнутри от края (редкая ячейка)."""
    m = big(size, size); rrect(ImageDraw.Draw(m), 0, 0, size, size, r)
    m = shrink(m)
    a = np.asarray(m, np.float32) / 255.0
    # Расстояние до края: размытая инверсия внутри формы.
    edge = Image.fromarray(((1 - a) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(depth / 2))
    e = np.asarray(edge, np.float32) / 255.0
    val = np.clip(e * 2.2, 0, 1) * a
    save(name, Image.fromarray((val * 255).astype(np.uint8)), (r + depth,) * 4)


def top_highlight(name, w=80, h=96, r=R):
    """
    Свет стекла: тонкая яркая кромка по верху изнутри и мягкий спад на верхние 60%.
    Владелец 22 сентября: зерно на панелях читалось как «каменная подложка» —
    глубину теперь даёт свет сверху и тень снизу, как у тёмного стекла эталона.
    """
    m = big(w, h); rrect(ImageDraw.Draw(m), 0, 0, w, h, r)
    a = np.asarray(shrink(m), np.float32) / 255.0
    ys = np.arange(h, dtype=np.float32)[:, None]
    ramp = np.clip(1 - ys / (h * 0.6), 0, 1) ** 2.2 * 0.25
    rim = ((ys >= 1) & (ys < 3)).astype(np.float32) * 0.5
    val = np.clip(np.maximum(ramp, rim), 0, 1) * a
    save(name, Image.fromarray((val * 255).astype(np.uint8)), (r + 4, 4, r + 4, int(h * 0.6)))


def bottom_shade(name, w=80, h=96, r=R):
    """Тень внизу панели: тёмный спад на нижние 55% (тинт — цвет вуали)."""
    m = big(w, h); rrect(ImageDraw.Draw(m), 0, 0, w, h, r)
    a = np.asarray(shrink(m), np.float32) / 255.0
    ys = np.arange(h, dtype=np.float32)[:, None]
    ramp = np.clip((ys - h * 0.45) / (h * 0.55), 0, 1) ** 1.6
    save(name, Image.fromarray((a * ramp * 255).astype(np.uint8)), (r + 4, int(h * 0.55), r + 4, 4))


def dashed_frame(name, size=176, r=R, stroke=LINE, dash=8, gap=6):
    """Пустая ячейка. Штрих центрирован на каждой стороне, углы сплошные — рисунок симметричен."""
    m = big(size, size); d = ImageDraw.Draw(m)
    rrect(d, 0, 0, size, size, r, 255); rrect(d, stroke, stroke, size - stroke, size - stroke, r - stroke, 0)
    a = np.asarray(shrink(m), np.float32)
    period = dash + gap
    span = size - 2 * r
    count = max(1, int((span + gap) // period))
    used = count * period - gap
    start = r + (span - used) / 2
    keep = np.zeros(size, bool)
    for i in range(count):
        s = start + i * period
        keep[int(round(s)):int(round(s + dash))] = True
    keep[:r] = True; keep[size - r:] = True
    mask = np.ones_like(a, bool)
    ys, xs = np.mgrid[0:size, 0:size]
    straight_x = (xs >= r) & (xs < size - r)
    straight_y = (ys >= r) & (ys < size - r)
    mask &= ~(straight_x & ~keep[xs] & ((ys < stroke + 1) | (ys >= size - stroke - 1)))
    mask &= ~(straight_y & ~keep[ys] & ((xs < stroke + 1) | (xs >= size - stroke - 1)))
    save(name, Image.fromarray((a * mask).astype(np.uint8)))


# ---------------------------------------------------------------- капсулы (плашки, полосы, переключатель)
def capsule(name, h, w=None, stroke=0):
    w = w or h * 2
    r = h // 2
    m = big(w, h); d = ImageDraw.Draw(m)
    rrect(d, 0, 0, w, h, r, 255)
    if stroke: rrect(d, stroke, stroke, w - stroke, h - stroke, r - stroke, 0)
    # Сверху и снизу граница на 2 px меньше радиуса: иначе у капсулы нет средней
    # полосы для растяжения, и в высокой плашке посередине оставалась щель.
    save(name, shrink(m), (r, r - 2, r, r - 2))


# ---------------------------------------------------------------- круги
def circle(name, size=128, stroke=0):
    m = big(size, size); d = ImageDraw.Draw(m)
    d.ellipse([0, 0, size * SS - 1, size * SS - 1], fill=255)
    if stroke: d.ellipse([stroke * SS, stroke * SS, (size - stroke) * SS - 1, (size - stroke) * SS - 1], fill=0)
    save(name, shrink(m))


# ---------------------------------------------------------------- ромбы
def diamond_pts(cx, cy, r):
    return [(cx * SS, (cy - r) * SS), ((cx + r) * SS, cy * SS), (cx * SS, (cy + r) * SS), ((cx - r) * SS, cy * SS)]


def diamond(name, size):
    m = big(size, size); ImageDraw.Draw(m).polygon(diamond_pts(size / 2, size / 2, size / 2), fill=255)
    save(name, shrink(m))


def diamond_frame(name, size, stroke=LINE):
    m = big(size, size); d = ImageDraw.Draw(m)
    d.polygon(diamond_pts(size / 2, size / 2, size / 2), fill=255)
    inset = stroke * math.sqrt(2)  # толщина по нормали к стороне ромба = stroke
    d.polygon(diamond_pts(size / 2, size / 2, size / 2 - inset), fill=0)
    save(name, shrink(m))


def gem(name, size=40):
    """Камень редкости: четыре грани разной светлоты, светлая кромка. Серый — под тинт."""
    c = size / 2
    alpha = big(size, size); ImageDraw.Draw(alpha).polygon(diamond_pts(c, c, c), fill=255)
    tone = Image.new("L", (size * SS, size * SS), 0); d = ImageDraw.Draw(tone)
    k = SS
    top, right, bottom, left, mid = (c * k, 0), (size * k, c * k), (c * k, size * k), (0, c * k), (c * k, c * k)
    d.polygon([left, top, mid], fill=255)       # свет сверху-слева
    d.polygon([top, right, mid], fill=214)
    d.polygon([right, bottom, mid], fill=150)   # тень снизу-справа
    d.polygon([bottom, left, mid], fill=182)
    # Кромка: светлее граней, чтобы камень читался на любом фоне.
    rim = big(size, size); dr = ImageDraw.Draw(rim)
    dr.polygon(diamond_pts(c, c, c), fill=255); dr.polygon(diamond_pts(c, c, c - 2.2), fill=0)
    t = np.maximum(np.asarray(shrink(tone), np.float32), np.asarray(shrink(rim), np.float32))
    save(name, shrink(alpha), gray=t)


# ---------------------------------------------------------------- материал: серебряная проволока
# Эталон (kit-sheet-2) не рисует плоские линии: рамки — серебряная проволока со
# светлой сердцевиной и тёмными краями, верхняя кромка светлее нижней, на углах
# блики. Всё это задаётся формой через поле расстояний (SDF): одинаково для
# прямоугольников, капсул, кругов и ромбов. Спрайт остаётся серым — тинт темы
# красит металл в серебро, бирюзу редкости или оранжевый выбора, а блики остаются.
LIGHT = np.array([-0.6, -0.8], np.float32)  # свет сверху-слева (y вниз)
METAL = 3        # проволока рамки, px спрайта (1,5 единицы)
METAL_BOLD = 5   # рамка редкости и выбора


def grid(w, h):
    ys, xs = np.mgrid[0:h * SS, 0:w * SS].astype(np.float32)
    return (xs + .5) / SS, (ys + .5) / SS


def sdf_rrect(x, y, w, h, r):
    qx = np.abs(x - w / 2) - (w / 2 - r)
    qy = np.abs(y - h / 2) - (h / 2 - r)
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - r


def sdf_circle(x, y, size):
    return np.hypot(x - size / 2, y - size / 2) - size / 2


def sdf_diamond(x, y, size):
    return (np.abs(x - size / 2) + np.abs(y - size / 2) - size / 2) / math.sqrt(2)


def normals(d):
    gy, gx = np.gradient(d)
    n = np.hypot(gx, gy) + 1e-6
    return gx / n, gy / n


def down(a):
    h, w = a.shape
    return a.reshape(h // SS, SS, w // SS, SS).mean(axis=(1, 3))


def shade_save(name, inside, tone, border=(0, 0, 0, 0)):
    a = down(inside.astype(np.float32))
    g = down(np.clip(tone, 0, 1) * inside) / np.maximum(a, 1e-6)
    save(name, Image.fromarray((a * 255).astype(np.uint8)), border, gray=np.clip(g * 255, 0, 255))


def metal(name, d, border, stroke=METAL, corner_glint=True, keep=None):
    """Проволока по контуру поля d: сердцевина светлее краёв, свет сверху-слева, блики."""
    inside = (d <= 0) & (d >= -stroke)
    if keep is not None: inside &= keep
    t = (d + stroke / 2) / (stroke / 2)
    core = 1 - 0.22 * t * t
    nx, ny = normals(d)
    lam = nx * LIGHT[0] + ny * LIGHT[1]
    light = 0.84 + 0.16 * lam
    spec = np.clip(lam, 0, 1) ** 14 * 0.45
    tone = core * light + spec
    if corner_glint:
        # Блики на скруглениях: там, где нормаль не по осям (угол), металл ярче.
        diag = np.abs(nx * ny) * 2
        tone = tone + np.clip(diag, 0, 1) ** 2 * 0.22
    shade_save(name, inside, tone, border)


def pigment(name, d, border, pool=0.16, spread=5.0, vertical=True):
    """Заливка с «пигментом у края», как у акварели: к краю чуть темнее, сверху чуть светлее."""
    inside = d <= 0
    tone = 1 - pool * np.exp(np.minimum(d, 0) / spread)
    if vertical:
        yy = np.linspace(0, 1, d.shape[0], dtype=np.float32)[:, None]
        tone = tone * (1.0 - 0.10 * yy)
    shade_save(name, inside, tone, border)


def rrect_fill(name, w=80, h=80, r=R):
    x, y = grid(w, h); pigment(name, sdf_rrect(x, y, w, h, r), (r + 4,) * 4)


def rrect_metal(name, stroke, w=96, h=96, r=R):
    x, y = grid(w, h); metal(name, sdf_rrect(x, y, w, h, r), (r + 8,) * 4, stroke)


def capsule_fill(name, h, w):
    x, y = grid(w, h); r = h / 2
    pigment(name, sdf_rrect(x, y, w, h, r), (int(r), int(r) - 2, int(r), int(r) - 2), pool=.12, spread=h / 5)


def capsule_metal(name, h, w, stroke=METAL):
    x, y = grid(w, h); r = h / 2
    metal(name, sdf_rrect(x, y, w, h, r), (int(r), int(r) - 2, int(r), int(r) - 2), stroke, corner_glint=False)


def circle_metal(name, size, stroke):
    x, y = grid(size, size); metal(name, sdf_circle(x, y, size), (0, 0, 0, 0), stroke, corner_glint=False)


def diamond_metal(name, size, stroke=METAL):
    x, y = grid(size, size); metal(name, sdf_diamond(x, y, size), (0, 0, 0, 0), stroke, corner_glint=False)


def knob(name, size=48):
    """Ручка переключателя: шар с мягким светом сверху-слева."""
    x, y = grid(size, size)
    d = sdf_circle(x, y, size)
    px, py = (x - size / 2) / (size / 2), (y - size / 2) / (size / 2)
    tone = 0.80 + 0.20 * np.clip(-(px * .6 + py * .8), -1, 1) - 0.10 * np.clip(px * px + py * py, 0, 1)
    shade_save(name, d <= 0, tone)


def gem_cut(name, size):
    """Гранёный ромб: четыре грани, светлая кромка сверху-слева, искра. Серый — под тинт."""
    x, y = grid(size, size)
    c = size / 2
    d = sdf_diamond(x, y, size)
    px, py = x - c, y - c
    tone = np.where(py < 0, np.where(px < 0, 1.0, 0.84), np.where(px < 0, 0.70, 0.56)).astype(np.float32)
    # Рёбра между гранями чуть светлее — камень читается гранёным даже мелко.
    tone = tone + np.clip(1 - np.minimum(np.abs(px), np.abs(py)) / (size * .06), 0, 1) * 0.10
    rim = (d > -max(1.2, size * .06)) & (d <= 0)
    nx, ny = normals(d)
    lam = nx * LIGHT[0] + ny * LIGHT[1]
    tone = np.where(rim, 0.78 + 0.22 * lam, tone)
    tone = tone + np.exp(-((px + c * .32) ** 2 + (py + c * .32) ** 2) / (size * .07) ** 2) * 0.6
    shade_save(name, d <= 0, tone)


def grain(name, size=256, seed=7):
    """Акварельное зерно, бесшовное: шум фильтруется в частотах, поэтому края тайла сходятся. Слой Tiled поверх заливки."""
    rng = np.random.default_rng(seed)
    fy = np.fft.fftfreq(size)[:, None]; fx = np.fft.fftfreq(size)[None, :]
    f2 = fx * fx + fy * fy
    out = np.zeros((size, size), np.float32)
    for sigma, wgt in ((0.012, 1.0), (0.04, .55), (0.16, .25)):
        spec = np.fft.fft2(rng.standard_normal((size, size)))
        out += wgt * np.real(np.fft.ifft2(spec * np.exp(-f2 / (2 * sigma * sigma))))
    out = (out - out.min()) / (out.max() - out.min())
    out = np.clip((out - .35) / .65, 0, 1) ** 1.3
    save(name, Image.fromarray((out * 255).astype(np.uint8)))


# ---------------------------------------------------------------- значки-штрихи
def stroke_glyph(name, size, paths, width=5):
    m = big(size, size); d = ImageDraw.Draw(m)
    w = int(round(width * SS))
    for pts in paths:
        p = [(x * SS, y * SS) for x, y in pts]
        d.line(p, fill=255, width=w, joint="curve")
        for x, y in (p[0], p[-1]):
            d.ellipse([x - w / 2, y - w / 2, x + w / 2, y + w / 2], fill=255)
    save(name, shrink(m))


def tri_glyph(name, size, up):
    m = big(size, size); d = ImageDraw.Draw(m)
    s = size * SS
    pts = [(s * .5, s * .18), (s * .88, s * .82), (s * .12, s * .82)] if up else [(s * .12, s * .18), (s * .88, s * .18), (s * .5, s * .82)]
    d.polygon(pts, fill=255)
    save(name, shrink(m))


# ---------------------------------------------------------------- вуали
def veil_radial(name, size=256):
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32)
    d = np.hypot((xs - size / 2) / (size / 2), (ys - size / 2) / (size / 2)) / math.sqrt(2)
    a = np.clip((d - 0.25) / 0.75, 0, 1) ** 1.6
    save(name, Image.fromarray((a * 255).astype(np.uint8)))


def veil_linear(name, h=256, w=8):
    a = (np.linspace(0, 1, h, dtype=np.float32) ** 1.4)[:, None].repeat(w, 1)
    save(name, Image.fromarray((a * 255).astype(np.uint8)))


def solid(name, size=8):
    save(name, Image.new("L", (size, size), 255), (2, 2, 2, 2))


# ---------------------------------------------------------------- боевой HUD (лист kit-sheet-4-hud)
def frame_open_top(name, stroke=METAL, w=96, h=96, r=R):
    """
    Рамка без верхней прямой: остаются углы с коротким прямым хвостом (4 единицы).
    Середину верхней кромки дорисовывают две линии по бокам заголовка — так имя
    босса «разрывает» рамку, как на эталоне, при любой длине имени.
    """
    x, y = grid(w, h)
    b = r + 8
    keep = ~((x >= b) & (x < w - b) & (y < stroke + 2))
    metal(name, sdf_rrect(x, y, w, h, r), (b,) * 4, stroke, keep=keep)


def ring_ticks(name, size=128, count=60, r0=49, r1=61, width=2.6):
    """Кольцо из делений вокруг секунд перезарядки."""
    m = big(size, size); d = ImageDraw.Draw(m)
    c = size / 2 * SS
    for i in range(count):
        a = 2 * math.pi * i / count
        ca, sa = math.cos(a), math.sin(a)
        d.line([(c + ca * r0 * SS, c + sa * r0 * SS), (c + ca * r1 * SS, c + sa * r1 * SS)], fill=255, width=int(width * SS))
    save(name, shrink(m))


def ring_glow(name, size=192, ring=(56, 64), blur=7):
    """Свечение кольца баффа: спрайт в 1,5 раза шире элемента (слой шире на четверть с каждой стороны)."""
    m = Image.new("L", (size, size), 0); d = ImageDraw.Draw(m)
    c = size / 2
    d.ellipse([c - ring[1], c - ring[1], c + ring[1], c + ring[1]], fill=255)
    d.ellipse([c - ring[0], c - ring[0], c + ring[0], c + ring[0]], fill=0)
    save(name, m.filter(ImageFilter.GaussianBlur(blur)))


def blob(name, size=128):
    """Мягкое пятно света: подсветка за портретом, за значком баффа, за критом."""
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32)
    d = np.hypot(xs + .5 - size / 2, ys + .5 - size / 2) / (size / 2)
    a = np.exp(-d * d * 3.0) * np.clip(1 - d, 0, 1) ** .6
    save(name, Image.fromarray((a / a.max() * 255).astype(np.uint8)))


def haze(name, w=128, h=64):
    """Красное зарево низкого здоровья: сильнее слева (за сердцем), гаснет вправо и к краям."""
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    u, v = (xs + .5) / w, (ys + .5) / h
    horiz = np.clip(1 - u, 0, 1) ** 1.3 * .75 + np.exp(-((u - .12) / .16) ** 2) * .35
    vert = np.clip(np.minimum(v, 1 - v) / .28, 0, 1) ** 1.2
    a = np.clip(horiz * vert, 0, 1)
    save(name, Image.fromarray((a * 255).astype(np.uint8)))


def spark(name, size=64):
    """Искра крита: четыре длинных луча по осям, четыре коротких по диагоналям."""
    m = big(size, size); d = ImageDraw.Draw(m)
    c = size / 2

    def ray(angle, length, half):
        ca, sa = math.cos(angle), math.sin(angle)
        pts = [(c - sa * half, c + ca * half), (c + ca * length, c + sa * length), (c + sa * half, c - ca * half)]
        d.polygon([(px * SS, py * SS) for px, py in pts], fill=255)

    for i in range(4):
        ray(math.pi / 2 * i, c - 1, 3.2)
        ray(math.pi / 2 * i + math.pi / 4, c * .46, 2.4)
    d.ellipse([(c - 5) * SS, (c - 5) * SS, (c + 5) * SS, (c + 5) * SS], fill=255)
    save(name, shrink(m))


def arrow(name, size=40):
    """Стрелка героя на миникарте: наконечник с вырезом снизу."""
    m = big(size, size)
    ImageDraw.Draw(m).polygon([(x * SS, y * SS) for x, y in ((20, 3), (35, 36), (20, 27.5), (5, 36))], fill=255)
    save(name, shrink(m))


def pointer(name, w=36, h=48):
    """Указатель подсказки: две волосяные линии сходятся в полый гранёный ромб на острие (вправо)."""
    x, y = grid(w, h)
    tip = (w - 11, h / 2)

    def seg(px, py, ax, ay, bx, by):
        dx, dy = bx - ax, by - ay
        t = np.clip(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0, 1)
        return np.hypot(px - ax - t * dx, py - ay - t * dy)

    line = np.minimum(seg(x, y, 4, 5, tip[0], tip[1]), seg(x, y, 4, h - 5, tip[0], tip[1]))
    lines = line <= 1.2
    dd = (np.abs(x - tip[0]) + np.abs(y - tip[1]) - 9) / math.sqrt(2)
    gem = dd <= 0
    rim = gem & (dd >= -1.8)
    tone = np.where(gem, np.where(rim, 1.0, 0.14), 0.88)
    shade_save(name, lines | gem, tone)


def mouse(name, w=40, h=56):
    """Мышь для подсказки «левый клик»: корпус-контур, разрез кнопок, левая кнопка залита."""
    m = big(w, h); d = ImageDraw.Draw(m)
    rrect(d, 4, 2, w - 4, h - 2, 16, 255)
    rrect(d, 7, 5, w - 7, h - 5, 13, 0)
    mid, split = w / 2, 24
    # Левая кнопка: заливка четверти корпуса над разрезом.
    left = big(w, h); dl = ImageDraw.Draw(left)
    rrect(dl, 7, 5, w - 7, h - 5, 13, 255)
    dl.rectangle([mid * SS - SS, 0, w * SS, h * SS], fill=0)
    dl.rectangle([0, (split - 1.5) * SS, w * SS, h * SS], fill=0)
    d.bitmap((0, 0), left, fill=235)
    d.line([(mid * SS, 3 * SS), (mid * SS, split * SS)], fill=255, width=int(2.5 * SS))
    d.line([(5 * SS, split * SS), ((w - 5) * SS, split * SS)], fill=255, width=int(2.5 * SS))
    save(name, shrink(m))


def stat_glyphs():
    """
    Значки параметров для подсказок на тёмной карточке: силуэты старых
    Chrome/stat_glyph_* белым (тёмно-синие на тёмном не видны), цвет — от темы.
    """
    src = os.path.join(os.path.dirname(OUT), "Chrome")
    for key in ("heart", "lavidium", "cooldown", "damage", "range", "radius", "duration"):
        path = os.path.join(src, "stat_glyph_" + key + ".png")
        if not os.path.exists(path):
            continue
        a = Image.open(path).convert("RGBA").getchannel("A")
        save("wc_stat_" + key, a)


def map_sample(name, size=512, seed=5):
    """
    Образец местности для витрины миникарты: тёмные участки с неровными светлыми
    кромками (часть — пунктиром) и река справа сверху. Цветной, не тинтуется;
    в игре миникарту рисует MinimapTerrain.shader.
    """
    rng = np.random.default_rng(seed)
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32) / size

    def noise(scale):
        g = rng.standard_normal((scale + 1, scale + 1)).astype(np.float32)
        g = (g - g.min()) / (g.max() - g.min())
        im = Image.fromarray((g * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)
        return np.asarray(im, np.float32) / 255 - .5

    wx = noise(5) * .10 + noise(18) * .025
    wy = noise(5) * .10 + noise(18) * .025
    px, py = xs + wx, ys + wy
    seeds = np.array([(.18, .2), (.52, .16), (.2, .55), (.5, .45), (.8, .42), (.3, .85), (.62, .78), (.9, .85), (.82, .12)], np.float32)
    dist = np.stack([np.hypot(px - sx, py - sy) for sx, sy in seeds])
    srt = np.sort(dist, axis=0)
    arg = np.argsort(dist, axis=0)
    idx, second = arg[0], arg[1]
    edge = np.clip(1 - (srt[1] - srt[0]) * size / 3.2, 0, 1) ** .8
    # Часть границ — пунктиром.
    dashed = (idx * 7 + second * 3) % 3 == 0
    phase = np.sin((xs + ys) * size / 5.5) > 0
    edge = np.where(dashed & ~phase, 0, edge)
    base = np.array([21, 27, 35], np.float32)
    img = base + (noise(9) * 12 + (idx % 3 - 1) * 3)[..., None]
    # Река: извилистая полоса справа сверху.
    # Сверху вниз, затем уходит за правый край.
    rx = .74 + np.sin(ys * 7 + .6) * .05 + wx * .5 + np.clip(ys - .38, 0, 1) ** 1.5 * 2.2
    river = np.clip(1 - np.abs(xs - rx) / (.07 + ys * .05), 0, 1)
    water = np.clip(river * 6, 0, 1)
    img = img * (1 - water[..., None]) + (np.array([26, 46, 60], np.float32) + noise(12)[..., None] * 10) * water[..., None]
    shore = np.clip(1 - np.abs(river - .12) / .06, 0, 1) * (river > 0)
    edge = np.maximum(edge * (1 - water), shore * .8)
    img = img + edge[..., None] * (np.array([150, 162, 180], np.float32) - img) * .7
    out = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGB").convert("RGBA")
    out.save(os.path.join(OUT, name + ".png"))
    slices.append((name, 0, 0, 0, 0))


def build():
    os.makedirs(OUT, exist_ok=True)
    # Панели, ячейки, кнопки, карточки: одна форма, слоями.
    rrect_fill("wc_fill")
    rrect_metal("wc_frame", METAL)
    rrect_metal("wc_frame_bold", METAL_BOLD)
    top_highlight("wc_highlight")
    bottom_shade("wc_shade")
    glow("wc_glow")
    # Малый радиус: ячейки, клавиши, строки списка, флажок.
    rrect_fill("wc_fill_s", 64, 64, R_S)
    rrect_metal("wc_frame_s", METAL, 80, 80, R_S)
    rrect_metal("wc_frame_bold_s", METAL_BOLD, 80, 80, R_S)
    top_highlight("wc_highlight_s", 64, 96, R_S)
    bottom_shade("wc_shade_s", 64, 96, R_S)
    glow("wc_glow_s", r=R_S)
    inner_glow("wc_inner_glow_s", r=R_S)
    # Свечение кнопки-капсулы: прямоугольное давало ореол на углах вокруг капсулы.
    glow("wc_button_glow", r=56, inner=(144, 112))
    inner_glow("wc_inner_glow")
    dashed_frame("wc_frame_dashed", r=R_S * 2)
    grain("wc_grain")
    # Капсулы — по спрайту на каждую стандартную высоту: радиус равен половине
    # высоты, поэтому концы всегда полукруглые, а проволока одной толщины.
    # (Одна капсула на все высоты давала прямоугольник со скруглением 6.)
    # Высоты в единицах Canvas: кнопка 56, плашка значения 40, ярлык 30, полоса 14.
    for name, h in (("wc_button", 56), ("wc_pill", 40), ("wc_tag", 30)):
        capsule_fill(name + "_fill", h * 2, h * 2 + 32)
        capsule_metal(name + "_frame", h * 2, h * 2 + 32)
    capsule_fill("wc_bar_fill", 28, 88)
    capsule_metal("wc_bar_frame", 28, 88, 2.5)
    # Крупная полоса (здоровье героя 20, босс 26): свой спрайт, чтобы концы оставались полукругом.
    capsule_fill("wc_bar_fill_l", 52, 136)
    capsule_metal("wc_bar_frame_l", 52, 136, 3)
    # Круги: портрет, радио, ручка, баффы, кольцо перезарядки.
    circle("wc_circle_fill")
    circle_metal("wc_circle_frame", 128, METAL)
    circle_metal("wc_circle_frame_bold", 128, METAL_BOLD)
    circle("wc_circle_ring", stroke=8)
    # Кольцо портрета: крупный спрайт, чтобы проволока была тонкой (2,5 единицы на 200).
    circle_metal("wc_circle_frame_l", 400, 5)
    knob("wc_knob")
    # Ромбы гранёные: концы линий, маркер пункта, камень редкости.
    gem_cut("wc_diamond_s", 14)
    gem_cut("wc_diamond_m", 20)
    gem_cut("wc_diamond_l", 30)
    gem_cut("wc_gem", 40)
    # Рамка ромба не тянется 9-slice — два размера, чтобы проволока была одной толщины.
    diamond_metal("wc_diamond_frame", 96)
    diamond_metal("wc_diamond_frame_s", 44)
    diamond("wc_diamond_fill", 96)
    # Значки.
    stroke_glyph("wc_cross", 24, [[(7, 7), (17, 17)], [(17, 7), (7, 17)]], 2.5)
    stroke_glyph("wc_check", 32, [[(7, 17), (13, 23), (25, 9)]], 3.5)
    stroke_glyph("wc_plus", 48, [[(24, 10), (24, 38)], [(10, 24), (38, 24)]], 3)
    stroke_glyph("wc_chevron_down", 32, [[(8, 12), (16, 20), (24, 12)]], 3)
    stroke_glyph("wc_chevron_right", 32, [[(12, 8), (20, 16), (12, 24)]], 3)
    tri_glyph("wc_tri_up", 20, True)
    tri_glyph("wc_tri_down", 20, False)
    # Вуали и сплошной пиксель для линий, подчёркиваний, заполнения полос.
    veil_radial("wc_veil_radial")
    veil_linear("wc_veil_linear")
    solid("wc_px")
    # Боевой HUD.
    frame_open_top("wc_frame_open")
    ring_ticks("wc_ring_ticks")
    ring_glow("wc_ring_glow")
    blob("wc_blob")
    haze("wc_haze")
    spark("wc_spark")
    arrow("wc_arrow")
    pointer("wc_pointer")
    mouse("wc_mouse")
    stat_glyphs()
    map_sample("wc_map_sample")

    with open(os.path.join(OUT, "slices.txt"), "w", encoding="utf-8", newline="\n") as f:
        for name, l, b, r, t in slices:
            f.write(f"{name} {l} {b} {r} {t} {PPU}\n")
    print(f"watercolor: {len(slices)} sprites -> {OUT}")


# ---------------------------------------------------------------- проверка стыков
def load(name, tint=(255, 255, 255), alpha=1.0):
    im = Image.open(os.path.join(OUT, name + ".png")).convert("RGBA")
    a = np.asarray(im, np.float32)
    a[..., :3] = a[..., :3] * np.array(tint, np.float32) / 255
    a[..., 3] *= alpha
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def border_of(name):
    for s in slices:
        if s[0] == name: return s[1:]
    return (0, 0, 0, 0)


def sliced(name, w, h, tint=(255, 255, 255), alpha=1.0):
    """9-slice как в Unity (в пикселях спрайта)."""
    src = load(name, tint, alpha); l, b, r, t = border_of(name)
    sw, sh = src.size
    k = min(1.0, w / max(1, l + r), h / max(1, t + b))  # Unity ужимает края, если элемент меньше суммы границ
    L, B, Rr, T = [int(round(v * k)) for v in (l, b, r, t)]
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    xs = [(0, l, 0, L), (l, sw - r, L, w - Rr), (sw - r, sw, w - Rr, w)]
    ys = [(0, t, 0, T), (t, sh - b, T, h - B), (sh - b, sh, h - B, h)]
    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if sx1 <= sx0 or sy1 <= sy0 or dx1 <= dx0 or dy1 <= dy0: continue
            out.alpha_composite(src.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.BILINEAR), (dx0, dy0))
    return out


def preview():
    """Образцы из готовых спрайтов, собранные как в Unity. Смотреть стыки, а не красоту."""
    os.makedirs(PREVIEW, exist_ok=True)
    INK = (8, 10, 14); PANEL = (22, 25, 31); CREAM = (244, 241, 234); ACC = (255, 138, 76)
    GREY = (181, 178, 170); RARE = (111, 227, 211); LAV = (255, 154, 60); HP = (226, 84, 74)
    img = Image.new("RGBA", (1600, 900), INK + (255,))

    def put(im, x, y): img.alpha_composite(im, (x, y))

    # Окно: заливка + свет сверху + рамка.
    put(sliced("wc_fill", 560, 360, PANEL, .9), 40, 40)
    put(sliced("wc_highlight", 560, 360, CREAM, .06), 40, 40)
    put(sliced("wc_frame", 560, 360, CREAM, .55), 40, 40)
    # Разделитель с ромбами.
    put(sliced("wc_px", 400, 2, CREAM, .6), 120, 130)
    for x in (120, 520): put(load("wc_diamond_s", CREAM, .8), x - 6, 131 - 6)
    put(load("wc_diamond_m", CREAM), 320 - 9, 131 - 9)
    # Кнопки: основная (оранжевая), вторичная (рамка), недоступная.
    put(sliced("wc_fill", 220, 64, ACC), 80, 200); put(sliced("wc_highlight", 220, 64, CREAM, .25), 80, 200)
    put(sliced("wc_fill", 220, 64, PANEL, .6), 340, 200); put(sliced("wc_frame", 220, 64, CREAM, .6), 340, 200)
    put(sliced("wc_fill", 220, 64, (60, 64, 72), .5), 80, 290); put(sliced("wc_frame", 220, 64, CREAM, .18), 80, 290)
    # Плашки редкости.
    put(sliced("wc_pill_fill", 150, 44, GREY, .12), 340, 300); put(sliced("wc_pill_frame", 150, 44, GREY, .9), 340, 300)
    # Ячейки: пустая, обычная, редкая (свечение, подсветка, камень), выбранная.
    cx = 660
    put(load("wc_frame_dashed", CREAM, .4), cx, 60)
    for i, (edge, rare, sel) in enumerate([(GREY, False, False), (RARE, True, False), (GREY, False, True)]):
        x = cx + (i + 1) * 210
        if rare: put(sliced("wc_glow", 176 + 96, 176 + 96, RARE, .8), x - 48, 60 - 48)
        put(sliced("wc_fill", 176, 176, PANEL, .92), x, 60)
        if rare: put(sliced("wc_inner_glow", 176, 176, RARE, .35), x, 60)
        put(sliced("wc_frame_bold" if rare else "wc_frame", 176, 176, edge, 1), x, 60)
        if rare: put(load("wc_gem", RARE), x + 88 - 20, 60 - 20)
        if sel: put(sliced("wc_frame_bold", 176 + 12, 176 + 12, ACC), x - 6, 60 - 6)
    # Карточка редкого улучшения: рамка целиком в цвете, камень на верхней кромке.
    put(sliced("wc_glow", 700 + 96, 200 + 96, RARE, .9), 660 - 48, 300 - 48)
    put(sliced("wc_fill", 700, 200, PANEL, .95), 660, 300)
    put(sliced("wc_highlight", 700, 200, CREAM, .06), 660, 300)
    put(sliced("wc_frame_bold", 700, 200, RARE), 660, 300)
    put(sliced("wc_px", 120, 2, RARE), 660 + 350 - 130, 301); put(sliced("wc_px", 120, 2, RARE), 660 + 350 + 10, 301)
    put(load("wc_gem", RARE), 660 + 350 - 20, 300 - 20)
    put(sliced("wc_pill_fill", 150, 44, RARE, .14), 860, 380); put(sliced("wc_pill_frame", 150, 44, RARE), 860, 380)
    put(sliced("wc_circle_fill", 140, 140, (14, 40, 44)), 690, 330); put(load("wc_circle_frame_bold", RARE), 690 + 6, 330 + 6)
    # Полосы: здоровье, лавидий.
    for i, col in enumerate((HP, LAV)):
        y = 600 + i * 50
        put(sliced("wc_bar_fill", 480, 16, (40, 44, 52), .9), 80, y)
        put(sliced("wc_bar_fill", 330, 16, col), 80, y)
        put(sliced("wc_bar_frame", 480, 16, CREAM, .35), 80, y)
    # Слайдер с камнем, переключатель, флажок, радио.
    put(sliced("wc_bar_fill", 400, 12, (40, 44, 52), .9), 80, 740); put(sliced("wc_bar_fill", 260, 12, ACC), 80, 740)
    put(load("wc_gem", CREAM), 80 + 260 - 20, 746 - 20)
    put(sliced("wc_pill_fill", 112, 56, ACC), 560, 600); put(sliced("wc_circle_fill", 44, 44, CREAM), 560 + 112 - 50, 606)
    put(sliced("wc_pill_fill", 112, 56, (40, 44, 52)), 560, 680); put(sliced("wc_pill_frame", 112, 56, CREAM, .35), 560, 680)
    put(sliced("wc_circle_fill", 44, 44, (150, 150, 150)), 566, 686)
    put(sliced("wc_fill", 48, 48, ACC), 720, 600); put(load("wc_check", INK), 720 + 8, 600 + 8)
    put(sliced("wc_frame", 48, 48, CREAM, .6), 720, 680)
    put(load("wc_circle_frame_bold", ACC).resize((48, 48), Image.LANCZOS), 800, 600)
    put(load("wc_circle_fill", ACC).resize((24, 24), Image.LANCZOS), 812, 612)
    # Бейдж уровня: двойной ромб.
    put(load("wc_diamond_fill", PANEL), 900, 580); put(load("wc_diamond_frame", RARE), 900, 580)
    put(load("wc_diamond_frame", RARE, .5).resize((80, 80), Image.LANCZOS), 908, 588)
    # Закрыть: ромб-рамка с крестом.
    put(load("wc_diamond_frame_s", CREAM, .7), 1040, 600); put(load("wc_cross", CREAM), 1040 + 10, 600 + 10)
    put(load("wc_tri_up", (120, 220, 140)), 1120, 610); put(load("wc_tri_down", HP), 1150, 610)
    # Вуаль экрана на полосе справа.
    put(sliced("wc_veil_radial", 360, 260, INK, .9), 1200, 600)
    img.convert("RGB").save(os.path.join(PREVIEW, "watercolor-preview.png"))
    print("preview ->", os.path.join(PREVIEW, "watercolor-preview.png"))


if __name__ == "__main__":
    build()
    preview()
