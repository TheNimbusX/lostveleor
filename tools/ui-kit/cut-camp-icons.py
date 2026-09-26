"""Нарезка второго листа минималистичных значков (лагерь, карта, баффы; 26 сентября).

Лист — ART/UI/concepts-2026-09-26-pass/icons-B2-ink.png: кремовые мазки тушью на чёрном, сетка 4×4.
Порядок клеток: осколки, перековка, продажа, обновить / палатка, костёр, наковальня, мешок /
арка, сундук, череп, колба / смола, рывок, замок, заказ.
Значки — белые маски (цвет даёт интерфейс). Файлы перезаписываются на месте — .meta и ссылки сохраняются.
Атлас карты MapSymbols остаётся сеткой 4×2 в прежнем порядке: палатка, костёр, наковальня, мешок,
арка, сундук, череп, огонёк (последняя клетка не используется — туда колба).

Запуск: python tools/ui-kit/cut-camp-icons.py [лист]
"""
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ASSETS = os.path.join(ROOT, "razlom", "Assets")
SHEET = os.path.join(ROOT, "ART", "UI", "concepts-2026-09-26-pass", "icons-B2-ink.png")
CELLS = ["shards", "reforge", "sell", "refresh", "tent", "campfire", "anvil", "sack",
         "arch", "chest", "skull", "flask", "resin", "surge", "lock", "order"]
FILES = {
    "shards": "UI/CampShops/shards.png",
    "reforge": "UI/CampShops/reforge.png",
    "sell": "UI/CampShops/sell.png",
    "refresh": "UI/CampShops/refresh.png",
    "resin": "UI/Kit/Watercolor/wc_buff_resin.png",
    "surge": "UI/Kit/Watercolor/wc_buff_surge.png",
    "lock": "UI/Kit/Icons/lock.png",
    "order": "UI/RunIcons/order.png",
    "flask": "UI/RunIcons/alchemist.png",
}
ATLAS = ["tent", "campfire", "anvil", "sack", "arch", "chest", "skull", "flask"]
SIZE, PAD = 256, 22


def cut(img, index):
    """Маска значка из клетки листа: как в cut-run-icons.py — брызги по краю отбрасываются, квадрат с полями."""
    from scipy import ndimage
    n = img.shape[0] // 4
    row, col = divmod(index, 4)
    cell = img[row * n:(row + 1) * n, col * n:(col + 1) * n]
    alpha = np.clip((cell - 0.08) / 0.8, 0.0, 1.0)
    solid = ndimage.binary_dilation(alpha > 0.1, iterations=3)
    labels, count = ndimage.label(solid)
    sizes = ndimage.sum(solid, labels, range(1, count + 1))
    keep = np.isin(labels, np.nonzero(sizes > sizes.max() * 0.02)[0] + 1)
    alpha = alpha * ndimage.binary_dilation(keep, iterations=4)
    ys, xs = np.nonzero(alpha > 0.05)
    crop = alpha[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    side = max(crop.shape)
    square = np.zeros((side, side), np.float32)
    oy, ox = (side - crop.shape[0]) // 2, (side - crop.shape[1]) // 2
    square[oy:oy + crop.shape[0], ox:ox + crop.shape[1]] = crop
    mask = Image.fromarray((square * 255 + .5).astype(np.uint8), "L").resize((SIZE - 2 * PAD,) * 2, Image.LANCZOS)
    canvas = Image.new("L", (SIZE, SIZE), 0)
    canvas.paste(mask, (PAD, PAD))
    return canvas


def white(mask):
    full = Image.new("L", mask.size, 255)
    return Image.merge("RGBA", (full, full, full, mask))


def main():
    sheet = sys.argv[1] if len(sys.argv) > 1 else SHEET
    img = np.asarray(Image.open(sheet).convert("L")).astype(np.float32) / 255.0
    masks = {name: cut(img, i) for i, name in enumerate(CELLS)}
    for name, path in FILES.items():
        white(masks[name]).save(os.path.join(ASSETS, path))
        print(path)
    atlas = Image.new("L", (SIZE * 4, SIZE * 2), 0)
    for i, name in enumerate(ATLAS):
        atlas.paste(masks[name], (i % 4 * SIZE, i // 4 * SIZE))
    white(atlas).save(os.path.join(ASSETS, "Resources", "UI", "HUD", "MapSymbols.png"))
    print("Resources/UI/HUD/MapSymbols.png")


if __name__ == "__main__":
    main()
