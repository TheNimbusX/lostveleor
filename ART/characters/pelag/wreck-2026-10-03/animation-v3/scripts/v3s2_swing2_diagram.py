"""Крушение v3, Swing2: вид сверху — почему контакт в кадре 6 недостижим (пути головы из v3s2_swing2_probe.sh).
python v3s2_swing2_diagram.py <probe dir> <swing1 bake.json> <out.png>"""
import json, math, os, re, sys
from PIL import Image, ImageDraw, ImageFont
P, BAKE, OUT = sys.argv[1:4]
F = ImageFont.truetype(r"C:\Windows\Fonts\segoeui.ttf", 17); FB = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 21)
S, W, H = 120, 1140, 820; O = (520, 470)
xy = lambda x, z: (O[0] + x * S, O[1] - z * S)


def path(name, upto):
    pts = []
    for line in open(os.path.join(P, name + ".txt"), encoding="utf-8"):
        m = re.match(r"\s+f\s*([\d.]+) head \((-?[\d.]+) (-?[\d.]+) (-?[\d.]+)\)", line)
        if m and float(m.group(1)) <= upto + 1e-6: pts.append((float(m.group(1)), float(m.group(2)), float(m.group(4))))
    return pts


im = Image.new("RGB", (W, H), (28, 30, 30)); d = ImageDraw.Draw(im)
for r in (1, 2, 3):
    d.ellipse([O[0] - r * S, O[1] - r * S, O[0] + r * S, O[1] + r * S], outline=(55, 60, 60))
arc = [xy(r * math.sin(math.radians(a)), r * math.cos(math.radians(a))) for r in (2.2, 2.7) for a in (range(-10, 11, 2) if r == 2.2 else range(10, -11, -2))]
d.polygon(arc, fill=(40, 90, 50), outline=(90, 200, 110))
d.text(xy(0.35, 2.85), "зона контакта: 2,2–2,7 м, ±10°", font=F, fill=(120, 220, 140))
d.ellipse([O[0] - 14, O[1] - 14, O[0] + 14, O[1] + 14], fill=(200, 190, 170)); d.line([O, xy(0, .45)], fill=(200, 190, 170), width=4)
d.text((O[0] + 18, O[1] + 4), "Пелаг (вперёд ↑)", font=F, fill=(200, 190, 170))
s1 = [(s["t"], s["p"][0], s["p"][2]) for s in json.load(open(BAKE))["samples"] if s["t"] <= 8]
d.line([xy(x, z) for t, x, z in s1], fill=(150, 150, 150), width=3)
d.text(xy(1.75, -0.2), "Swing1 0→8", font=F, fill=(170, 170, 170))
c6 = path("c6_backhand", 6); c15 = path("c15_any", 15)
d.line([xy(x, z) for t, x, z in c15], fill=(90, 140, 230), width=2)
d.line([xy(x, z) for t, x, z in c6], fill=(235, 80, 70), width=5)
for t, x, z in c6:
    if abs(t - round(t)) < 1e-6: d.ellipse([xy(x, z)[0] - 4, xy(x, z)[1] - 4, xy(x, z)[0] + 4, xy(x, z)[1] + 4], fill=(235, 80, 70))
t, x, z = c6[-1]; px, py = xy(x, z)
d.text((px - 230, py + 10), "Swing2 кадр 6 = тик 14:\nголова тут (−114°), 3,1 м мимо зоны", font=F, fill=(250, 120, 110))
t, x, z = c15[-1]
d.text((xy(x, z)[0] + 150, xy(x, z)[1] + 20), "впереди снова только к тику 23 —\nполный оборот, и опять справа налево", font=F, fill=(130, 170, 250))
d.text((16, 12), "Крушение v3 · Swing2: вид сверху, путь головы якоря (физика игры, лучший путь хвата)", font=FB, fill=(240, 240, 240))
d.text((16, 44), "от Swing1 кадр 8 голова летит влево 25,6 м/с; за 6 тиков — ≈5 м дуги вокруг хвата (r ≈ 2 м) ≈ 110°", font=F, fill=(210, 210, 210))
d.text((16, H - 34), "красный — Swing2 c контактом в кадре 6 (лучший из CMA, кисти до 25 м/с — то же); синий — продолжение до тика 23", font=F, fill=(170, 180, 180))
im.save(OUT); print("diagram", OUT)
