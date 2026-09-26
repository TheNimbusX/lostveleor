from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
sheet = Image.new('RGB', (5 * 360, 3 * 380), (29, 33, 30))
draw = ImageDraw.Draw(sheet)
for row, view in enumerate(('front', 'game', 'side')):
    for col, frame in enumerate((0, 4, 7, 10, 14)):
        p = root / 'review' / 'AN_ForestWendigo_Hit' / view / 'frames' / f'{frame:04d}.png'
        source = Image.open(p).convert('RGB').resize((360, 360))
        sheet.paste(source, (col * 360, row * 380 + 20))
        draw.text((col * 360 + 8, row * 380 + 3), f'{view} / {frame:02d}', fill='white')
sheet.save(root / 'hit_v2_contact_sheet.jpg', quality=94)
