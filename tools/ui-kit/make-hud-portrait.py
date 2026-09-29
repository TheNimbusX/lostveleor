"""Портрет Пелага в боевом HUD без каймы и его мягкая маска (владелец 29.09: «кайма вокруг волос,
рваный край круга, шов посередине» — чинить все три).

  PelagPortraitPaintedCutout.png — тот же вырез, но без каймы. Вырез когда-то сняли с рисунка на
      тёмно-синем фоне с красным ореолом за головой: полупрозрачные пиксели края (≈20 тысяч) и первый
      ряд непрозрачных несут цвет этого фона, и в игре волосы и повязка обведены тёмно-красной каймой.
      Скрипт перекрашивает край в цвет соседних «чистых» пикселей (на 2 пикселя глубже края), под
      прозрачным кладёт цвет ближайшего края (мип-уровни и билинейный фильтр не тянут синий фон),
      выкидывает отдельные крошки ореола с альфой 1–2 и доводит почти непрозрачное тело
      (альфа 250–254) до полной непрозрачности. Форма выреза и сам рисунок не меняются.
      Повторный запуск на готовом файле ничего не меняет. Старая картинка остаётся в git.

  PelagPortraitMask.png — мягкая маска для шейдера Razlom/UI Ink (INK_SHAPE, _ShapeTex) в
      координатах прямоугольника рисунка: ниже середины круга — круг портрета со сглаженным краем
      (≈1 единица холста), выше — всё (голова выходит за круг, 26 сентября). Раньше это делали
      стенсил-маска диска (край без сглаживания — «лесенка») и две копии рисунка под RectMask2D
      (стык на высоте челюсти — «шов»); теперь рисунок один (CombatHudWcBuilder.SoftPortrait).

Раскладка — как в префабе CombatHudWc (CombatHudWcBuilder.cs, единицы холста 1920×1080): круг
портрета 132 с началом в левом нижнем углу узла «Портрет»; рисунок 132 × 1,18 = 155,76, центр
(66; 66 + 16). Поменялась раскладка — поменять ART_* и CIRCLE_* здесь и перезапустить скрипт.

Запуск: python tools/ui-kit/make-hud-portrait.py [--preview папка]
  --preview — кадры портрета на тёмном диске при масштабе интерфейса 80/100/120 % (1080p), было и
  стало рядом и увеличенно: проверить кайму, край и стык без запуска игры.
"""
import argparse
import os

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HUD = os.path.join(ROOT, "razlom", "Assets", "Resources", "UI", "HUD")
PORTRAIT = os.path.join(HUD, "PelagPortraitPaintedCutout.png")
MASK = os.path.join(HUD, "PelagPortraitMask.png")

# Раскладка префаба, единицы холста от левого нижнего угла узла «Портрет».
CIRCLE_CENTRE = (66.0, 66.0)
CIRCLE_RADIUS = 66.0
ART_CENTRE = (66.0, 82.0)
ART_SIZE = 155.76
# Край круга маски чуть внутри края диска: плечи не свисают за тёмный диск ни на пиксель.
CIRCLE_INSET = .3
# Ширина сглаживания края круга, единицы холста (≈1 пиксель при 1080p и масштабе 100 %).
EDGE = 1.1
# Выше середины круга рисунок виден целиком; переход — мягкий (вне круга там рисунка нет).
HEAD_SOFT = 6.0
MASK_SIZE = 512

OPAQUE = 250          # альфа тела выреза 250–254 — это «непрозрачно»
CLEAN_DEPTH = 1.5     # «чистые» пиксели — глубже края больше чем на столько пикселей
REMNANT_ALPHA = 24    # крошки ореола: альфа ниже этой…
REMNANT_REACH = 2.5   # …и дальше этого от уверенного края (альфа ≥ 128)


def defringe(rgba):
    rgb = rgba[..., :3].astype(np.float32)
    alpha = rgba[..., 3].astype(np.int32)

    # Крошки ореола: всё, что не связано с телом, и бледные пятна дальше двух пикселей от края.
    labels, count = ndimage.label(alpha > 0, structure=np.ones((3, 3)))
    if count > 1:
        sizes = ndimage.sum(np.ones_like(alpha), labels, np.arange(1, count + 1))
        alpha[labels != 1 + int(np.argmax(sizes))] = 0
    reach = ndimage.distance_transform_edt(alpha < 128)
    alpha[(alpha < REMNANT_ALPHA) & (reach > REMNANT_REACH)] = 0

    alpha[alpha >= OPAQUE] = 255
    solid = alpha == 255
    clean = solid & (ndimage.distance_transform_edt(solid) > CLEAN_DEPTH)

    # Цвет края — среднее чистых соседей (нормированное размытие), дальше — цвет ближайшего чистого.
    weight = ndimage.gaussian_filter(clean.astype(np.float32), 1.2)
    local = np.stack([ndimage.gaussian_filter(rgb[..., c] * clean, 1.2) for c in range(3)], -1)
    local /= np.maximum(weight, 1e-6)[..., None]
    _, (iy, ix) = ndimage.distance_transform_edt(~clean, return_indices=True)
    nearest = rgb[iy, ix]
    fore = np.where((weight > .05)[..., None], local, nearest)

    band = ~clean & (alpha > 0)
    out = rgb.copy()
    out[band] = fore[band]
    out[alpha == 0] = nearest[alpha == 0]
    result = np.dstack([np.clip(out + .5, 0, 255), alpha]).astype(np.uint8)
    return result, int(band.sum())


def mask():
    n = MASK_SIZE
    # Центры текселей в единицах холста.
    u = (np.arange(n) + .5) / n
    x = ART_CENTRE[0] - ART_SIZE * .5 + u * ART_SIZE
    y = ART_CENTRE[1] - ART_SIZE * .5 + u * ART_SIZE
    xx, yy = np.meshgrid(x, y)
    d = np.hypot(xx - CIRCLE_CENTRE[0], yy - CIRCLE_CENTRE[1])
    circle = np.clip((CIRCLE_RADIUS - CIRCLE_INSET - d) / EDGE + .5, 0, 1)
    head = np.clip((yy - CIRCLE_CENTRE[1]) / HEAD_SOFT + .5, 0, 1)
    a = np.maximum(circle, head)
    # Строки PNG идут сверху вниз, а v текстуры — снизу вверх.
    a = a[::-1]
    img = np.zeros((n, n, 4), np.uint8)
    img[..., :3] = 255
    img[..., 3] = np.round(a * 255).astype(np.uint8)
    return img


# ---------------------------------------------------------------- предпросмотр

DISK = np.array([4, 6, 10], np.float32) / 255      # UiTheme.SmokeDeep
WORLD = np.array([104, 118, 86], np.float32) / 255  # светлая лесная земля: на ней видна кайма


def resample(img, px):
    # Уменьшение ступенями по 2 (как мип-уровни) и последний шаг — билинейно: похоже на трилинейную выборку.
    im = Image.fromarray(img)
    while im.width >= px * 2:
        im = im.resize((im.width // 2, im.height // 2), Image.BOX)
    return np.asarray(im.resize((px, px), Image.BILINEAR)).astype(np.float32) / 255


def frame(art, soft, scale, zoom):
    """Портрет на диске поверх «земли» при масштабе интерфейса scale; zoom — увеличение для глаза."""
    unit = scale  # пикселей на единицу холста при 1080p
    size = int(round(190 * unit))
    ox, oy = 30 * unit, 10 * unit  # левый нижний угол узла «Портрет» в кадре
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32) + .5
    cx, cy = (xs - ox) / unit, (size - ys - oy) / unit
    canvas = np.ones((size, size, 3), np.float32) * WORLD
    d = np.hypot(cx - CIRCLE_CENTRE[0], cy - CIRCLE_CENTRE[1])
    disk = np.clip((CIRCLE_RADIUS - d) * unit + .5, 0, 1)[..., None]
    canvas = canvas * (1 - disk) + DISK * disk

    px = int(round(ART_SIZE * unit))
    tex = resample(art, px)
    left = int(round(ox + (ART_CENTRE[0] - ART_SIZE / 2) * unit))
    top = int(round(size - oy - (ART_CENTRE[1] + ART_SIZE / 2) * unit))
    region = np.zeros((size, size, 4), np.float32)
    x0, y0 = max(left, 0), max(top, 0)
    x1, y1 = min(left + px, size), min(top + px, size)
    region[y0:y1, x0:x1] = tex[y0 - top:y1 - top, x0 - left:x1 - left]
    a = region[..., 3:4]
    if soft is None:
        # Было: низ под стенсилом диска (жёсткий край), верх — от середины круга.
        hard = ((d <= CIRCLE_RADIUS) | (cy >= CIRCLE_CENTRE[1])).astype(np.float32)[..., None]
        a = a * hard
    else:
        m = resample(soft, px)[..., 3:4]
        full = np.zeros((size, size, 1), np.float32)
        full[y0:y1, x0:x1] = m[y0 - top:y1 - top, x0 - left:x1 - left]
        a = a * full
    canvas = region[..., :3] * a + canvas * (1 - a)
    out = Image.fromarray(np.clip(canvas * 255 + .5, 0, 255).astype(np.uint8))
    return out.resize((size * zoom, size * zoom), Image.NEAREST) if zoom > 1 else out


def preview(folder, before, after, soft):
    os.makedirs(folder, exist_ok=True)
    for scale in (.8, 1.0, 1.2):
        a = frame(before, None, scale, 1)
        b = frame(after, soft, scale, 1)
        sheet = Image.new("RGB", (a.width * 2 + 10, a.height), (30, 30, 30))
        sheet.paste(a, (0, 0))
        sheet.paste(b, (a.width + 10, 0))
        sheet.save(os.path.join(folder, "portrait-%d.png" % round(scale * 100)))
    a = frame(before, None, 1.0, 4)
    b = frame(after, soft, 1.0, 4)
    sheet = Image.new("RGB", (a.width * 2 + 20, a.height), (30, 30, 30))
    sheet.paste(a, (0, 0))
    sheet.paste(b, (a.width + 20, 0))
    sheet.save(os.path.join(folder, "portrait-100-zoom4.png"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--preview", help="папка для кадров до/после")
    args = parser.parse_args()

    before = np.asarray(Image.open(PORTRAIT).convert("RGBA"))
    after, band = defringe(before.copy())
    Image.fromarray(after, "RGBA").save(PORTRAIT, optimize=True)
    soft = mask()
    Image.fromarray(soft, "RGBA").save(MASK, optimize=True)
    print("край перекрашен: %d пикселей → %s" % (band, os.path.relpath(PORTRAIT, ROOT)))
    print("маска %d² → %s" % (MASK_SIZE, os.path.relpath(MASK, ROOT)))
    if args.preview:
        preview(args.preview, before, after, soft)
        print("кадры → " + args.preview)


if __name__ == "__main__":
    main()
