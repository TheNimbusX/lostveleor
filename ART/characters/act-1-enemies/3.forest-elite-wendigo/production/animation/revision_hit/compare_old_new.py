from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
old = root.parent.parent / 'review' / 'animation_stage' / 'AN_ForestWendigo_Hit' / 'game' / 'frames'
new = root / 'review' / 'AN_ForestWendigo_Hit' / 'game' / 'frames'
frames = (0, 3, 5, 7, 10, 14)
sheet = Image.new('RGB', (6*320, 2*345), (30, 30, 30))
draw = ImageDraw.Draw(sheet)
for row, (label, src) in enumerate((('OLD', old), ('REVISION', new))):
    for col, frame in enumerate(frames):
        im = Image.open(src / f'{frame:04d}.png').convert('RGB').resize((320, 320))
        sheet.paste(im, (col*320, row*345 + 25))
        draw.text((col*320+8, row*345+6), f'{label} | {frame:02d}', fill='white')
sheet.save(root/'hit_old_new_comparison.jpg', quality=94)
