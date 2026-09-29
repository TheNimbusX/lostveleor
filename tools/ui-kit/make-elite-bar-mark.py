"""Рога на концах полосы элиты (владелец 29.09, выбор «5 — Рога»: полоса прежняя, мазок «Дыма и
света», но у ЭЛИТЫ оба конца вырастают в костяные рога, загнутые вверх; заливка тёмно-алая; огонёк на
конце заливки; цифры «1240 / 2000»; маленький рогатый череп слева убран — владелец его отверг).
Концепт: ART/characters/act-1-enemies/review/mobs-v2-round2-2026-09-29/elite-bar/05-elite-bar-antler-ends*.

  EliteBarAntler.png — левый рог: цветной рисунок с альфой. Правый — тот же спрайт, отражённый
                       в HealthBars (отрицательный масштаб по x вокруг точки крепления).

Исходник — ART/UI/elite-bar-2026-09-29/antler-left-magenta.png: Higgsfield, gpt_image_2_5 high 2k,
image-to-image по вырезу левого рога из концепта 5 (ref-concept-05-left-antler.png), рог отдельно на
ровном пурпурном фоне (задание 29.09 e650b9db-e89d-4d2d-822b-9a01246cb3d3). Там же два отвергнутых
варианта (alt-*): на зелёном — рог плоский и бурый, «прозрачный» — тонкий контур, в 45 пикселях теряется.

Что делает скрипт:
  * вырезает рог из пурпурного фона: альфа по «пурпурности» min(R, B) − G, цвет краёв — обратным
    смешиванием с цветом фона (без пурпурной каймы); отдельные крошки фона выкидываются;
  * чуть высветляет кость к «костяному белому» (кадр игры приглушает светлое на ~8 %);
  * обводит рог чернильной каймой цвета дорожки: в бою рог ≈ 45 пикселей при 1080p, нарисованный
    контур там тоньше пикселя — без каймы рог тает на светлой траве и земле;
  * уменьшает до TEXTURE_HEIGHT (≈ вдвое больше, чем на экране при 1080p: без мип-карт это ещё чисто,
    а при 4K — один к одному), под прозрачным — цвет ближайшего края (билинейный фильтр не тянет чёрное);
  * печатает раскладку для HealthBars: точку крепления (центр основания рога, туда упирается конец
    полосы) и сколько рога ниже неё — числа повторены в HealthBars (AntlerPivot, AntlerBelow).

Запуск: python tools/ui-kit/make-elite-bar-mark.py [папка]  (без папки — прямо в Resources/UI/HUD)
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "ART", "UI", "elite-bar-2026-09-29", "antler-left-magenta.png")
OUT = os.path.join(ROOT, "razlom", "Assets", "Resources", "UI", "HUD")

# Высота холста в пикселях (кратна 4 — сжатие BC7/DXT5 без запасного RGBA32). На экране при 1080p
# рог ≈ 45 пикселей (HealthBars.EliteAntler 0,52 м ≈ 87 пикселей на метр).
TEXTURE_HEIGHT = 96
PAD = 3  # прозрачная рамка итогового холста, пиксели
# К «костяному белому»: насыщенность (доля цвета против серого той же яркости) и высветление 1 − (1 − c)·LIFT.
# У исходника кость жёлто-охристая, в концепте — бледная, почти белая.
SATURATION = .72
LIFT = .84
# Чернильная кайма: толщина и мягкость в пикселях ИТОГОВОГО холста, цвет — дым дорожки (BackColor).
RIM = 1.3
RIM_SOFT = .7
INK = np.array([0x12, 0x10, 0x12], np.float32) / 255.0
INK_ALPHA = .92


def key_magenta(rgb):
    """Альфа и чистый цвет рога на ровном пурпурном фоне."""
    h, w, _ = rgb.shape
    corners = np.concatenate([rgb[:16, :16].reshape(-1, 3), rgb[:16, -16:].reshape(-1, 3),
                              rgb[-16:, :16].reshape(-1, 3), rgb[-16:, -16:].reshape(-1, 3)])
    back = np.median(corners, 0)
    key = np.minimum(rgb[:, :, 0], rgb[:, :, 2]) - rgb[:, :, 1]
    back_key = min(back[0], back[2]) - back[1]
    # Контур рога — тёмно-бурый, его «пурпурность» около −0,05: между ним и фоном альфа линейна.
    # Фон не совсем ровный (к углам темнее, «пурпурность» до 0,8 от фоновой) — он весь прозрачный.
    edge = back_key * .85
    alpha = np.clip((edge - key) / (edge + .05), 0.0, 1.0)
    # Только сам рог: самая большая плотная связная часть и её мягкий край.
    labels, count = ndimage.label(alpha > .5)
    sizes = ndimage.sum(np.ones_like(alpha), labels, np.arange(1, count + 1))
    body = labels == (np.argmax(sizes) + 1)
    alpha *= ndimage.binary_dilation(body, iterations=4)
    # Цвет без фона: пиксель = цвет·a + фон·(1 − a).
    a = alpha[:, :, None]
    color = np.clip((rgb - (1.0 - a) * back) / np.maximum(a, 1e-3), 0.0, 1.0)
    return color, alpha


def attach_point(alpha):
    """
    Точка крепления: центр основания (тупой срез рога справа внизу) — средняя точка плотных пикселей
    в правых 5 % рога по высоте.
    """
    ys, xs = np.nonzero(alpha > .5)
    height = ys.max() - ys.min()
    sel = xs >= xs.max() - .05 * height
    return float(xs[sel].mean()), float(ys[sel].mean())


def rim(alpha, width, soft):
    """Кайма: силуэт, раздутый на width и размытый на soft (в пикселях этого холста)."""
    grown = ndimage.distance_transform_edt(alpha < .5)
    ring = np.clip(width + .5 - grown, 0.0, 1.0)
    return np.clip(ndimage.gaussian_filter(np.maximum(ring, alpha), soft), 0.0, 1.0)


def resize(rgb, alpha, width, height):
    """Уменьшение с домноженной альфой: светлый край не тянет за собой цвет прозрачного."""
    pre = rgb * alpha[:, :, None]
    planes = [np.asarray(Image.fromarray(p.astype(np.float32), "F").resize((width, height), Image.LANCZOS))
              for p in [pre[:, :, 0], pre[:, :, 1], pre[:, :, 2], alpha]]
    a = np.clip(planes[3], 0.0, 1.0)
    rgb = np.clip(np.dstack(planes[:3]) / np.maximum(a, 1e-4)[:, :, None], 0.0, 1.0)
    return rgb, a


def bleed(rgb, alpha):
    """Под прозрачным — цвет ближайшего видимого пикселя."""
    empty = alpha < 1.0 / 255.0
    if empty.all() or not empty.any():
        return rgb
    _, (iy, ix) = ndimage.distance_transform_edt(empty, return_indices=True)
    out = rgb.copy()
    out[empty] = rgb[iy[empty], ix[empty]]
    return out


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else OUT
    os.makedirs(out, exist_ok=True)
    rgb = np.asarray(Image.open(SRC).convert("RGB")).astype(np.float32) / 255.0
    color, alpha = key_magenta(rgb)
    grey = (color * np.array([.299, .587, .114], np.float32)).sum(2, keepdims=True)
    color = grey + (color - grey) * SATURATION
    color = 1.0 - (1.0 - color) * LIFT
    ax, ay = attach_point(alpha)

    # Рамка рисунка в исходнике и масштаб до итогового холста.
    ys, xs = np.nonzero(alpha > .02)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    inner = TEXTURE_HEIGHT - 2 * PAD - 2 * int(np.ceil(RIM + 2 * RIM_SOFT))
    scale = inner / float(y1 - y0)
    margin = int(np.ceil((PAD + RIM + 2 * RIM_SOFT) / scale))
    x0, y0 = max(0, x0 - margin), max(0, y0 - margin)
    x1, y1 = min(rgb.shape[1], x1 + margin), min(rgb.shape[0], y1 + margin)
    height = TEXTURE_HEIGHT
    width = int(np.ceil((x1 - x0) * height / float(y1 - y0) / 4.0)) * 4
    # Холст шире рисунка на округление: рисунок — по центру.
    extra = int(round((width * (y1 - y0) / float(height) - (x1 - x0)) * .5))
    x0 -= extra
    x1 = x0 + int(round(width * (y1 - y0) / float(height)))
    pad_l, pad_r = max(0, -x0), max(0, x1 - rgb.shape[1])
    color = np.pad(color, ((0, 0), (pad_l, pad_r), (0, 0)), mode="edge")
    alpha = np.pad(alpha, ((0, 0), (pad_l, pad_r)))
    x0, x1, ax = x0 + pad_l, x1 + pad_l, ax + pad_l
    color, alpha = resize(color[y0:y1, x0:x1], alpha[y0:y1, x0:x1], width, height)
    ax, ay = (ax - x0) * width / float(x1 - x0), (ay - y0) * height / float(y1 - y0)

    # Кайма под рогом, рог поверх неё.
    ring = rim(alpha, RIM, RIM_SOFT) * INK_ALPHA
    total = alpha + ring * (1.0 - alpha)
    rgb_out = (color * alpha[:, :, None] + INK * (ring * (1.0 - alpha))[:, :, None]) / np.maximum(total, 1e-4)[:, :, None]
    rgb_out = bleed(np.clip(rgb_out, 0.0, 1.0), total)

    path = os.path.join(out, "EliteBarAntler.png")
    rgba = np.dstack([rgb_out, total])
    Image.fromarray((np.clip(rgba, 0.0, 1.0) * 255 + .5).astype(np.uint8), "RGBA").save(path)
    print(path, width, "x", height)

    # Раскладка для HealthBars: всё — доли холста, y — снизу (как pivot спрайта в Unity).
    solid = np.nonzero(total > .35)
    pivot = (ax / width, 1.0 - ay / height)
    below = (solid[0].max() + 1 - ay) / height
    above = (ay - solid[0].min()) / height
    outward = (ax - solid[1].min()) / height
    print("AntlerPivot = (%.3f, %.3f); AntlerBelow = %.3f; выше точки %.3f; наружу %.3f (доли высоты); стороны %.3f"
          % (pivot[0], pivot[1], below, above, outward, width / float(height)))


if __name__ == "__main__":
    main()
