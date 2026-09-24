"""
Рисованные детали пака «Ночная акварель» (проба 23 сентября 2026).

Владелец: код даёт ровность, но «бездушно», от эталона далеко по
художественности. Поэтому часть деталей берётся из генерации по эталонному
листу (ART/UI/kit-painted/raw, серебро на прозрачном фоне), а код только
вырезает и выравнивает их:

- рамка: берётся ОДИН угол (левый верхний) и зеркалится в четыре — все углы
  одинаковые, прямые участки не гуляют; середина 9-slice — прямая проволока;
- камень и ромбы: обрезка по форме, три размера;
- кнопка-капсула: высота ровно 56 единиц, края — полукруг.

Всё переводится в оттенки серого: цвет по-прежнему задаёт тема (серебро,
бирюза редкости, оранжевый кнопки), а блики и грани рисунка остаются.

Выход: razlom/Assets/UI/Kit/WatercolorPainted/*.png + slices.txt (200 px на единицу).
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RAW = os.path.join(ROOT, "ART", "UI", "kit-painted", "raw")
OUT = os.path.join(ROOT, "razlom", "Assets", "UI", "Kit", "WatercolorPainted")
PPU = 200
slices = []


def load(name):
    return np.asarray(Image.open(os.path.join(RAW, name + ".png")).convert("RGBA")).astype(np.float32)


def to_gray(rgba, haze=0.0, normalize=True):
    """Серый по яркости, альфа без «дымки» (генератор кладёт серую пелену на весь холст)."""
    rgb, a = rgba[..., :3], rgba[..., 3]
    lum = rgb[..., 0] * .3 + rgb[..., 1] * .55 + rgb[..., 2] * .15
    if normalize:
        top = np.percentile(lum[a > 200], 99) if (a > 200).any() else 255
        lum = np.clip(lum / max(top, 1) * 255, 0, 255)
    if haze > 0:
        a = np.clip((a - haze) / (255 - haze) * 255, 0, 255)
    return np.dstack([lum, lum, lum, a]).astype(np.uint8)


def save(name, img, border=(0, 0, 0, 0)):
    Image.fromarray(img, "RGBA").save(os.path.join(OUT, name + ".png"))
    slices.append((name,) + tuple(int(b) for b in border))


def crop_alpha(rgba, thr=10):
    ys, xs = np.where(rgba[..., 3] > thr)
    return rgba[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def resize(img, w, h):
    return np.asarray(Image.fromarray(img, "RGBA").resize((w, h), Image.LANCZOS))


def frame():
    """Рамка: левый верхний угол, отзеркаленный в четыре. Проволока 3 px (1,5 единицы), ореол ~10 единиц."""
    src = load("frame-a")
    a = src[..., 3]
    H, W = a.shape
    line_y = int(a[:400, W // 2].argmax())        # верхняя кромка
    line_x = int(a[H // 2, :400].argmax())        # левая кромка
    thick = int((a[:400, W // 2] > 128).sum())
    halo = min(150, line_x, line_y) - 2           # ореол наружу, в пикселях исходника (сколько есть до края)
    inner = 170                                   # внутрь: скругление угла и блик
    q = src[line_y - halo:line_y + inner, line_x - halo:line_x + inner]
    q = to_gray(q, haze=26)
    scale = 3.0 / thick
    n = max(8, int(round(q.shape[0] * scale)))
    q = resize(q, n, n)
    top = np.concatenate([q, q[:, ::-1]], axis=1)
    full = np.concatenate([top, top[::-1, :]], axis=0)
    border = n - 2
    save("wcp_frame", full, (border, border, border, border))
    line_px = halo * scale
    return line_px


def frame_bold(line_px):
    """Жирная рамка редкости: та же проволока, утолщённая размытием-максимумом альфы."""
    img = np.asarray(Image.open(os.path.join(OUT, "wcp_frame.png")).convert("RGBA")).astype(np.float32)
    from PIL import ImageFilter
    a = Image.fromarray(img[..., 3].astype(np.uint8)).filter(ImageFilter.MaxFilter(3))
    img[..., 3] = np.maximum(img[..., 3], np.asarray(a, np.float32) * .9)
    n = img.shape[0] // 2
    save("wcp_frame_bold", img.astype(np.uint8), (n - 2,) * 4)


def gems():
    g = crop_alpha(to_gray(load("gem"), haze=8))
    h, w = g.shape[:2]
    side = max(h, w)
    pad = np.zeros((side, side, 4), np.uint8)
    pad[(side - h) // 2:(side - h) // 2 + h, (side - w) // 2:(side - w) // 2 + w] = g
    for name, px in (("wcp_gem", 80), ("wcp_diamond_l", 36), ("wcp_diamond_m", 24), ("wcp_diamond_s", 16)):
        save(name, resize(pad, px, px))


def button():
    """Кнопка-капсула высотой 56 единиц (112 px): концы полукругом, середина тянется."""
    b = crop_alpha(to_gray(load("button"), haze=10))
    h, w = b.shape[:2]
    th = 112
    tw = int(round(w * th / h))
    b = resize(b, tw, th)
    save("wcp_button", b, (th // 2, th // 2 - 2, th // 2, th // 2 - 2))


def build():
    os.makedirs(OUT, exist_ok=True)
    line_px = frame()
    frame_bold(line_px)
    gems()
    button()
    with open(os.path.join(OUT, "slices.txt"), "w", encoding="utf-8", newline="\n") as f:
        for name, l, b, r, t in slices:
            f.write(f"{name} {l} {b} {r} {t} {PPU}\n")
    # Насколько слой рамки шире элемента: проволока ложится внешним краем на край элемента.
    expand = (line_px - 1.5) / 2
    with open(os.path.join(OUT, "frame-offset.txt"), "w", encoding="utf-8") as f:
        f.write(f"{expand:.2f}\n")
    print("painted:", len(slices), "sprites; frame expand units =", round(expand, 2))


if __name__ == "__main__":
    build()
