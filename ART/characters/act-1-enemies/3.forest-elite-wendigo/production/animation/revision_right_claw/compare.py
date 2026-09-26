from pathlib import Path
from PIL import Image, ImageDraw

here = Path(__file__).resolve().parent
root = here.parents[2]
old = root / 'production' / 'review' / 'animation_stage' / 'AN_ForestWendigo_Claw' / 'game' / 'frames'
new = here / 'review_full' / 'AN_ForestWendigo_Claw' / 'game' / 'frames'
refs = root / 'production' / 'animation' / 'revision_primary' / 'ref_frames' / 'claw'
sheet = Image.new('RGB', (360 * 6, 360 * 3), (30, 34, 34))
draw = ImageDraw.Draw(sheet)
rows = [
    ('old', old, ['0000','0006','0012','0015','0018','0024']),
    ('right-claw probe', new, ['0000','0006','0012','0015','0018','0024']),
    ('video reference', refs, ['001','004','007','010','013','016']),
]
for row, (label, base, frames) in enumerate(rows):
    for col, name in enumerate(frames):
        src = Image.open(base / f'{name}.png').convert('RGB')
        src.thumbnail((350, 325))
        x = col*360 + (360-src.width)//2
        y = row*360 + 30 + (325-src.height)//2
        sheet.paste(src, (x,y))
        draw.text((col*360+8,row*360+6),f'{label} {name}',fill=(235,235,220))
out = here / 'comparison.png'
sheet.save(out)
print(out)
