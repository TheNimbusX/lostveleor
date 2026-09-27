"""Close-up check of single authored frames on the rig (no bake, nothing saved).

blender -b ../rig/ForestSplitter_Rig.blend -P pose_closeup.py -- <Take> <out_dir> f1 f2 ...
"""
import sys
from pathlib import Path

import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import render_setup as rs  # noqa: E402
import splitter_pose as sp  # noqa: E402
from takes import TAKES  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
take, out, frames = args[0], Path(args[1]), [float(a) for a in args[2:]]
spec = next(v for k, v in TAKES.items() if k.endswith(take))
scene = rs.setup()
rs.ground()
arm = bpy.data.objects[sp.ARM]
views = {
    "head34": ((1.3, -1.9, 1.0), (0.0, -0.55, 0.45)),
    "side": ((2.4, -0.3, 0.6), (0.0, -0.3, 0.5)),
    "top": ((0.6, -1.6, 2.4), (0.0, -0.1, 0.5)),
}
cam = rs.camera("CAM_Close")
cam.data.lens = 50
scene.camera = cam
game = rs.game_camera(dist=2.6)
scene.render.resolution_x = scene.render.resolution_y = 360
for f in frames:
    sp.apply(arm, spec["fn"](f))
    for name, (eye, tgt) in views.items():
        rs.look(cam, Vector(eye), Vector(tgt))
        scene.camera = cam
        scene.render.filepath = str(out / f"{take}_{name}_{f:05.2f}.png")
        bpy.ops.render.render(write_still=True)
    scene.camera = game
    scene.render.filepath = str(out / f"{take}_game_{f:05.2f}.png")
    bpy.ops.render.render(write_still=True)
print("CLOSEUP_DONE", flush=True)
