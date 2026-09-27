"""Probe the walk slab stance (solver only, nothing is keyed or saved).
blender -b ForestRootSnarer_Anim_r01.blend -P probe_walk_pin.py
Per stance frame: pinned-point error, slab min z, slip of the vertices touching the ground
(entity motion removed), socket, reach error; plus socket continuity at touchdown / lift-off."""
import sys
from pathlib import Path
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core, clip_walk  # noqa: E402

rig = anim_core.Rig(bpy.data.objects[anim_core.ARM_NAME], bpy.data.objects[anim_core.MESH_NAME])
clip_walk.prepare(rig)
V, N = clip_walk.V, clip_walk.N
TOUCH = 0.003


def slab_world(M, s):
    tr = M[f"{s}_arm_lower"] @ rig.rest[f"{s}_arm_lower"].inverted()
    return [tr @ c for c in rig.slab[s]]


for s, a in clip_walk.ARM.items():
    d0 = a["down"]
    prev = None
    worst_slip, worst_pen, acc = 0.0, 0.0, {}
    for k in range(0, 7):
        f = d0 + k
        if k == 6:
            f = d0 + N * clip_walk.ARM_DUTY - 1e-3
        M, info = rig.solve(clip_walk.walk_pose(f % N))
        pts = slab_world(M, s)
        zmin = min(p.z for p in pts)
        worst_pen = min(worst_pen, zmin)
        touch = {i for i, p in enumerate(pts) if p.z < TOUCH}
        slip = 0.0
        if prev is not None:
            dt = f - prev[0]
            for i in touch & prev[1]:
                dv = pts[i] - prev[2][i] - Vector((0, V * dt, 0))
                slip = max(slip, Vector((dv.x, dv.y)).length / dt)
                acc.setdefault(i, Vector((0, 0, 0)))
                acc[i] = acc[i] + Vector((dv.x, dv.y, 0))
        worst_slip = max(worst_slip, slip)
        sock = M[f"{s}_hand"].translation
        print(f"{s} f={f:5.2f} pin_err={info.get('pin_err', {}).get(s, -1):.5f} slab_min_z={zmin*100:6.2f}cm "
              f"touch={len(touch):3d} slip={slip*100:5.2f}cm/f socket=({sock.x:.3f},{sock.y:.3f},{sock.z:.3f}) "
              f"reach_err={info['reach_err'][f'{s}_arm']:.4f}", flush=True)
        prev = (f, touch, pts)
    acc_max = max((v.length for v in acc.values()), default=0.0)
    print(f"SUMMARY {s} worst_slip={worst_slip*100:.2f}cm/f worst_pen={worst_pen*100:.2f}cm "
          f"accumulated_slide_max={acc_max*100:.2f}cm", flush=True)
    for f in (d0 - 1e-3, d0 + 1e-3, d0 + N * clip_walk.ARM_DUTY - 1e-3, d0 + N * clip_walk.ARM_DUTY + 1e-3):
        M, _ = rig.solve(clip_walk.walk_pose(f % N))
        print(f"  continuity {s} f={f:.3f} socket={tuple(round(x, 4) for x in M[f'{s}_hand'].translation)}")
