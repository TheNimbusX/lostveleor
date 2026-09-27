"""Probe the walk hind-foot plants under skinning (solver only, nothing keyed or saved).
blender -b ForestRootSnarer_Anim_r01.blend -P probe_walk_hind.py -- [nopin]"""
import sys
from pathlib import Path
import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core, clip_walk  # noqa: E402

rig = anim_core.Rig(bpy.data.objects[anim_core.ARM_NAME], bpy.data.objects[anim_core.MESH_NAME])
clip_walk.prepare(rig)
if "nopin" in sys.argv:
    clip_walk.TAB.pop("hind")
V, P8, DUTY = clip_walk.V, clip_walk.HIND_PERIOD, clip_walk.HIND_DUTY
print("sole verts", {s: len(v) for s, v in rig.sole_skin.items()})
for s, h in clip_walk.HIND.items():
    for k in (0, 1):
        f0 = h["down"] + k * P8
        frames = [f0 + i * 0.5 for i in range(int(P8 * DUTY / 0.5) + 1)]
        first, drift, zmin, zmax = None, 0.0, 1.0, -1.0
        for f in frames:
            M, info = rig.solve(clip_walk.walk_pose(f % 16))
            pts = rig.skinned_sole(M, s)
            comp = [(p.x, p.y - V * (f - f0)) for p in pts]
            if first is None:
                first = comp
            drift = max(drift, max(((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2) ** 0.5 for a, b in zip(comp, first)))
            zmin, zmax = min(zmin, min(p.z for p in pts)), max(zmax, min(p.z for p in pts))
        f_lo = f0 + P8 * DUTY
        a = rig.solve(clip_walk.walk_pose((f_lo - 1e-3) % 16))[0][f"{s}_foot"].translation
        b = rig.solve(clip_walk.walk_pose((f_lo + 1e-3) % 16))[0][f"{s}_foot"].translation
        print(f"HIND {s} step{k} f={f0:.1f}..{f_lo:.1f} sole_drift={drift*100:.2f}cm sole_min_z={zmin*1000:.1f}..{zmax*1000:.1f}mm "
              f"liftoff_ankle_jump={(a-b).length*1000:.2f}mm reach={info['reach_err'][f'{s}_leg']:.4f}", flush=True)
