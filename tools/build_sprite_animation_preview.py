from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math


ROOT = Path(r"C:\Users\d.grab\Desktop\the-game")
SPRITES = ROOT / "razlom" / "Assets" / "Resources" / "CharacterSprites"
OUTPUT = ROOT / "designed-images" / "deliverables" / "razlom-character-production-pack-v5" / "PREVIEW_exact_art_animation.gif"

WIDTH, HEIGHT = 960, 540
FPS = 12
FRAME_COUNT = 72


def load(character: str, name: str) -> Image.Image:
    return Image.open(SPRITES / character / f"{name}.png").convert("RGBA")


def sequence(frame: int, character: str) -> tuple[str, float, float, float]:
    """Return sprite name, x offset, y offset and scale multiplier."""
    if character == "Pelag":
        if frame < 12:
            return ("idle_0" if frame < 6 else "idle_1", 0, math.sin(frame * 0.55) * 2, 1.0)
        if frame < 24:
            return ("run_0" if (frame // 2) % 2 == 0 else "run_1", (frame - 12) * 3, -abs(math.sin(frame * 1.4)) * 4, 1.0)
        if frame < 36:
            attack = ["idle_1", "attack_0", "attack_0", "attack_1", "attack_1", "attack_1", "attack_0", "attack_0", "idle_1", "idle_1", "idle_0", "idle_0"]
            return (attack[frame - 24], 34 + min(frame - 24, 6) * 3, 0, 1.0 + (0.025 if 27 <= frame <= 30 else 0))
        if frame < 48:
            hook = ["attack_0", "hook_0", "hook_0", "hook_1", "hook_1", "hook_1", "hook_0", "hook_0", "idle_1", "idle_1", "idle_0", "idle_0"]
            return (hook[frame - 36], 50, 0, 1.0)
        if frame < 58:
            hit = ["idle_0", "hit_0", "hit_0", "hit_1", "hit_1", "stun_0", "stun_0", "idle_1", "idle_1", "idle_0"]
            return (hit[frame - 48], 50 - max(0, frame - 49) * 2, 0, 1.0)
        death = ["knockback_0"] * 5 + ["death_0"] * 9
        return (death[min(frame - 58, len(death) - 1)], 34, 0, 1.0)

    if frame < 12:
        return ("idle_0" if frame < 6 else "idle_1", 0, math.sin(frame * 0.5 + 1.5) * 2, 1.0)
    if frame < 24:
        return ("walk_0" if (frame // 3) % 2 == 0 else "idle_1", -(frame - 12) * 2, -abs(math.sin(frame * 1.2)) * 3, 1.0)
    if frame < 36:
        attack = ["idle_1", "attack_0", "attack_0", "attack_1", "attack_1", "attack_1", "attack_0", "attack_0", "idle_1", "idle_1", "idle_0", "idle_0"]
        return (attack[frame - 24], -24 - min(frame - 24, 6) * 2, 0, 1.0 + (0.02 if 27 <= frame <= 30 else 0))
    if frame < 48:
        block = ["idle_0", "block_0", "block_0", "block_0", "hit_0", "hit_0", "block_0", "block_0", "idle_1", "idle_1", "idle_0", "idle_0"]
        return (block[frame - 36], -38, 0, 1.0)
    if frame < 58:
        hit = ["idle_0", "hit_0", "hit_0", "hit_1", "hit_1", "block_0", "block_0", "idle_1", "idle_1", "idle_0"]
        return (hit[frame - 48], -38 + max(0, frame - 49) * 2, 0, 1.0)
    death = ["death_0"] * 5 + ["death_1"] * 9
    return (death[min(frame - 58, len(death) - 1)], -24, 0, 1.0)


def action_label(frame: int) -> str:
    if frame < 12:
        return "IDLE"
    if frame < 24:
        return "MOVE"
    if frame < 36:
        return "ATTACK"
    if frame < 48:
        return "ABILITY / BLOCK"
    if frame < 58:
        return "HIT REACTION"
    return "DEATH"


def fit_sprite(sprite: Image.Image, max_w: int, max_h: int, scale: float) -> Image.Image:
    bbox = sprite.getbbox()
    if bbox is None:
        return sprite
    cropped = sprite.crop(bbox)
    ratio = min(max_w / cropped.width, max_h / cropped.height) * scale
    size = (max(1, round(cropped.width * ratio)), max(1, round(cropped.height * ratio)))
    return cropped.resize(size, Image.Resampling.LANCZOS)


def font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        Path(r"C:\Windows\Fonts\arialbd.ttf"),
        Path(r"C:\Windows\Fonts\segoeuib.ttf"),
    ]
    for path in candidates:
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def build() -> None:
    cache: dict[tuple[str, str], Image.Image] = {}
    frames: list[Image.Image] = []
    title_font = font(28)
    label_font = font(20)
    small_font = font(14)

    for index in range(FRAME_COUNT):
        canvas = Image.new("RGBA", (WIDTH, HEIGHT), (17, 23, 35, 255))
        draw = ImageDraw.Draw(canvas)

        # Graphic-novel arena floor and restrained speed lines.
        draw.polygon([(40, 470), (480, 250), (920, 470), (480, 525)], fill=(42, 58, 73, 255))
        draw.line([(40, 470), (480, 250), (920, 470)], fill=(83, 126, 133, 255), width=3)
        draw.line([(480, 250), (480, 525)], fill=(69, 94, 108, 255), width=2)
        for y in (100, 145, 190):
            draw.line([(0, y), (180, y - 35)], fill=(29, 40, 56, 255), width=3)
            draw.line([(780, y - 20), (960, y - 55)], fill=(29, 40, 56, 255), width=3)

        label = action_label(index)
        draw.text((WIDTH // 2, 28), "RAZLOM — EXACT ART SPRITES", font=title_font, anchor="ma", fill=(249, 222, 111, 255))
        draw.rounded_rectangle((390, 65, 570, 101), radius=14, fill=(105, 40, 55, 255), outline=(243, 106, 94, 255), width=2)
        draw.text((480, 83), label, font=small_font, anchor="mm", fill=(255, 244, 218, 255))

        for character, center_x, accent in (("Pelag", 290, (65, 204, 212, 255)), ("Orvill", 670, (237, 166, 75, 255))):
            name, dx, dy, scale = sequence(index, character)
            key = (character, name)
            if key not in cache:
                cache[key] = load(character, name)
            sprite = fit_sprite(cache[key], 360, 390, scale)
            x = round(center_x + dx - sprite.width / 2)
            y = round(470 + dy - sprite.height)
            canvas.alpha_composite(sprite, (x, y))
            draw.text((center_x, 495), character.upper(), font=label_font, anchor="mm", fill=accent)

        draw.text((480, 525), "23 кадра из production pack • без 3D и без перерисовки", font=small_font, anchor="mm", fill=(166, 181, 196, 255))
        frames.append(canvas.convert("P", palette=Image.Palette.ADAPTIVE, colors=255))

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(
        OUTPUT,
        save_all=True,
        append_images=frames[1:],
        duration=round(1000 / FPS),
        loop=0,
        optimize=False,
        disposal=2,
    )
    print(OUTPUT)


if __name__ == "__main__":
    build()
