"""animfix: поза из Unity (замер measure_throw.cs: мировые повороты костей модели и позиции) → риг Blender броска.
Поворот кости = дельта от покоя в мире (Unity → Blender: x → −X, z → −Y, y → Z; масштаб 1,838 — сверено по Throw0)."""
import json, math
import bpy
from mathutils import Vector, Quaternion, Matrix
from wk_rig import M, SIDES
from s_lib import translate_world

C = Matrix(((-1, 0, 0), (0, 0, -1), (0, 1, 0)))
SCALE = 1.838


def _q(a):
    return Quaternion((a[3], a[0], a[1], a[2]))


def load(path):
    return json.load(open(path, encoding="utf-8"))


_CAL = None


def _calib():
    global _CAL
    if _CAL is None:
        import os
        _CAL = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "calib_B.json")))
    return _CAL


def apply(rig, data, wq, p, ground_z=None):
    """wq, p — списки по data['bones'] (позы ref или кадра). ground_z — высота подушечек стоп в Blender (None — не садить).
    Поворот кости: R_blender = C·U·C⁻¹ · K, K — поправка осей кости из калибровки (Throw0 Unity ↔ кадр 0 прежнего мастера)."""
    B = data["bones"]; T0 = data["ref"]["Throw0"]["wq"]
    cal = _calib()
    d = rig.dst; mwi = d.matrix_world.inverted()
    idx = {n: i for i, n in enumerate(B)}
    order = []
    def walk(b):
        order.append(b.name)
        for c in b.children: walk(c)
    walk(d.data.bones[M("Hips")])
    hp = p[idx["Hips"]]
    hips = Vector((-hp[0], -hp[2], hp[1])) * SCALE
    for n in order:
        short = n.replace("mixamorig:", "")
        if short not in idx or short not in cal: continue
        i = idx[short]
        U0 = C @ _q(T0[i]).to_matrix() @ C.inverted()
        Bq = Quaternion(cal[short]["q"]).to_matrix()
        K = U0.inverted() @ Bq
        rot = (C @ _q(wq[i]).to_matrix() @ C.inverted()) @ K
        pb = d.pose.bones[n]
        head = hips if n == M("Hips") else (d.matrix_world @ pb.matrix).translation
        pb.matrix = mwi @ (Matrix.Translation(head) @ rot.to_4x4())
        bpy.context.view_layer.update()
    if ground_z is not None:
        low = min(rig.P(s + "ToeBase").z for s in SIDES)
        translate_world(d, d.pose.bones[M("Hips")], Vector((0, 0, ground_z - low)))
        bpy.context.view_layer.update()
    return dict(rig.snapshot())
