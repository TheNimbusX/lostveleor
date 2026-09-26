from pathlib import Path
import sys
from PIL import Image, ImageDraw

root = Path(sys.argv[1])
for view in ("game", "side"):
    images = sorted(root.glob(f"*_{view}.png"))
    size = 500
    sheet = Image.new("RGB", (size * 4, (size + 34) * 2), (20, 20, 20))
    d = ImageDraw.Draw(sheet)
    for i, path in enumerate(images):
        x, y = (i % 4) * size, (i // 4) * (size + 34)
        sheet.paste(Image.open(path).convert("RGB").resize((size,size)), (x,y+34))
        d.text((x+10,y+8), path.stem, fill="white")
    sheet.save(root/f"sheet_{view}.png")
