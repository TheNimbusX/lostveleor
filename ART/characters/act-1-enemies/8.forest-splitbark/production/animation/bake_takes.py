"""Author + bake all ForestSplitter game takes.

blender -b ../rig/ForestSplitter_Rig.blend -P bake_takes.py
 -> ForestSplitter_Anim_r01.blend   editable: keyed FK + IK foot controls per take (actions *_CTRL)
 -> ForestSplitter_Baked_r01.blend  deform-only FK keys every 1/4 frame, LINEAR, no constraints/CTRL bones
 -> bake_report.json                per-frame metrics (reach, IK error, skin min z, beak, ankles)
The procedural source of every take is takes_*.py; this script never edits the rig blend.
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
from takes import R01_ORDER as ORDER, TAKES  # noqa: E402  (r01 only; roll takes: bake_roll.py)

STEP = 4  # samples per frame in the baked take
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.render.fps = 30
arm = bpy.data.objects[sp.ARM]
mesh = bpy.data.objects[sp.MESH]
names = bl.deform_names(arm)
report = {"takes": {}}
baked = {}
controls = {}
if arm.animation_data:
    arm.animation_data.action = None

for take in ORDER:
    spec = TAKES[take]
    n = spec["frames"]
    # 1. editable control values + metrics (integer frames; no action assigned while evaluating)
    rows, snaps = [], []
    for f in range(n + 1):
        pose = spec["fn"](float(f))
        pulled = sp.apply(arm, pose)
        snaps.append(bl.control_values(arm))
        rows.append(measure.frame_metrics(arm, mesh, pose, pulled))
    controls[take] = snaps
    # 2. dense deform samples (evaluated with constraints)
    frames, bases = [], []
    for i in range(n * STEP + 1):
        t = i / STEP
        sp.apply(arm, spec["fn"](t))
        frames.append(t)
        bases.append(bl.basis_from_pose(arm, bl.sample(arm, names)))
    baked[take] = (frames, bases)
    report["takes"][take] = {"frames": [0, n], "loop": spec["loop"], "rows": rows}
    print("AUTHORED", take, flush=True)

# editable scene
for take in ORDER:
    bl.write_control_action(arm, take + "_CTRL", controls[take], TAKES[take]["loop"])
arm.animation_data.action = bpy.data.actions[ORDER[0] + "_CTRL"]
scene.frame_start, scene.frame_end = 0, TAKES[ORDER[0]]["frames"]
scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "ForestSplitter_Anim_r01.blend"))
print("SAVED_EDITABLE", flush=True)

# baked deform-only scene
arm.animation_data.action = None
for act in list(bpy.data.actions):
    bpy.data.actions.remove(act)
sp.reset(arm)
bl.strip_controls(arm)
for take in ORDER:
    frames, bases = baked[take]
    bl.write_action(arm, take, frames, bases, TAKES[take]["loop"])
arm.animation_data.action = bpy.data.actions[ORDER[0]]
scene.frame_start, scene.frame_end = 0, TAKES[ORDER[0]]["frames"]
scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / "ForestSplitter_Baked_r01.blend"))
(HERE / "bake_report.json").write_text(json.dumps(report), encoding="utf-8")
print("BAKE_DONE", flush=True)
