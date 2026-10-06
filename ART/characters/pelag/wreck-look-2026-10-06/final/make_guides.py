# Layout/scale guides for the final Wreck frames (06.10), try 2 ("edit the screenshot, paint inside the magenta zones").
# Try 1 (try1/, padded canvas + thin outlines + full C/A frames as refs) was ignored by the model: lane came out ~2x again.
# Game numbers (Simulation.Wreck.Numbers/Forms): slam circle R 1.2 m at 2.2 m, lane to 6 m from the hero, width 1.5 m;
# Breakwater wall to 8 m, width 2 m; Ninth Wave reach 2.6 m, R up to 1.8 m, lane width up to x2; Shell burst R 2.5 m.
# Camera: ortho, pitch 48 deg -> ground depth x sin(48) = 0.743. Ruler = hero height on r1 (142 px ~ 1.5 m-in-frame -> 95 px/m).
# Canvas: r1_scene (1620x1080) cropped to 16:9 (1620x911, y 85..996); about 1.2-1.3x the live zoom, ratios to the hero are exact.
import math, os
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.abspath(__file__))
R1 = r"C:\Users\d.grab\Desktop\the-game\artifacts\hf\r1_scene.jpg"
OUT = os.path.join(ROOT, "guides")
os.makedirs(OUT, exist_ok=True)

K = 95.0
DEPTH = math.sin(math.radians(48))
HERO = (670.0, 510.0)
FILL = (255, 0, 255, 120)
LINE = (255, 0, 255, 255)


def proj(gx, gz):
    return (HERO[0] + gx * K, HERO[1] + gz * K * DEPTH)


def rot(x, y, th):
    c, s = math.cos(th), math.sin(th)
    return x * c - y * s, x * s + y * c


def ellipse_pts(cx, cz, r, n=96):
    return [proj(cx + r * math.cos(a), cz + r * math.sin(a)) for a in [i * 2 * math.pi / n for i in range(n + 1)]]


def zone(d, pts):
    d.polygon(pts, fill=FILL)
    d.line(pts, fill=LINE, width=4, joint="curve")


def dashed(d, pts):
    for i in range(0, len(pts) - 1, 4):
        d.line(pts[i:i + 3], fill=LINE, width=5)


def slam(size, th, reach, radius, end, half):
    # union of circle + lane, drawn as one filled shape so the overlap is not darker
    m = Image.new("L", size, 0)
    md = ImageDraw.Draw(m)
    cx, cz = rot(reach, 0, th)
    circle = ellipse_pts(cx, cz, radius)
    corners = [(reach, -half), (end, -half), (end, half), (reach, half)]
    lane = [proj(*rot(x, y, th)) for x, y in corners]
    md.polygon(circle, fill=255)
    md.polygon(lane, fill=255)
    return m, [circle, lane + [lane[0]]]


def render(name, deg, reach=None, radius=None, end=None, half=None, shell=False):
    base = Image.open(R1).convert("RGB").crop((0, 85, 1620, 996)).convert("RGBA")
    over = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(over)
    th = math.radians(deg)
    if reach is not None:
        m, outlines = slam(base.size, th, reach, radius, end, half)
        fill = Image.new("RGBA", base.size, FILL)
        over.paste(fill, (0, 0), m)
        # outline of the union: edge of the mask
        edge = m.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 0 else 0)
        edge = edge.filter(ImageFilter.MaxFilter(5))
        over.paste(Image.new("RGBA", base.size, LINE), (0, 0), edge)
    if shell:
        dashed(d, ellipse_pts(0, 0, 2.5))
    out = Image.alpha_composite(base, over).convert("RGB")
    p = os.path.join(OUT, "guide-%s.jpg" % name)
    out.save(p, quality=92)
    print(p, out.size)


render("base-v1", -8, 2.2, 1.2, 6.0, 0.75)
render("base-v2", 25, 2.2, 1.2, 6.0, 0.75)
render("breakwater-v1", -8, 2.2, 1.2, 8.0, 1.0)
render("breakwater-v2", 25, 2.2, 1.2, 8.0, 1.0)
render("ninthwave-v1", -8, 2.6, 1.8, 6.0, 1.5)
render("ninthwave-v2", 25, 2.6, 1.8, 6.0, 1.5)
render("watershell-v1", -8, shell=True)
render("watershell-v2", -8, 2.2, 1.2, 6.0, 0.75, shell=True)

# material swatches: close-up crops of the chosen frames, so the model takes their material but not their (too big) layout
W = os.path.dirname(ROOT)
Image.open(os.path.join(W, "frames", "C-iron-v2.png")).convert("RGB").crop((540, 300, 1290, 560)).save(os.path.join(ROOT, "refs", "swatch_C-iron-lane.jpg"), quality=92)
Image.open(os.path.join(W, "frames", "A-earth-v2.png")).convert("RGB").crop((300, 380, 760, 660)).save(os.path.join(ROOT, "refs", "swatch_A-earth-crater.jpg"), quality=92)
print("swatches ok")
