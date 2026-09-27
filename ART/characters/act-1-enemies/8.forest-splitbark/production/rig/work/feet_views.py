import sys
from pathlib import Path
import bpy
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import render_util as ru
out = Path(sys.argv[sys.argv.index("--") + 1]); out.mkdir(parents=True, exist_ok=True)
ru.setup_scene("WORKBENCH", 700)
shots = {
    "feet_side_R": ((-2.5, 0.05, 0.25), (-0.4, 0.05, 0.25), 1.4),
    "feet_top_R": ((-0.42, 0.05, 2.0), (-0.42, 0.0501, 0.0), 1.4),
    "feet_front": ((0, -2.5, 0.2), (0, 0, 0.2), 1.6),
    "feet_back": ((0, 2.5, 0.2), (0, 0, 0.2), 1.6),
}
for n, (e, t, o) in shots.items():
    ru.shot(out / f"{n}.png", e, t, ortho=o)
print("FEET_DONE")
