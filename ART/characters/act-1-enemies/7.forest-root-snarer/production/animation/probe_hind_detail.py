"""Detail of a hind sole's motion during one walk stance (solver only, nothing keyed or saved).
blender -b ForestRootSnarer_Anim_r01.blend -P probe_hind_detail.py -- [L|R] [step 0|1]"""
import sys
from pathlib import Path
import bpy

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import anim_core, clip_walk  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
s = argv[0] if argv else "L"
k = int(argv[1]) if len(argv) > 1 else 1
rig = anim_core.Rig(bpy.data.objects[anim_core.ARM_NAME], bpy.data.objects[anim_core.MESH_NAME])
clip_walk.prepare(rig)
V = clip_walk.V
t = clip_walk.TAB["hind"][(s, k)]
f0 = clip_walk.HIND[s]["down"] + k * clip_walk.HIND_PERIOD
n = len(t["ankles"]) - 1
dt = clip_walk.HIND_PERIOD * clip_walk.HIND_DUTY / n
print("table steps", n, "corr_lo", [round(x, 4) for x in t["corr_lo"]])
prev = None
for j in range(n + 1):
    f = f0 + j * dt
    M, info = rig.solve(clip_walk.walk_pose(f % 16))
    pts = rig.skinned_sole(M, s)
    lo = min(range(len(pts)), key=lambda i: pts[i].z)
    touch = [i for i in range(len(pts)) if pts[i].z < 0.003]
    slip = 0.0
    if prev is not None:
        for i in touch:
            if prev[i].z < 0.003:
                slip = max(slip, ((pts[i].x - prev[i].x) ** 2 + (pts[i].y - prev[i].y - V * dt) ** 2) ** 0.5)
    a = t["ankles"][j]
    print(f"j={j:2d} f={f:6.2f} table_ankle=({a.x:.3f},{a.y:.3f},{a.z:.3f}) used=({M[f'{s}_foot'].translation.x:.3f},"
          f"{M[f'{s}_foot'].translation.y:.3f},{M[f'{s}_foot'].translation.z:.3f}) low=v{lo} z={pts[lo].z*1000:.1f}mm "
          f"touch={len(touch)} slip_step={slip*1000:.1f}mm", flush=True)
    prev = pts
