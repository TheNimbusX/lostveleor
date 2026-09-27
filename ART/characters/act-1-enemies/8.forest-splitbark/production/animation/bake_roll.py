"""Author + bake the 27.09 roll takes (r02) exactly like bake_takes.py did for r01, without touching r01.

blender -b ../rig/ForestSplitter_Rig.blend -P bake_roll.py
 -> roll_actions_r02.blend   the new actions only: <Take>_CTRL (editable controls, Bezier per integer frame)
                             and <Take> (deform-only FK keys every 1/4 frame, LINEAR) - same writers as r01
 -> bake_report_r02.json     per-frame metrics of the new takes (reach, IK pull, skin min z, ankles)
then merge_r02.py copies them into ForestSplitter_Anim_r02.blend / ForestSplitter_Baked_r02.blend
(= the r01 blends + the new actions; the r01 actions are not re-baked, so they stay bit-identical).
"""
import json
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import bake_lib as bl  # noqa: E402
import measure  # noqa: E402
import splitter_pose as sp  # noqa: E402
from takes import R02_NEW, TAKES  # noqa: E402

STEP = 4  # samples per frame, as r01
LIB = HERE / "roll_actions_r02.blend"
scene = bpy.context.scene
scene.render.fps = 30
arm = bpy.data.objects[sp.ARM]
mesh = bpy.data.objects[sp.MESH]
names = bl.deform_names(arm)
if arm.animation_data:
    arm.animation_data.action = None
report = {"takes": {}}
made = []
for take in R02_NEW:
    spec = TAKES[take]
    n = spec["frames"]
    rows, snaps = [], []
    for f in range(n + 1):
        pose = spec["fn"](float(f))
        pulled = sp.apply(arm, pose)
        snaps.append(bl.control_values(arm))
        rows.append(measure.frame_metrics(arm, mesh, pose, pulled))
    frames, bases = [], []
    for i in range(n * STEP + 1):
        t = i / STEP
        sp.apply(arm, spec["fn"](t))
        frames.append(t)
        bases.append(bl.basis_from_pose(arm, bl.sample(arm, names)))
    report["takes"][take] = {"frames": [0, n], "loop": spec["loop"], "rows": rows}
    made.append(bl.write_control_action(arm, take + "_CTRL", snaps, spec["loop"]))
    arm.animation_data.action = None
    made.append(bl.write_action(arm, take, frames, bases, spec["loop"]))
    arm.animation_data.action = None
    print("BAKED", take, n, flush=True)

sp.reset(arm)
bpy.data.libraries.write(str(LIB), set(made), fake_user=True)
(HERE / "bake_report_r02.json").write_text(json.dumps(report), encoding="utf-8")
print("BAKE_ROLL_DONE", LIB, flush=True)
