"""Builds the reference images for the Anchor Throw ChatGPT pack (07.10).

Pose sheet refs: Pelag model views, anchor/chain/grip, the accepted Wreck key-pose sheet.
Base target-frame refs: clean game frame, in-game anchor-family VFX cells, approved Wreck lunge
target frame, anchor design, scale guide for a 7 m x 0.9 m throw line.
"""
import math
import os
import shutil

from PIL import Image, ImageDraw, ImageFont

ROOT = r"C:\Users\d.grab\Desktop\the-game"
OUT = os.path.join(ROOT, r"ART\characters\pelag\anchor-throw-2026-10-07\refs")
V4 = os.path.join(ROOT, r"ART\characters\pelag\wreck-2026-10-03\motion-ref-pack-v4")
LOOK = os.path.join(ROOT, r"ART\characters\pelag\wreck-look-2026-10-06")
STACK = os.path.join(ROOT, r"artifacts\wreck\v4\vfx-sabrestack")

os.makedirs(OUT, exist_ok=True)


def save_jpg(im, name, q=92):
    im.convert("RGB").save(os.path.join(OUT, name), quality=q)


# ---- pose sheet refs ----
shutil.copyfile(os.path.join(V4, "1-pelag-model-4views.jpg"), os.path.join(OUT, "pose-1-pelag-model-4views.jpg"))
shutil.copyfile(os.path.join(V4, "2-anchor-chain-design.jpg"), os.path.join(OUT, "pose-2-anchor-chain-design.jpg"))
save_jpg(Image.open(os.path.join(V4, "B-anchor-combo-key-poses-chatgpt.png")), "pose-3-accepted-anchor-combo-sheet.jpg")

# ---- base frame refs ----
scene = Image.open(os.path.join(LOOK, r"chatgpt-pack-v4\1-game-scene.jpg")).convert("RGB")
save_jpg(scene, "base-1-game-scene.jpg", 95)

# in-game anchor family VFX cells (label bar cut off)
sw = Image.open(os.path.join(STACK, "sabre-vs-wreck-swings.png")).convert("RGB")
lu = Image.open(os.path.join(STACK, "sabre-vs-wreck-lunge.png")).convert("RGB")
cells = [
    sw.crop((646, 490, 1286, 930)),    # WRECK swing1 +2
    sw.crop((646, 1426, 1286, 1866)),  # WRECK swing2 +2
    lu.crop((1292, 490, 1932, 930)),   # WRECK lunge +8
]
gap = 8
strip = Image.new("RGB", (640 * 3 + gap * 2, 440), (12, 12, 14))
for i, c in enumerate(cells):
    strip.paste(c, (i * (640 + gap), 0))
save_jpg(strip, "base-2-ingame-anchor-vfx.jpg", 93)

save_jpg(Image.open(os.path.join(LOOK, r"chatgpt-results\v4-lunge-simple.png")), "base-3-approved-wreck-lunge.jpg", 93)
shutil.copyfile(os.path.join(V4, "2-anchor-chain-design.jpg"), os.path.join(OUT, "base-4-anchor-chain-design.jpg"))

# scale guide: ground band from the hand (0.5 m) to 7 m, 0.9 m wide, ~90 px/m across, x0.74 in depth
guide = scene.copy()
ov = Image.new("RGBA", guide.size, (0, 0, 0, 0))
d = ImageDraw.Draw(ov)
feet = (668.0, 592.0)
pxm_x, pxm_y = 90.0, 67.0
ang = math.atan2(0.27, 1.0)  # ground direction toward the guardian group
ux, uy = math.cos(ang), math.sin(ang)
nx, ny = -uy, ux


def g2s(along, side):
    gx = ux * along + nx * side
    gy = uy * along + ny * side
    return (feet[0] + gx * pxm_x, feet[1] + gy * pxm_y)


half = 0.45
poly = [g2s(0.5, -half), g2s(7.0, -half), g2s(7.0, half), g2s(0.5, half)]
d.polygon(poly, fill=(255, 0, 255, 90), outline=(255, 0, 255, 255))
d.line(poly + [poly[0]], fill=(255, 0, 255, 255), width=5)
# semicircle of landing spots for the yank, ~1.2 m in front of Pelag
for k in range(-2, 3):
    a = k * 0.45
    cx = math.cos(a) * 1.3
    cy = math.sin(a) * 1.3
    gx = ux * cx + nx * cy
    gy = uy * cx + ny * cy
    sx, sy = feet[0] + gx * pxm_x, feet[1] + gy * pxm_y
    d.ellipse((sx - 9, sy - 7, sx + 9, sy + 7), outline=(255, 255, 255, 255), width=3)
try:
    font = ImageFont.truetype("arialbd.ttf", 26)
except OSError:
    font = ImageFont.load_default()
end = g2s(7.0, 0)
d.text((end[0] - 40, end[1] + 30), "7 m", fill=(255, 0, 255, 255), font=font, stroke_width=3, stroke_fill=(0, 0, 0, 255))
mid = g2s(3.5, -half)
d.text((mid[0] - 60, mid[1] - 44), "0.9 m wide", fill=(255, 0, 255, 255), font=font, stroke_width=3, stroke_fill=(0, 0, 0, 255))
guide = Image.alpha_composite(guide.convert("RGBA"), ov)
save_jpg(guide, "base-5-scale-guide.jpg", 93)
print("ok", os.listdir(OUT))
