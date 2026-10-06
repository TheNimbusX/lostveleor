"""Крушение v3, Swing2: где пятно касания стопы на носке (для оси пируэта). blender -b -P v3s4_probe_pivot.py -- <pitch,...> <ext,...>
Ставит стойку, правую стопу на кончики пальцев с подушечкой ext (м за ToeBase) и пяткой pitch°, печатает вершины стопы ниже 12 мм:
число, центр относительно точки опоры (f, l) и медиану расстояния до центра (≈ сдвиг пятна за тик при повороте на 33°: × 0,58)."""
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector
from s_lib import Body
from wk_rig import Rig
import wk_grip, wk_pose, v3s4_keys as K

argv = sys.argv[sys.argv.index("--") + 1:]
pitches = [float(x) for x in argv[0].split(",")]; exts = [float(x) for x in argv[1].split(",")]; dzs = [-0.08]; sinks = [float(x) for x in argv[2].split(",")] if len(argv) > 2 else [0.0]
rig = Rig(); wk_grip.prepare(rig); body = Body(rig.mesh)
ids = [i for i, p in enumerate(body.vpart) if p == "RightFoot"]
for pitch, ext, dz, sink in [(a, b, c, d) for a in pitches for b in exts for c in dzs for d in sinks]:
    if True:
        yaw = -60.0
        R = K.ankle_on_ball("Right", K.HIPS_FL, yaw, pitch, ext, sink)
        p = dict(W=1.0, dz=dz, pyaw=yaw + 30, cyaw=yaw + 50, lean=15.0, look=yaw + 30,
                 feet=dict(Left=(0.3, 0.2, 0.30, 0.0, 10.0), Right=R))
        wk_pose.body(rig, p); bpy.context.view_layer.update()
        vs = body.verts()
        low = [vs[i] for i in ids if vs[i].z < 0.012]
        root = lambda v: (-v.y * 0.98881, v.x * 0.98881)
        if not low: print("pitch %.0f ext %.3f: no contact (toe_z %.4f)" % (pitch, ext, rig.toe_z("Right"))); continue
        c = [sum(root(v)[k] for v in low) / len(low) for k in range(2)]
        d = sorted(math.dist(root(v), c) for v in low)
        print("sink %.3f pitch %.0f ext %.3f: n %d centre-ball (%.3f %.3f) median r %.3f max r %.3f toe_z %.4f" % (
            sink, pitch, ext, len(low), c[0] - K.HIPS_FL[0], c[1] - K.HIPS_FL[1], d[len(d) // 2], d[-1], rig.toe_z("Right")))
