"""Append the r02 roll actions (roll_actions_r02.blend) to a copy of an r01 blend. The r01 file is not saved.

blender -b ForestSplitter_Baked_r01.blend -P merge_r02.py -- baked     -> ForestSplitter_Baked_r02.blend
blender -b ForestSplitter_Anim_r01.blend  -P merge_r02.py -- editable  -> ForestSplitter_Anim_r02.blend
baked: takes the deform-only <Take> actions; editable: the <Take>_CTRL actions. The r01 actions are left as
they are (not re-baked), so every old take stays bit-identical.
"""
import sys
from pathlib import Path

import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from takes import R02_NEW, TAKES  # noqa: E402

kind = sys.argv[sys.argv.index("--") + 1]
want = {t: (t if kind == "baked" else t + "_CTRL") for t in R02_NEW}
out = HERE / ("ForestSplitter_Baked_r02.blend" if kind == "baked" else "ForestSplitter_Anim_r02.blend")
src = Path(bpy.data.filepath).name
assert src == ("ForestSplitter_Baked_r01.blend" if kind == "baked" else "ForestSplitter_Anim_r01.blend"), src
clash = [n for n in want.values() if n in bpy.data.actions]
assert not clash, clash
with bpy.data.libraries.load(str(HERE / "roll_actions_r02.blend"), link=False) as (src_data, dst_data):
    missing = [n for n in want.values() if n not in src_data.actions]
    assert not missing, missing
    dst_data.actions = list(want.values())
arm = bpy.data.objects["ARM_ForestSplitter"]
if kind == "baked":
    deform = {b.name for b in arm.data.bones}
for take, name in want.items():
    act = bpy.data.actions[name]
    assert act.name == name, act.name
    act.use_fake_user = True
    act.use_frame_range = True
    act.frame_start, act.frame_end = 0, TAKES[take]["frames"]
    act.use_cyclic = TAKES[take]["loop"]
    if kind == "baked":
        paths = {fc.data_path.split('"')[1] for fc in act.layers[0].strips[0].channelbag(act.slots[0]).fcurves}
        assert paths <= deform, paths - deform
print("MERGED", kind, [a.name for a in bpy.data.actions], flush=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(out), copy=True)
print("SAVED", out, flush=True)
