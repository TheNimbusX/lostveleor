"""Reference breakdown helper: frame with a labelled 50 px grid for landmark reading."""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
HERE = Path(__file__).resolve().parent
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 13)
def grid(frame, box=None, zoom=1.0, marks=None):
    im = Image.open(HERE / 'reference' / f'ref_{frame + 1:03d}.png').convert('RGB')
    d = ImageDraw.Draw(im)
    for v in range(0, 960, 50):
        c = (255, 60, 60) if v % 100 == 0 else (255, 255, 0)
        d.line((v, 0, v, 959), fill=c, width=1); d.line((0, v, 959, v), fill=c, width=1)
        d.text((v + 2, 2), str(v), font=font, fill=c); d.text((2, v + 2), str(v), font=font, fill=c)
    for k, (x, y) in (marks or {}).items():
        d.ellipse((x - 5, y - 5, x + 5, y + 5), outline=(0, 255, 255), width=2); d.text((x + 6, y - 7), k, font=font, fill=(0, 255, 255))
    if box:
        im = im.crop(box)
        for v in range(box[0] - box[0] % 50, box[2], 50):
            pass
    if zoom != 1.0:
        im = im.resize((int(im.width * zoom), int(im.height * zoom)))
    return im
if __name__ == '__main__':
    f = int(sys.argv[1]); box = tuple(map(int, sys.argv[2].split(','))) if len(sys.argv) > 2 and sys.argv[2] != '-' else None
    zoom = float(sys.argv[3]) if len(sys.argv) > 3 else 1.0
    grid(f, box, zoom).save(HERE / 'breakdown' / f'grid_{f:03d}{"_crop" if box else ""}.png')
