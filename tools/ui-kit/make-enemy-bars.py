"""Полоска здоровья над врагами в материале «Дым и свет» (владелец 26 сентября: всё — на материал
боевого HUD). Полоску рисует SpriteRenderer в мире, шейдер «Дыма и света» там не работает, поэтому
дым и мазки заранее вырезаются из спрайтов пака Assets/UI/Kit/Smoke:

  EnemyBarTrack.png — тёмная дымная дорожка: мазок brush_stroke_1 в дымном ореоле. Холст 512×128,
                      плотная часть — TRACK_CORE (середина); HealthBars кладёт её ровно на Width×Height,
                      ореол выходит за полоску;
  EnemyBarFill.png  — заливка: мазок brush_stroke_2 ровной толщины, начало кисти слева, сухой отрыв
                      справа. Холст 512×64, плотная часть — FILL_CORE по высоте. HealthBars режет его
                      по доле здоровья, мазок не сжимается (как полоса героя в HUD);
  EnemyBarOrb.png   — огонёк элиты: мягкий круг диаметром ORB_DISC из 64, середина светлее (RGB);
  EnemyBarGlow.png  — сияние вокруг огонька: мягкое пятно на весь холст.

Все — белые маски (цвет даёт HealthBars). Стороны — степени двойки: импорт по умолчанию их не
масштабирует. Края прозрачные: повтор текстуры по умолчанию не протаскивает краску на другой край.
Числа раскладки повторены в HealthBars (TrackSpan*, OrbSpan) — меняются вместе.

Запуск: python tools/ui-kit/make-enemy-bars.py [папка]  (без папки — прямо в Resources/UI/HUD)
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
KIT = os.path.join(ROOT, "razlom", "Assets", "UI", "Kit", "Smoke")
OUT = os.path.join(ROOT, "razlom", "Assets", "Resources", "UI", "HUD")

# Холст дорожки и где на нём плотная часть мазка (x0, y0, ширина, высота; y — сверху).
TRACK_SIZE = (512, 128)
TRACK_CORE = (32, 32, 448, 64)
# Холст заливки; плотная часть — середина FILL_CORE по высоте.
FILL_SIZE = (512, 64)
FILL_CORE = 40
ORB_SIZE, ORB_DISC = 64, 44


def mask(name):
    rgba = np.asarray(Image.open(os.path.join(KIT, name + ".png")).convert("RGBA"))
    return rgba[:, :, 3].astype(np.float32) / 255.0


def resize(alpha, width, height):
    image = Image.fromarray(alpha.astype(np.float32), "F").resize((width, height), Image.LANCZOS)
    return np.clip(np.asarray(image), 0.0, 1.0)


def even_band(alpha, core, rows, smooth=40.0):
    """
    Мазок ровной толщины: каждый столбец сдвигается и растягивается так, чтобы плотная часть
    (альфа выше половины) шла одной полосой высотой <paramref core> по середине <paramref rows>
    строк. Середина и толщина сглажены вдоль мазка — щетина по краям остаётся своей, выравнивается
    только общий ход (у пака мазок к хвосту худеет вдвое и гуляет по высоте).
    """
    h, w = alpha.shape
    ys = np.arange(h, dtype=np.float32)[:, None]
    weight = np.clip(alpha - .5, 0.0, None) + 1e-4
    center = (weight * ys).sum(0) / weight.sum(0)
    thick = (alpha > .5).sum(0).astype(np.float32)
    center = ndimage.gaussian_filter1d(center, smooth)
    thick = np.maximum(ndimage.gaussian_filter1d(thick, smooth), 8.0)
    out = np.zeros((rows, w), np.float32)
    target = (np.arange(rows, dtype=np.float32) - (rows - 1) * .5)
    for x in range(w):
        # Строка выхода → строка источника: растяжка около середины столбца.
        src = center[x] + target * (thick[x] / core)
        out[:, x] = np.interp(src, np.arange(h), alpha[:, x], left=0.0, right=0.0)
    return out


def lift_off(alpha, length, seed):
    """
    Сухой отрыв кисти справа: каждая строка кончается в своём месте (щетина), середина мазка
    тянется дальше краёв; соседние строки похожи (шум сглажен поперёк).
    """
    h, w = alpha.shape
    rng = np.random.default_rng(seed)
    jitter = ndimage.gaussian_filter1d(rng.standard_normal(h), 1.2)
    jitter = (jitter - jitter.min()) / (np.ptp(jitter) + 1e-6)
    edge = np.abs(np.arange(h) - (h - 1) * .5) / (h * .5)
    end = w - 1 - length * (.15 + .45 * edge + .4 * jitter)
    x = np.arange(w, dtype=np.float32)[None, :]
    ramp = np.clip((end[:, None] - x) / (length * .3), 0.0, 1.0)
    return alpha * ramp ** 1.4


def lift_on(alpha, length, seed):
    """Сухое касание кисти слева — тот же отрыв, отражённый: концы дорожки одинаково рваные."""
    return lift_off(alpha[:, ::-1], length, seed)[:, ::-1]


def clean(alpha, core):
    """
    Без отдельных брызг: мелкие клочки и клочки далеко от середины полосы (у головы мазка пака
    над ним висят точки) убираются, щетина у края остаётся.
    """
    labels, count = ndimage.label(alpha > .08)
    if count == 0:
        return alpha
    index = np.arange(1, count + 1)
    area = ndimage.sum(np.ones_like(alpha), labels, index)
    rows = ndimage.center_of_mass(np.ones_like(alpha), labels, index)
    middle = (alpha.shape[0] - 1) * .5
    keep = np.zeros(count + 1, bool)
    for i, (a, (y, _)) in enumerate(zip(area, rows), start=1):
        keep[i] = a >= area.max() * .5 or (a >= 30 and abs(y - middle) < core * .75)
    # Мягкий край клочка (альфа ниже порога) идёт за своим клочком.
    grown = ndimage.grey_dilation(np.where(keep[labels], labels, 0), size=(3, 3))
    return alpha * (keep[labels] | (keep[grown] & (grown > 0)))


def edge_fade(alpha, width):
    h, w = alpha.shape
    ramp_y = np.clip(np.minimum(np.arange(h), np.arange(h)[::-1]) / float(width), 0.0, 1.0)
    ramp_x = np.clip(np.minimum(np.arange(w), np.arange(w)[::-1]) / float(width), 0.0, 1.0)
    return alpha * ramp_y[:, None] * ramp_x[None, :]


def save(alpha, name, shade=None):
    """Белая маска (или серая с объёмом <paramref shade>) с альфой; RGB и под прозрачным — не чёрный."""
    rgb = np.ones(alpha.shape + (3,), np.float32) if shade is None else np.repeat(shade[:, :, None], 3, 2)
    rgba = np.dstack([rgb, alpha])
    path = os.path.join(OUT, name + ".png")
    Image.fromarray((np.clip(rgba, 0.0, 1.0) * 255 + .5).astype(np.uint8), "RGBA").save(path)
    print(path)


def core_rows(alpha):
    """Строки, где мазок плотный: средняя по длине альфа выше половины."""
    rows = np.where(alpha[:, alpha.shape[1] // 5: alpha.shape[1] * 4 // 5].mean(1) > .5)[0]
    return int(rows.min()), int(rows.max()) + 1


def track():
    x0, y0, w, h = TRACK_CORE
    width, height = TRACK_SIZE
    # Голова кисти слева (у пака мазок начинается тонким остриём — оно отрезано), тело до худеющего хвоста.
    stroke = mask("brush_stroke_1")[:, 110:760]
    band = even_band(stroke, core=60.0, rows=110)
    band = resize(band, w, int(round(110 * h / 60.0)))
    band = clean(band, h)
    band = lift_on(lift_off(band, 26, seed=261), 22, seed=263)
    canvas = np.zeros((height, width), np.float32)
    top = y0 + h // 2 - band.shape[0] // 2
    canvas[top:top + band.shape[0], x0:x0 + w] = band
    # Дымный ореол: размытый мазок, шире и мягче, — дорожка тает в мир, а не режется краем.
    halo = ndimage.gaussian_filter(canvas, (9.0, 12.0)) * .75
    alpha = 1.0 - (1.0 - canvas) * (1.0 - halo)
    save(edge_fade(alpha, 6), "EnemyBarTrack")
    return alpha


def fill():
    width, height = FILL_SIZE
    stroke = mask("brush_stroke_2")[:, 96:700]
    band = even_band(stroke, core=60.0, rows=108)
    band = resize(band, width - 10, int(round(108 * FILL_CORE / 60.0)))
    # Холст ниже полосы со щетиной — лишние строки сверху и снизу срезаются (там почти ничего нет).
    cut = (band.shape[0] - height) // 2
    band = clean(band[cut:cut + height], FILL_CORE)
    band = lift_off(band, 30, seed=262)
    canvas = np.zeros((height, width), np.float32)
    canvas[:, 4:4 + band.shape[1]] = band
    save(edge_fade(canvas, 2), "EnemyBarFill")
    return canvas


def orb():
    size = ORB_SIZE
    c = (size - 1) * .5
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    r = np.hypot(x - c, y - c)
    radius = ORB_DISC * .5
    alpha = np.clip((radius - r) / 3.0 + .5, 0.0, 1.0)
    # Свет, а не шар: ярче всего середина, к краю краска гуще (RGB темнее — тон огонька).
    shade = np.clip(1.0 - .28 * (r / radius) ** 1.6, .72, 1.0)
    save(alpha, "EnemyBarOrb", shade)
    glow_alpha = np.exp(-(r / (size * .5) * 1.9) ** 2) * np.clip((size * .5 - r) / 5.0, 0.0, 1.0)
    save(glow_alpha, "EnemyBarGlow")


def main():
    global OUT
    if len(sys.argv) > 1:
        OUT = os.path.abspath(sys.argv[1])
    os.makedirs(OUT, exist_ok=True)
    track()
    fill()
    orb()


if __name__ == "__main__":
    main()
