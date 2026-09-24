"""
Иконки способностей: ART/characters/pelag/Icon_*.png -> razlom/Assets/Resources/UI/Abilities.

Владелец генерирует иконки по общему промпту (23 сентября 2026): один символ на
тёмном фоне без рамки, рамку рисует ячейка пака. Фон у генераций чуть разный
(от #070C15 до #0F1620), и в ряду ячеек это читалось бы разными квадратами.
Скрипт сводит фон каждой иконки к одному цвету — заливке ячейки пака, — не
трогая сам рисунок: сдвиг цвета плавно гаснет по мере удаления пикселя от фона.

Файл в игре перезаписывается поверх, .meta остаётся — ссылки не рвутся.
Иконку, которой ещё нет в игре, скрипт тоже положит; имя берётся из файла.

Запуск: python tools/ui-kit/import_ability_icons.py [Имя ...]
"""
import glob
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "ART", "characters", "pelag")
DST = os.path.join(ROOT, "razlom", "Assets", "Resources", "UI", "Abilities")
TARGET = np.array([14, 19, 28], np.float32)   # фон ячейки: заливка панели #111620 с тенью снизу
REACH = 38.0                                  # насколько далеко от цвета фона ещё сдвигаем


def background(rgb):
    """Цвет фона — медиана пикселей рамки шириной 3% по краям."""
    h, w = rgb.shape[:2]
    b = max(4, int(min(h, w) * .03))
    edge = np.concatenate([rgb[:b].reshape(-1, 3), rgb[-b:].reshape(-1, 3), rgb[:, :b].reshape(-1, 3), rgb[:, -b:].reshape(-1, 3)])
    return np.median(edge, axis=0)


def convert(path):
    name = os.path.splitext(os.path.basename(path))[0]
    rgb = np.asarray(Image.open(path).convert("RGB"), np.float32)
    bg = background(rgb)
    dist = np.linalg.norm(rgb - bg, axis=2)
    weight = np.clip(1 - dist / REACH, 0, 1)[..., None] ** 1.5
    out = np.clip(rgb + (TARGET - bg) * weight, 0, 255).astype(np.uint8)
    Image.fromarray(out, "RGB").save(os.path.join(DST, name + ".png"), optimize=True)
    print(f"{name}: фон {bg.astype(int).tolist()} -> {TARGET.astype(int).tolist()}")


def main():
    wanted = set(sys.argv[1:])
    files = sorted(glob.glob(os.path.join(SRC, "Icon_*.png")))
    for path in files:
        key = os.path.splitext(os.path.basename(path))[0][len("Icon_"):]
        if wanted and key not in wanted:
            continue
        convert(path)


if __name__ == "__main__":
    main()
