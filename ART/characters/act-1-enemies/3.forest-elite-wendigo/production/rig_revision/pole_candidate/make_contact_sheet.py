"""Make a visual comparison of the read-only pole-rig evaluation renders."""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

here = Path(__file__).resolve().parent
samples = [
    ("Claw f18", "Claw_18_game.png"),
    ("Leap crouch f23", "Leap_23_front.png"),
    ("Leap landing f33", "Leap_33_game.png"),
    ("Walk f06", "Walk_06_game.png"),
    ("Death f45", "Death_45_front.png"),
]
side = 500
header = 50
sheet = Image.new("RGB", (side * 2, (side + header) * len(samples)), (30, 32, 36))
draw = ImageDraw.Draw(sheet)
font = ImageFont.truetype("arial.ttf", 26)
for row, (label, name) in enumerate(samples):
    top = row * (side + header)
    for col, variant in enumerate(("source", "candidate")):
        original = Image.open(here / f"compare_{variant}" / name).convert("RGB")
        original.thumbnail((side, side))
        sheet.paste(original, (col * side + (side-original.width)//2, top+header+(side-original.height)//2))
        draw.text((col*side+20, top+12), f"{label}: {variant}", fill=(245, 241, 224), font=font)
sheet.save(here / "source_vs_pole_candidate.png")
print(here / "source_vs_pole_candidate.png")
