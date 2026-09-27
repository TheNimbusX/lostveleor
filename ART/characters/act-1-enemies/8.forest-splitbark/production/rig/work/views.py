"""Quick textured views of the raw candidate (head close-ups, underside) for rig landmark placement."""
import sys
from pathlib import Path

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import render_util as ru  # noqa: E402

out = Path(sys.argv[sys.argv.index("--") + 1])
out.mkdir(parents=True, exist_ok=True)
ru.setup_scene("WORKBENCH", 800)
shots = {
    "head_front": ((0, -1.6, 0.55), (0, -0.5, 0.55), None, 85),
    "head_side": ((-1.3, -0.55, 0.55), (0, -0.45, 0.55), None, 85),
    "head_below": ((0, -1.2, -0.3), (0, -0.45, 0.5), None, 70),
    "under": ((0, -0.01, -3.0), (0, 0, 0.3), 1.8, 50),
    "side_ortho": ((-3.0, 0, 0.6), (0, 0, 0.6), 1.7, 50),
    "front_ortho": ((0, -3.0, 0.6), (0, 0, 0.6), 1.7, 50),
    "top_ortho": ((0, 0, 3.5), (0, 0.0001, 0), 1.7, 50),
}
for name, (eye, tgt, ortho, lens) in shots.items():
    ru.shot(out / f"{name}.png", eye, tgt, ortho=ortho, lens=lens)
print("VIEWS_DONE")
