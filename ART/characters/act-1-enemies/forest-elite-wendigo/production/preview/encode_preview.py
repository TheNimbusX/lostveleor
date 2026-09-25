"""Encode one reviewed PNG sequence as a compact animated WebP with Pillow."""

import argparse
from pathlib import Path

from PIL import Image, features


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("frames", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--max-side", type=int, default=512)
    args = parser.parse_args()
    if not features.check("webp"):
        raise RuntimeError("This Pillow build cannot encode WebP")
    files = sorted(args.frames.glob("[0-9][0-9][0-9][0-9].png"))
    if not files:
        raise RuntimeError("No numbered PNG frames to encode")
    images = []
    for file in files:
        with Image.open(file) as source:
            frame = source.convert("RGB")
            frame.thumbnail((args.max_side, args.max_side), Image.Resampling.LANCZOS)
            images.append(frame.copy())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    images[0].save(args.output, format="WEBP", save_all=True,
                   append_images=images[1:], duration=round(1000 / args.fps),
                   loop=0, quality=81, method=5)
    print(f"ANIMATED_WEBP {args.output} FRAMES {len(images)}")


if __name__ == "__main__":
    main()
