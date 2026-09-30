"""Diagnostic contact sheet from unmodified actual gameplay screenshots."""
from pathlib import Path
from PIL import Image, ImageDraw
root = Path(__file__).resolve().parent
cases = ('whirlwind','cleave','blaze','chain-step','fire-flask','skewer','backblast')
sheet = Image.new('RGB', (1280, 384 * len(cases)), '#262c29')
draw = ImageDraw.Draw(sheet)
for row, name in enumerate(cases):
    for col, shot in enumerate(sorted((root / 'poses-r02' / name).glob('shot_*.png'))[:2]):
        picture = Image.open(shot).convert('RGB')
        picture.thumbnail((640, 360))
        sheet.paste(picture, (640 * col, 384 * row + 24))
        draw.text((640 * col + 8, 384 * row + 4), name + ' / ' + shot.name, fill='white')
sheet.save(root / 'poses-r02/contactsheet.jpg', quality=93)
print('Prepared diagnostic contact sheet')
