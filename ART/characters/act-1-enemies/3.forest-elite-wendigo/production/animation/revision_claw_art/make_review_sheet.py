"""Create a compact three-view key-pose sheet from the final rendered frames."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
ROOT = HERE / 'review_delivery_final' / 'AN_ForestWendigo_Claw'
FRAMES = (0, 8, 15, 17, 18, 22)
VIEWS = ('game', 'front', 'side')
CELL = 320
LABEL = 34
sheet = Image.new('RGB', (CELL*len(FRAMES), (CELL+LABEL)*len(VIEWS)), '#202826')
draw = ImageDraw.Draw(sheet)
for row, view in enumerate(VIEWS):
    for col, frame in enumerate(FRAMES):
        path = ROOT / view / 'frames' / f'{frame:04d}.png'
        with Image.open(path) as source:
            tile = source.convert('RGB').resize((CELL,CELL), Image.Resampling.LANCZOS)
        x, y = col*CELL, row*(CELL+LABEL)
        sheet.paste(tile,(x,y))
        draw.text((x+9,y+CELL+8),f'{view.upper()}  f{frame:02d}',fill='#f2efe4')
out = HERE / 'ForestWendigo_Claw_keyposes.png'
sheet.save(out)
print('SHEET',out)
