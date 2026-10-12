"""Scale guides for the three forms of Anchor Throw (Net, Fan, Harpoon), 07.10.

Drawn over the clean game frame refs/base-1-game-scene.jpg with the same ground mapping as make_refs.py
(Pelag's feet at (668, 592), ~90 px/m across, ~67 px/m in depth).
Guardians (feet, ground metres from Pelag): B middle 4.6 m @13.6deg, C far right 6.6 m @12.7deg,
D lower-left 4.3 m @47deg, E lower-right 5.9 m @37deg, F bottom 6.0 m @58deg, A top 4.8 m @-63deg.
"""
import math
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = r"C:\Users\d.grab\Desktop\the-game\ART\characters\pelag\anchor-throw-2026-10-07"
OUT = os.path.join(ROOT, "refs")
scene = Image.open(os.path.join(OUT, "base-1-game-scene.jpg")).convert("RGB")

FEET = (668.0, 592.0)
PXM_X, PXM_Y = 90.0, 67.0
try:
    FONT = ImageFont.truetype("arialbd.ttf", 26)
except OSError:
    FONT = ImageFont.load_default()


def g2s(deg, along, side):
    a = math.radians(deg)
    ux, uy = math.cos(a), math.sin(a)
    nx, ny = -uy, ux
    gx = ux * along + nx * side
    gy = uy * along + ny * side
    return (FEET[0] + gx * PXM_X, FEET[1] + gy * PXM_Y)


def band(d, deg, a0, a1, half, rgb, fill_alpha=80, width=5):
    poly = [g2s(deg, a0, -half), g2s(deg, a1, -half), g2s(deg, a1, half), g2s(deg, a0, half)]
    d.polygon(poly, fill=rgb + (fill_alpha,))
    d.line(poly + [poly[0]], fill=rgb + (255,), width=width)
    return poly


def dashed(d, p0, p1, rgb, width=4, dash=14, gap=10):
    x0, y0 = p0
    x1, y1 = p1
    L = math.hypot(x1 - x0, y1 - y0)
    n = int(L // (dash + gap)) + 1
    for i in range(n):
        s = i * (dash + gap) / L
        e = min(1.0, (i * (dash + gap) + dash) / L)
        d.line((x0 + (x1 - x0) * s, y0 + (y1 - y0) * s, x0 + (x1 - x0) * e, y0 + (y1 - y0) * e),
               fill=rgb + (255,), width=width)


def label(d, xy, text, rgb):
    d.text(xy, text, fill=rgb + (255,), font=FONT, stroke_width=3, stroke_fill=(0, 0, 0, 255))


def save(ov, name):
    im = Image.alpha_composite(scene.convert("RGBA"), ov).convert("RGB")
    im.save(os.path.join(OUT, name), quality=93)


MAG = (255, 0, 255)
CYAN = (0, 255, 255)

# ---- NET: a 3 m wide band (aimed a bit lower, 25 deg) from 0.5 to 7 m; B, C, D, E inside, A and F outside
ov = Image.new("RGBA", scene.size, (0, 0, 0, 0))
d = ImageDraw.Draw(ov)
band(d, 25, 0.5, 7.0, 1.6, MAG, 70)
e = g2s(25, 7.0, 0)
label(d, (e[0] + 20, e[1] - 10), "7 m", MAG)
m = g2s(25, 3.6, -1.6)
label(d, (m[0] - 20, m[1] - 46), "3 m wide", MAG)
save(ov, "form-net-scale-guide.jpg")

# ---- FAN: three 0.9 m bands at 10, 35, 60 deg (spread +-25 deg), 7 m each
ov = Image.new("RGBA", scene.size, (0, 0, 0, 0))
d = ImageDraw.Draw(ov)
for deg in (10, 35, 60):
    band(d, deg, 0.5, 7.0, 0.45, MAG, 80)
e = g2s(10, 7.0, 0)
label(d, (e[0] + 18, e[1] - 16), "7 m", MAG)
e = g2s(35, 7.0, 0)
label(d, (e[0] + 18, e[1] - 6), "7 m", MAG)
e = g2s(60, 6.6, 0)
label(d, (e[0] + 24, e[1] - 10), "7 m", MAG)
save(ov, "form-fan-scale-guide.jpg")

# ---- HARPOON: base line (15 deg), solid band up to the first guardian (B at ~4.6 m), dashed beyond = not reached
ov = Image.new("RGBA", scene.size, (0, 0, 0, 0))
d = ImageDraw.Draw(ov)
band(d, 15.1, 0.5, 4.5, 0.45, CYAN, 80)
for side in (-0.45, 0.45):
    dashed(d, g2s(15.1, 4.5, side), g2s(15.1, 7.0, side), CYAN, width=3)
b = g2s(13.6, 4.62, 0)
d.ellipse((b[0] - 58, b[1] - 120, b[0] + 58, b[1] + 22), outline=CYAN + (255,), width=4)
label(d, (b[0] - 70, b[1] + 28), "stops here", CYAN)
e = g2s(15.1, 7.0, 0)
label(d, (e[0] - 10, e[1] + 30), "7 m (not reached)", CYAN)
save(ov, "form-harpoon-scale-guide.jpg")
print("ok")
