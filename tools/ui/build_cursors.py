"""Slice the approved unified cursor artwork into game-sized cursor textures.

This is a technical crop/scale pass. The artwork and materials come from the
approved single imagegen sheet; no substitute cursor drawings are introduced.
"""

from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "ART/UI/cursors-2026-09-24/unified-concept-v2.png"
OUTPUT = ROOT / "razlom/Assets/Resources/UI/Cursors"
PREVIEW = ROOT / "ART/UI/cursors-2026-09-24/installed-preview.png"
SIZE = 48
ART_SIZE = 42

# Non-overlapping regions of the approved five-icon sheet.
REGIONS = {
    "pointer": (90, 50, 470, 490),
    "attack": (520, 50, 980, 490),
    "interact": (1000, 50, 1450, 490),
    "aim": (280, 510, 750, 960),
    "blocked": (840, 510, 1250, 960),
}


def extract(source: Image.Image, region: tuple[int, int, int, int]) -> Image.Image:
    area = source.crop(region)
    # The concept has nearly transparent ambient brown around each drawing.
    # Remove that ambient alpha before resizing, retaining the solid bevels.
    alpha = area.getchannel("A")
    solid = alpha.point(lambda value: 255 if value >= 200 else 0)
    box = solid.getbbox()
    if box is None:
        raise ValueError(f"Cursor art is missing from region {region}")
    margin = 12
    box = (
        max(0, box[0] - margin), max(0, box[1] - margin),
        min(area.width, box[2] + margin), min(area.height, box[3] + margin),
    )
    area = area.crop(box)
    alpha = area.getchannel("A").point(
        lambda value: max(0, min(255, round((value - 72) * 255 / 183)))
    )
    area.putalpha(alpha)
    scale = ART_SIZE / max(area.size)
    size = tuple(max(1, round(dimension * scale)) for dimension in area.size)
    area = area.resize(size, Image.Resampling.LANCZOS)
    result = Image.new("RGBA", (SIZE, SIZE))
    result.alpha_composite(area, ((SIZE - size[0]) // 2, (SIZE - size[1]) // 2))
    return result


def main() -> None:
    source = Image.open(SOURCE).convert("RGBA")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    previews = {}
    for name, region in REGIONS.items():
        image = extract(source, region)
        image.save(OUTPUT / f"{name}.png")
        previews[name] = image

    # Only a review image; never imported by Unity.
    sheet = Image.new("RGB", (5 * 160, 2 * 160), "#292d30")
    draw = ImageDraw.Draw(sheet)
    for index, (name, image) in enumerate(previews.items()):
        for row, size in enumerate((48, 96)):
            x = index * 160 + (160 - size) // 2
            y = row * 160 + 12
            sample = image if size == SIZE else image.resize((size, size), Image.Resampling.NEAREST)
            sheet.paste(sample, (x, y), sample)
        draw.text((index * 160 + 12, 290), name, fill="#f2e4d1")
    sheet.save(PREVIEW)


if __name__ == "__main__":
    main()
