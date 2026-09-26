"""Лист проверки клипа Hit: шесть кадров с контуром позы покоя.

Запуск после review_forest_bud_hit.py: python make_hit_contact_sheet.py
"""
import json, os
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

R = Path(r'C:/Users/d.grab/Desktop/the-game')
TILES = Path(os.environ.get('FOREST_BUD_HIT_TILES', str(R / 'artifacts/forest-bud-production/review-hit')))
OUT = Path(os.environ.get('FOREST_BUD_HIT_SHEET', str(R / 'ART/ENEMIES/Forest_Bud/review/hit_r01.png')))
OUT.parent.mkdir(parents=True, exist_ok=True)
LABELS = {1: 'покой', 3: 'отдача, лепестки распахнулись', 4: 'сплющивание', 6: 'лепестки захлопываются',
          8: 'распрямление', 11: 'покой'}
frames = json.loads((TILES / 'tiles.json').read_text(encoding='utf8'))['frames']

TILE, PAD, HEAD, FOOT = 520, 12, 64, 44
sheet = Image.new('RGB', (3 * TILE + 4 * PAD, HEAD + 2 * (TILE + FOOT) + 3 * PAD), (22, 26, 22))
draw = ImageDraw.Draw(sheet)
title = ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf', 26)
font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 21)
small = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 17)
draw.text((PAD + 4, 14), 'Плюй-плод — реакция на попадание (Hit), 11 кадров, 0,33 с', font=title, fill=(238, 230, 204))
draw.text((3 * TILE + 3 * PAD - 520, 22), 'орто-камера игры 48° · белый контур — поза покоя', font=small, fill=(170, 176, 160))

alpha = Image.open(TILES / 'ghost.png').convert('RGBA').split()[3]
edge = ImageChops.subtract(alpha.filter(ImageFilter.MaxFilter(5)), alpha.filter(ImageFilter.MinFilter(3)))
edge = edge.point(lambda v: 150 if v > 60 else 0)
for index, frame in enumerate(frames):
    tile = Image.open(TILES / f'hit_{frame:02d}.png').convert('RGBA')
    if frame != frames[0]:
        tile.alpha_composite(Image.merge('RGBA', [Image.new('L', tile.size, 245)] * 3 + [edge]))
    x = PAD + (index % 3) * (TILE + PAD)
    y = HEAD + PAD + (index // 3) * (TILE + FOOT + PAD)
    sheet.paste(tile.convert('RGB'), (x, y))
    seconds = f'{(frame - 1) / 30:.2f}'.replace('.', ',')
    draw.text((x + 10, y + TILE + 8), f'кадр {frame} · {seconds} с · {LABELS.get(frame, "")}', font=font, fill=(235, 227, 197))
sheet.save(OUT)
print(OUT)
