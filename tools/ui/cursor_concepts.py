"""Preview-only cursor directions. Does not write into Unity assets."""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "ART/UI/cursors-2026-09-24/proposals-v2.png"
S = 5
N = 48
DARK = "#1d2928"
CREAM = "#f8f0dc"
STEEL = "#d5e1dd"
BRONZE = "#bc7b4c"
CORAL = "#e9654e"
TEAL = "#55b5aa"


def pen():
    im = Image.new("RGBA", (N*S, N*S), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)


def poly(d, pts, fill, outline=DARK, width=2):
    pts = [(round(x*S), round(y*S)) for x,y in pts]
    d.polygon(pts, fill=fill)
    if outline:
        d.line(pts + pts[:1], fill=outline, width=round(width*S), joint="curve")


def line(d, pts, fill, width=2):
    d.line([(round(x*S), round(y*S)) for x,y in pts], fill=fill,
           width=round(width*S), joint="curve")


def ellipse(d, box, fill=None, outline=None, width=2):
    d.ellipse(tuple(round(v*S) for v in box), fill=fill, outline=outline,
              width=round(width*S))


def forged(kind, d):
    if kind == "pointer":
        # Fine sabre-shaped pointer; the live hotspot stays at the upper tip.
        poly(d, [(8,5),(14,9),(31,28),(24,28),(18,39),(14,29),(8,32)], CREAM, width=2.2)
        poly(d, [(10,8),(14,11),(27,26),(22,25),(14,20)], STEEL, outline=None)
        line(d, [(9,5),(17,13),(24,24)], "#ffffff", 1.4)
        line(d, [(15,29),(21,27)], BRONZE, 2)
    elif kind == "move":
        ellipse(d, (10,10,38,38), outline=DARK, width=4)
        ellipse(d, (10,10,38,38), outline=CREAM, width=2)
        poly(d, [(24,6),(20,17),(28,17)], BRONZE, width=1.5)
        poly(d, [(24,42),(20,31),(28,31)], BRONZE, width=1.5)
        ellipse(d, (20,20,28,28), fill=CREAM, outline=DARK, width=1.4)
    elif kind == "attack":
        line(d, [(10,37),(14,33),(23,27),(32,17),(37,9)], DARK, 7)
        line(d, [(10,37),(14,33),(23,27),(32,17),(37,9)], CREAM, 4.2)
        line(d, [(19,29),(28,20),(35,11)], STEEL, 1.8)
        line(d, [(11,25),(18,32)], BRONZE, 3.5)
        ellipse(d, (7,35,13,41), fill=BRONZE, outline=DARK, width=1.2)
    else:
        ellipse(d, (9,10,39,38), outline=DARK, width=5)
        ellipse(d, (9,10,39,38), outline=CREAM, width=2.5)
        ellipse(d, (16,17,32,31), fill=TEAL, outline=DARK, width=2)
        line(d, [(24,8),(24,14)], BRONZE, 3)


def painted(kind, d):
    if kind == "pointer":
        poly(d, [(7,5),(11,7),(15,12),(21,16),(31,26),(25,29),(21,39),(17,36),
                 (13,29),(8,31),(9,25)], DARK, outline=None)
        poly(d, [(8,6),(13,12),(19,17),(28,26),(22,27),(20,35),(16,29),(10,29)], CREAM, outline=None)
        line(d, [(10,7),(14,14),(23,23)], "#fffaf0", 2)
        line(d, [(13,29),(18,35)], CORAL, 2.5)
    elif kind == "move":
        # Broken, hand-painted arcs rather than a rigid compass rose.
        d.arc(tuple(round(v*S) for v in (9,9,39,39)), 205, 335, fill=DARK, width=round(5*S))
        d.arc(tuple(round(v*S) for v in (9,9,39,39)), 205, 335, fill=CREAM, width=round(2.8*S))
        d.arc(tuple(round(v*S) for v in (9,9,39,39)), 22, 155, fill=DARK, width=round(5*S))
        d.arc(tuple(round(v*S) for v in (9,9,39,39)), 22, 155, fill=CREAM, width=round(2.8*S))
        poly(d, [(29,10),(39,14),(33,22)], CORAL, width=1.7)
        ellipse(d, (21,21,27,27), fill=CREAM)
    elif kind == "attack":
        line(d, [(10,36),(17,31),(29,21),(37,10)], DARK, 10)
        line(d, [(10,36),(17,31),(29,21),(37,10)], CREAM, 6)
        line(d, [(17,31),(29,21),(37,10)], "#ffffff", 2.3)
        poly(d, [(10,26),(16,31),(15,38),(8,36)], CORAL, width=1.6)
        poly(d, [(34,8),(40,6),(39,13)], CREAM, outline=None)
    else:
        poly(d, [(24,7),(34,13),(40,25),(34,35),(23,40),(13,34),(8,23),(14,13)], DARK, outline=None)
        poly(d, [(24,10),(33,16),(37,25),(32,33),(23,37),(15,32),(11,23),(16,15)], CREAM, outline=None)
        line(d, [(17,25),(22,30),(32,18)], TEAL, 3.5)


def quiet(kind, d):
    if kind == "pointer":
        poly(d, [(7,5),(8,33),(15,27),(20,39),(25,37),(20,25),(31,24)], DARK, outline=None)
        poly(d, [(8,6),(10,29),(15,24),(21,37),(23,36),(18,24),(28,23)], CREAM, outline=None)
    elif kind == "move":
        ellipse(d, (11,11,37,37), outline=DARK, width=5)
        ellipse(d, (11,11,37,37), outline=CREAM, width=2.4)
        line(d, [(24,5),(24,12)], CREAM, 2.5)
        line(d, [(24,36),(24,43)], CREAM, 2.5)
        line(d, [(5,24),(12,24)], CREAM, 2.5)
        line(d, [(36,24),(43,24)], CREAM, 2.5)
    elif kind == "attack":
        # Open reticle: less coverage of the target than the old four-point star.
        for pts in [[(10,18),(10,10),(18,10)],[(30,10),(38,10),(38,18)],
                    [(10,30),(10,38),(18,38)],[(30,38),(38,38),(38,30)]]:
            line(d, pts, DARK, 5)
            line(d, pts, CREAM, 2.4)
        line(d, [(21,24),(27,24)], CORAL, 2.4)
    else:
        ellipse(d, (9,9,39,39), outline=DARK, width=5)
        ellipse(d, (9,9,39,39), outline=CREAM, width=2.4)
        line(d, [(19,25),(23,29),(31,18)], DARK, 5)
        line(d, [(19,25),(23,29),(31,18)], TEAL, 2.6)


makers = [forged, painted, quiet]
titles = ["A · КОВАНАЯ СТАЛЬ", "B · ЖИВАЯ КИСТЬ", "C · ТИХИЙ УКАЗАТЕЛЬ"]
subtitles = ["Сабельный силуэт, металл и медь", "Тёплый живой штрих без геометрии", "Неброский, с минимумом деталей"]
kinds = ["pointer", "move", "attack", "interact"]
labels = ["Обычный", "Движение", "Атака", "Взаимодействие"]
font_path = ROOT / "ART/HUD/typography/Rubik-Regular.ttf"
bold_path = ROOT / "ART/HUD/typography/Rubik-SemiBold.ttf"
font = ImageFont.truetype(str(font_path), 21)
small = ImageFont.truetype(str(font_path), 16)
bold = ImageFont.truetype(str(bold_path), 27)

W, H = 1340, 800
board = Image.new("RGB", (W,H), "#172227")
d = ImageDraw.Draw(board)
d.text((36,26), "КУРСОРЫ · ТРИ НАПРАВЛЕНИЯ", font=bold, fill="#f7edd7")
d.text((37,63), "Только концепты — в игру не подключены. Сверху 2×, снизу реальный размер 48 px.",
       font=small, fill="#aeb9b3")

for row, (make, title, subtitle) in enumerate(zip(makers, titles, subtitles)):
    y = 106 + row*225
    d.rounded_rectangle((24,y,1316,y+207), radius=18, fill="#20313a", outline="#42545a", width=2)
    d.text((45,y+18), title, font=font, fill="#fff3db")
    d.text((45,y+51), subtitle, font=small, fill="#bdcac7")
    for col, (kind,label) in enumerate(zip(kinds, labels)):
        x = 400 + col*222
        d.rounded_rectangle((x,y+16,x+205,y+190), radius=12, fill="#bbaa88")
        d.rectangle((x,y+128,x+205,y+190), fill="#30433d")
        d.text((x+12,y+23), label, font=small, fill="#233238")
        sprite, sd = pen()
        make(kind, sd)
        sprite = sprite.resize((N,N), Image.Resampling.LANCZOS)
        large = sprite.resize((N*2,N*2), Image.Resampling.NEAREST)
        board.paste(large, (x+55,y+35), large)
        board.paste(sprite, (x+79,y+131), sprite)

OUT.parent.mkdir(parents=True, exist_ok=True)
board.save(OUT)
print(OUT)
