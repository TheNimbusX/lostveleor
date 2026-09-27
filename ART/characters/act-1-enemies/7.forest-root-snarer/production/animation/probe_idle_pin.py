"""Probe the Idle slab contacts (solver only, nothing keyed or saved).
blender -b ForestRootSnarer_Anim_r01.blend -P probe_idle_pin.py -- [nopin]"""
import sys
from pathlib import Path
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core, clip_idle_hit  # noqa: E402

rig = anim_core.Rig(bpy.data.objects[anim_core.ARM_NAME], bpy.data.objects[anim_core.MESH_NAME])
if "nopin" not in sys.argv:
    clip_idle_hit.prepare_idle(rig)
TOUCH = 0.003
for s in ("L", "R"):
    first, worst, zmin, zmax_low = {}, 0.0, 0.0, 0.0
    for f in range(0, 61):
        M, info = rig.solve(clip_idle_hit.idle_pose(f))
        tr = M[f"{s}_arm_lower"] @ rig.rest[f"{s}_arm_lower"].inverted()
        pts = [tr @ c for c in rig.slab[s]]
        lo = min(p.z for p in pts)
        zmin, zmax_low = min(zmin, lo), max(zmax_low, lo)
        for i, p in enumerate(pts):
            if p.z < TOUCH:
                if i not in first:
                    first[i] = p
                worst = max(worst, (p - first[i]).to_2d().length)
    print(f"IDLE {s} pin={'nopin' not in sys.argv} mode={clip_idle_hit.IDLE_PIN_MODE} slide_while_touching={worst*1000:.1f}mm slab_min_z range {zmin*1000:.1f}..{zmax_low*1000:.1f}mm", flush=True)
