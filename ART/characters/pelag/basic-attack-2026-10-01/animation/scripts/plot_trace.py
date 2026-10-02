import json, sys, math
from PIL import Image, ImageDraw, ImageFont
trace = json.load(open(sys.argv[1])); out = sys.argv[2]
contacts = {"Sabre1": 4, "Sabre2": 4, "Sabre3": 7}
W = 420; S = 140  # px на единицу тейка
img = Image.new("RGB", (W * 3, W + 30), (30, 30, 30)); d = ImageDraw.Draw(img)
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 16)
for k, (name, tips) in enumerate(trace.items()):
    ox, oy = k * W + W // 2, W // 2 + 30
    # сектор Sim ±55° вперёд (вперёд = -Y мира = вниз картинки... рисуем вперёд ВВЕРХ)
    R = 1.25 * S / 2.05  # 2.5 м ≈ 1.25 ед. тейка при масштабе ~2 м/ед — грубо
    for a in range(-55, 56, 5):
        x = ox + R * math.sin(math.radians(a)); y = oy - R * math.cos(math.radians(a))
        d.point((x, y), fill=(90, 90, 160))
    d.line((ox, oy, ox + R * math.sin(math.radians(-55)), oy - R * math.cos(math.radians(-55))), fill=(70, 70, 130))
    d.line((ox, oy, ox + R * math.sin(math.radians(55)), oy - R * math.cos(math.radians(55))), fill=(70, 70, 130))
    prev = None
    for i, t in enumerate(tips):
        # мир: вперёд -Y → экран вверх; правая рука персонажа (-X) → экран вправо при взгляде сверху-сзади
        x = ox - t[0] * S; y = oy + t[1] * S
        c = (255, 220, 60) if i == contacts[name] else (200, 120 + 9 * i, 255 - 9 * i)
        if prev: d.line((prev[0], prev[1], x, y), fill=(160, 160, 160))
        d.ellipse((x - 4, y - 4, x + 4, y + 4), fill=c)
        d.text((x + 5, y - 8), str(i), fill=(220, 220, 220), font=font)
        prev = (x, y)
    d.ellipse((ox - 5, oy - 5, ox + 5, oy + 5), fill=(255, 80, 80))
    d.text((k * W + 8, 6), name + " (вид сверху, вперёд — вверх)", fill=(255, 255, 0), font=font)
img.save(out)
