"""Вложения для ChatGPT: иконки форм Шквала (по образцу ART/UI/icons-whirlwind-forms-2026-10-02).

Собирает chatgpt-refs/ из файлов репозитория (только чтение) и кладёт копию базовой иконки в source/.
    python make_refs.py
"""
import os
import shutil

from PIL import Image, ImageDraw, ImageFont

REPO = r'C:\Users\d.grab\Desktop\the-game'
HERE = os.path.dirname(os.path.abspath(__file__))
REFS = os.path.join(HERE, 'chatgpt-refs')
SRC = os.path.join(HERE, 'source')
WHIRL = os.path.join(REPO, 'ART', 'UI', 'icons-whirlwind-forms-2026-10-02')
SQUALL = os.path.join(REPO, 'ART', 'characters', 'pelag', 'squall-forms-2026-10-02', 'chatgpt-results')
BASE_ICON = os.path.join(REPO, 'razlom', 'Assets', 'Resources', 'UI', 'Abilities', 'Icon_Squall.png')

os.makedirs(REFS, exist_ok=True)
os.makedirs(SRC, exist_ok=True)

# 1 — эталон стиля: тот же лист, что ушёл в ChatGPT для Вихря (в нём и нынешний Шквал, вторая плитка).
shutil.copyfile(os.path.join(WHIRL, 'chatgpt-refs', '1-icon-style-sheet.jpg'), os.path.join(REFS, '1-icon-style-sheet.jpg'))

# 2 — нынешняя базовая иконка Шквала из игры (Resources/UI/Abilities/Icon_Squall.png): композиция и сабля.
shutil.copyfile(BASE_ICON, os.path.join(SRC, 'Icon_Squall.png'))
base = Image.open(BASE_ICON).convert('RGB')
base.resize((1024, 1024), Image.LANCZOS).save(os.path.join(REFS, '2-icon-squall-current.jpg'), quality=92)

# 3 — принятые иконки Вихря в пене (база + три формы, как в игре): язык воды и пены для иконок форм.
shutil.copyfile(os.path.join(WHIRL, 'sheet-final.jpg'), os.path.join(REFS, '3-whirlwind-form-icons.jpg'))

# 4 — мотивы форм из кадров владельца (ChatGPT, 02.10): вырезы 700x700 → 600x600 с подписью, как 3-form-motifs Вихря.
PANELS = [
    ('Hunt', 'elusive-return-3.png', (800, 324, 1500, 1024)),   # прыжки и всплески на целях (Охота — на основе этих кадров)
    ('Foam Trail', 'trail-wavy-1.png', (620, 170, 1320, 870)),  # полосы пены на земле между целями
    ('Elusive', 'elusive-return-3.png', (520, 200, 1220, 900)),  # пенный двойник на старте и дуга возврата
]
sheet = Image.new('RGB', (600 * len(PANELS), 600), (20, 24, 34))
try:
    font = ImageFont.truetype('arial.ttf', 16)
except OSError:
    font = ImageFont.load_default()
for i, (label, name, box) in enumerate(PANELS):
    panel = Image.open(os.path.join(SQUALL, name)).convert('RGB').crop(box).resize((600, 600), Image.LANCZOS)
    draw = ImageDraw.Draw(panel)
    draw.text((11, 9), label, fill=(0, 0, 0), font=font)
    draw.text((10, 8), label, fill=(255, 255, 255), font=font)
    sheet.paste(panel, (600 * i, 0))
sheet.save(os.path.join(REFS, '4-squall-form-motifs.jpg'), quality=90)
print('ok', REFS)
