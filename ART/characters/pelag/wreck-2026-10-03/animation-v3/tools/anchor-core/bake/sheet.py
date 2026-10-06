"""Contact sheet of a baked anchor path at game scale (PIL only).

python sheet.py <name.sheet.json> <out.jpg> [--every 1] [--cols 8] [--ppm 87.1]
Tiles: the game camera (orthographic, pitch 48 deg, CombatSize 6.2 at 1080p = 87.1 px/m by default), hero at the
origin facing +x like the 03.10 capture (target on +x). Overview: top and side views of the head path coloured by speed,
arcs 2.2/2.7 m (contact band) and 2.8 m (damage arc +-72.5 deg), the Sim contact tick marked.
"""
import json, math, argparse
from PIL import Image, ImageDraw, ImageFont

ap = argparse.ArgumentParser()
ap.add_argument('data'); ap.add_argument('out')
ap.add_argument('--every', type=int, default=1)
ap.add_argument('--cols', type=int, default=8)
ap.add_argument('--ppm', type=float, default=1080 / (2 * 6.2))
a = ap.parse_args()
D = json.load(open(a.data, encoding='utf8'))
L = D['chainLength']
names = D['boneNames']
ix = {n: i for i, n in enumerate(names)}
PITCH = math.radians(48)
UP = (0.0, math.cos(PITCH), math.sin(PITCH))          # screen up in world
FWD = (0.0, -math.sin(PITCH), math.cos(PITCH))        # camera looks along this
try:
    FONT = ImageFont.truetype('arial.ttf', 13); FONT_B = ImageFont.truetype('arialbd.ttf', 15); FONT_T = ImageFont.truetype('arialbd.ttf', 20)
except OSError:
    FONT = FONT_B = FONT_T = ImageFont.load_default()
BG, GROUND, BODY, METAL, CHAIN_T, CHAIN_S = (24, 28, 34), (52, 60, 52), (200, 190, 175), (110, 120, 135), (240, 170, 60), (150, 150, 150)
LINKS = [('Hips', 'Spine'), ('Spine', 'Spine1'), ('Spine1', 'Spine2'), ('Spine2', 'Neck'), ('Neck', 'Head'),
         ('Spine2', 'LeftShoulder'), ('LeftShoulder', 'LeftArm'), ('LeftArm', 'LeftForeArm'), ('LeftForeArm', 'LeftHand'),
         ('Spine2', 'RightShoulder'), ('RightShoulder', 'RightArm'), ('RightArm', 'RightForeArm'), ('RightForeArm', 'RightHand'),
         ('Hips', 'LeftUpLeg'), ('LeftUpLeg', 'LeftLeg'), ('LeftLeg', 'LeftFoot'), ('LeftFoot', 'LeftToeBase'),
         ('Hips', 'RightUpLeg'), ('RightUpLeg', 'RightLeg'), ('RightLeg', 'RightFoot'), ('RightFoot', 'RightToeBase')]


def world(p):            # root space (x right, z forward) -> world with the hero facing +x (yaw 90)
    return (p[2], p[1], -p[0])


def qrot(q, v):
    x, y, z, w = q
    tx, ty, tz = 2 * (y * v[2] - z * v[1]), 2 * (z * v[0] - x * v[2]), 2 * (x * v[1] - y * v[0])
    return (v[0] + w * tx + (y * tz - z * ty), v[1] + w * ty + (z * tx - x * tz), v[2] + w * tz + (x * ty - y * tx))


def hull2d(pts):
    pts = sorted(set(pts))
    if len(pts) < 3:
        return pts
    def half(seq):
        h = []
        for p in seq:
            while len(h) >= 2 and (h[-1][0] - h[-2][0]) * (p[1] - h[-2][1]) - (h[-1][1] - h[-2][1]) * (p[0] - h[-2][0]) <= 0:
                h.pop()
            h.append(p)
        return h
    lo, up = half(pts), half(reversed(pts))
    return lo[:-1] + up[:-1]


def speed_color(v):
    t = max(0.0, min(1.0, v / 30.0))
    return (int(60 + 195 * t), int(170 - 120 * t), int(255 - 215 * t))


def head_points(fr):
    return [tuple(fr['p'][c] + o[c] for c in range(3)) for o in (qrot(fr['q'], h) for h in D['hull'])]


class Cam:
    def __init__(s, w, h, ppm, cx, cy):
        s.w, s.h, s.ppm, s.cx, s.cy = w, h, ppm, cx, cy

    def __call__(s, p):
        wp = world(p)
        sx = wp[0]
        sy = wp[1] * UP[1] + wp[2] * UP[2]
        return (s.cx + sx * s.ppm, s.cy - sy * s.ppm)


def depth(p):
    wp = world(p)
    return wp[1] * FWD[1] + wp[2] * FWD[2]


def ground_arc(d, cam, r0, r1, deg, fill, width=1):
    pts = []
    for k in range(-30, 31):
        ang = math.radians(deg) * k / 30
        pts.append(cam((r1 * math.sin(ang), 0, r1 * math.cos(ang))))
    if r0 is not None:
        inner = [cam((r0 * math.sin(math.radians(deg) * k / 30), 0, r0 * math.cos(math.radians(deg) * k / 30))) for k in range(30, -31, -1)]
        d.polygon(pts + inner, fill=fill)
    else:
        d.line(pts, fill=fill, width=width)


def tile(fr, prev, w, h):
    img = Image.new('RGB', (w, h), BG)
    d = ImageDraw.Draw(img)
    cam = Cam(w, h, a.ppm, w * .30, h * .70)
    ground_arc(d, cam, 2.2, 2.7, 10, (60, 80, 60))
    ground_arc(d, cam, None, 2.8, 72.5, (120, 90, 60), 1)
    bones = fr['bones']
    body_depth = depth(bones[ix['Spine2']])
    hp = head_points(fr)
    head_behind = depth(fr['p']) > body_depth
    def draw_head():
        poly = hull2d([tuple(round(x, 1) for x in cam(p)) for p in hp])
        d.polygon(poly, fill=METAL, outline=(200, 210, 220))
        rx, ry = cam(fr['ring']); d.ellipse((rx - 3, ry - 3, rx + 3, ry + 3), outline=(255, 255, 255))
    def draw_body():
        for a_, b_ in LINKS:
            d.line([cam(bones[ix[a_]]), cam(bones[ix[b_]])], fill=BODY, width=max(2, int(a.ppm * .09)))
        hx, hy = cam(bones[ix['Head']]); r = a.ppm * .11
        d.ellipse((hx - r, hy - r * 1.6, hx + r, hy + r * .4), fill=BODY)
    # trail of the head centre (previous frames)
    for q, nxt in zip(prev, prev[1:] + [fr]):
        d.line([cam(q['p']), cam(nxt['p'])], fill=speed_color(nxt['speed']), width=2)
    if head_behind:
        draw_head(); draw_body()
    else:
        draw_body()
    chain = [cam(p) for p in fr['chain']]
    d.line(chain, fill=CHAIN_T if fr['taut'] else CHAIN_S, width=2)
    if not head_behind:
        draw_head()
    gx, gy = cam(fr['grip']); d.ellipse((gx - 3, gy - 3, gx + 3, gy + 3), fill=(255, 80, 80))
    rel = fr['speed']
    slack = L - fr['span']
    tag = []
    if abs(fr['t'] - D['contactTime']) < 1 / 120:
        tag.append('КОНТАКТ Sim')
    if slack > .02 and rel > 8:
        tag.append('ПРОВИС %.0f см' % (slack * 100))
    if fr['grounded']:
        tag.append('земля')
    d.text((5, 4), 'f%d  тик %.1f  клип %.2f' % (fr['f'], fr['t'] * 30, fr['clip']), font=FONT_B, fill=(235, 235, 235))
    d.text((5, 22), '%.1f м/с  цепь %.2f/%.2f м' % (fr['speed'], fr['span'], L), font=FONT, fill=speed_color(fr['speed']))
    if tag:
        d.text((5, h - 20), '  '.join(tag), font=FONT_B, fill=(255, 120, 90) if 'ПРОВИС' in ' '.join(tag) else (255, 220, 120))
    if abs(fr['t'] - D['contactTime']) < 1 / 120:
        d.rectangle((0, 0, w - 1, h - 1), outline=(255, 220, 120), width=3)
    return img


def overview(frames, w, h, side):
    """Top (x right, z up the page) or side (z right, y up) view in root space, 100 px/m."""
    img = Image.new('RGB', (w, h), BG)
    d = ImageDraw.Draw(img)
    ppm = 95.0
    if side:
        cx, cy = w * .22, h * .82
        P = lambda p: (cx + p[2] * ppm, cy - p[1] * ppm)
        d.line([(0, cy), (w, cy)], fill=GROUND, width=2)
        for r, col in ((2.2, (90, 140, 90)), (2.7, (90, 140, 90)), (2.8, (170, 120, 70))):
            d.line([P((0, 0, r)), P((0, 1.0, r))], fill=col, width=1)
        d.rectangle([P((0, .9, 2.2)), P((0, .5, 2.7))], outline=(120, 200, 120))
        d.text((6, 4), 'Сбоку (z вперёд →, y вверх). Рамка: дуга 2,2–2,7 м, высота 0,5–0,9 м', font=FONT_B, fill=(230, 230, 230))
    else:
        cx, cy = w * .5, h * .78
        P = lambda p: (cx + p[0] * ppm, cy - p[2] * ppm)
        for r, deg, col in ((2.2, 10, (90, 140, 90)), (2.7, 10, (90, 140, 90)), (2.8, 72.5, (170, 120, 70))):
            pts = [P((r * math.sin(math.radians(deg) * k / 40), 0, r * math.cos(math.radians(deg) * k / 40))) for k in range(-40, 41)]
            d.line(pts, fill=col, width=2)
        d.text((6, 4), 'Сверху (x вправо, z вперёд ↑). Дуга урона 2,8 м ±72,5°, полоса контакта 2,2–2,7 м ±10°', font=FONT_B, fill=(230, 230, 230))
    contact = min(frames, key=lambda f: abs(f['t'] - D['contactTime']))
    bones = contact['bones']
    for a_, b_ in LINKS:
        d.line([P(bones[ix[a_]]), P(bones[ix[b_]])], fill=(140, 130, 120), width=3)
    for f in frames[::2]:
        d.line([P(f['grip']), P(f['ring'])], fill=(90, 90, 90) if not f['taut'] else (150, 110, 50), width=1)
    for q, nxt in zip(frames, frames[1:]):
        d.line([P(q['p']), P(nxt['p'])], fill=speed_color(nxt['speed']), width=3)
        d.line([P(q['grip']), P(nxt['grip'])], fill=(255, 90, 90), width=2)
    poly = hull2d([tuple(round(x, 1) for x in P(p)) for p in head_points(contact)])
    d.polygon(poly, outline=(255, 220, 120))
    x, y = P(contact['p']); d.ellipse((x - 5, y - 5, x + 5, y + 5), fill=(255, 220, 120))
    d.text((x + 8, y - 8), 'тик контакта Sim: %.1f м/с' % contact['speed'], font=FONT, fill=(255, 220, 120))
    d.text((6, h - 20), 'путь центра головы: синий медленно → красный 30 м/с; красная линия — хват (левая кисть)', font=FONT, fill=(200, 200, 200))
    return img


frames = D['frames']
show = frames[::a.every]
tw, th = int(5.4 * a.ppm), int(3.6 * a.ppm)
cols = min(a.cols, len(show))
rows = (len(show) + cols - 1) // cols
ow, oh = cols * tw // 2, 430
table_h = 22 + 18 * len(D['rows'])
W, H = cols * tw, 40 + table_h + oh + rows * th
sheet = Image.new('RGB', (W, H), (12, 14, 18))
d = ImageDraw.Draw(sheet)
d.text((10, 8), D['title'] + '   |   кадры 60 к/с, масштаб игры %.1f px/м (CombatSize 6,2, 1080p), камера 48°' % a.ppm,
       font=FONT_T, fill=(240, 240, 240))
y = 40
for r in D['rows']:
    col = (120, 220, 120) if r['pass'] == 'ok' else (255, 110, 90) if r['pass'] == 'FAIL' else (180, 180, 180)
    d.text((14, y), '%-5s %-4s %s: %s  (порог %s)' % (r['pass'], r['id'], r['name'], r['value'], r['target']), font=FONT, fill=col)
    y += 18
y = 40 + table_h
sheet.paste(overview(frames, ow, oh, False), (0, y))
sheet.paste(overview(frames, W - ow, oh, True), (ow, y))
y += oh
for i, fr in enumerate(show):
    k = frames.index(fr)
    prev = frames[max(0, k - 8):k]
    sheet.paste(tile(fr, prev, tw, th), ((i % cols) * tw, y + (i // cols) * th))
for c in range(1, cols):
    d.line([(c * tw, y), (c * tw, H)], fill=(12, 14, 18), width=2)
sheet.save(a.out, quality=90)
print('SHEET', a.out, sheet.size, 'tiles', len(show), 'solver ms/frame', D.get('solverMsPerFrame'))
