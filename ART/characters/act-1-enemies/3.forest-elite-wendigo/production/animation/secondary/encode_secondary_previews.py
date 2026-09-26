"""Encode the reviewed side/game PNG sequences into compact looping WebPs."""

import sys
from pathlib import Path

from PIL import Image


root = Path(sys.argv[1]).resolve()
clips = {"Idle": (60, 2), "Walk": (12, 3), "Hit": (14, 1), "Death": (45, 1)}
for clip, (end, repeat) in clips.items():
    for view in ("game", "side"):
        frame_dir = root / "continuous" / f"AN_ForestWendigo_{clip}" / view / "frames"
        files = [frame_dir / f"{index:04d}.png" for index in range(end)]
        if any(not path.is_file() for path in files):
            raise FileNotFoundError(f"Missing continuous frames for {clip} {view}")
        base = []
        for path in files:
            with Image.open(path) as source:
                base.append(source.convert("RGB").copy())
        frames = base * repeat
        path = root / "previews" / f"{clip}_{view}.webp"
        path.parent.mkdir(parents=True, exist_ok=True)
        frames[0].save(path, format="WEBP", save_all=True, append_images=frames[1:],
                       duration=33, loop=0, quality=84, method=5)
        print(path)
