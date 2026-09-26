"""Нарезка минималистичных значков забега (владелец, 26 сентября: «переделать на более минималистичный вид»).

Лист — ART/UI/concepts-2026-09-26-pass/icons-B-ink.png: кремовые мазки тушью на чёрном, сетка 4×4.
Каждый значок — белая маска (RGB белый, A — яркость), квадрат 256 px с полями; цвет даёт интерфейс.
Файлы Assets/UI/RunIcons/*.png перезаписываются на месте — .meta и ссылки в префабах сохраняются.

Запуск: python tools/ui-kit/cut-run-icons.py [лист]
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SHEET = os.path.join(ROOT, "ART", "UI", "concepts-2026-09-26-pass", "icons-B-ink.png")
OUT = os.path.join(ROOT, "razlom", "Assets", "UI", "RunIcons")
NAMES = ["rift", "depth", "items", "gold", "exit", "cache", "encounter", "ability",
         "talent", "repeat", "camp", "salvage", "death", "victory", "health", "lavidium"]
SIZE, PAD = 256, 22


def main():
    sheet = sys.argv[1] if len(sys.argv) > 1 else SHEET
    img = np.asarray(Image.open(sheet).convert("L")).astype(np.float32) / 255.0
    n = img.shape[0] // 4
    for index, name in enumerate(NAMES):
        row, col = divmod(index, 4)
        cell = img[row * n:(row + 1) * n, col * n:(col + 1) * n]
        alpha = np.clip((cell - 0.08) / 0.8, 0.0, 1.0)
        solid = ndimage.binary_dilation(alpha > 0.1, iterations=3)
        labels, count = ndimage.label(solid)
        # Крупные куски значка; одиночные брызги кисти по краю клетки отбрасываются.
        sizes = ndimage.sum(solid, labels, range(1, count + 1))
        keep = np.isin(labels, np.nonzero(sizes > sizes.max() * 0.02)[0] + 1)
        alpha = alpha * ndimage.binary_dilation(keep, iterations=4)
        ys, xs = np.nonzero(alpha > 0.05)
        y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
        crop = alpha[y0:y1, x0:x1]
        side = max(crop.shape)
        square = np.zeros((side, side), np.float32)
        oy, ox = (side - crop.shape[0]) // 2, (side - crop.shape[1]) // 2
        square[oy:oy + crop.shape[0], ox:ox + crop.shape[1]] = crop
        mask = Image.fromarray((square * 255 + .5).astype(np.uint8), "L").resize((SIZE - 2 * PAD,) * 2, Image.LANCZOS)
        canvas = Image.new("L", (SIZE, SIZE), 0)
        canvas.paste(mask, (PAD, PAD))
        white = Image.new("L", (SIZE, SIZE), 255)
        Image.merge("RGBA", (white, white, white, canvas)).save(os.path.join(OUT, name + ".png"))
        print(name)


if __name__ == "__main__":
    main()
