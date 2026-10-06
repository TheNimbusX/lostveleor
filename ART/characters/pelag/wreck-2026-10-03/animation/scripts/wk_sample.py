"""Крушение v2: интерполяция ключей (монотонная кубика по каждому числу, как ab_measure.py) и замер позы."""
import math
from mathutils import Vector
from s_lib import pchip, sagittal_lean
from wk_rig import M, SIDES, yaw_of
import wk_grip

FIELDS = ("W", "dz", "pyaw", "cyaw", "lean", "look", "gL", "gR")


def flat(p):
    v = [p.get(k, 0.0) for k in FIELDS]
    for s in SIDES: v += list(p["feet"][s])
    v += list(p["chain"])
    for s in SIDES: v += list(p["hand"][s]) + list(p["hax"][s]) + list(p["poles"][s]) + list(p["sol"][s])
    return v


def unflat(v):
    p = {k: v[i] for i, k in enumerate(FIELDS)}
    i = len(FIELDS)
    p["feet"] = {}
    for s in SIDES: p["feet"][s] = tuple(v[i:i + 5]); i += 5
    p["chain"] = tuple(v[i:i + 3]); i += 3
    p["hand"], p["hax"], p["poles"], p["sol"] = {}, {}, {}, {}
    for s in SIDES:
        p["hand"][s] = tuple(v[i:i + 3]); p["hax"][s] = tuple(v[i + 3:i + 6]); p["poles"][s] = tuple(v[i + 6:i + 9])
        p["sol"][s] = tuple(v[i + 9:i + 13]); i += 13
    return p


def sample(keys, frame):
    if frame in keys: return keys[frame]
    fr = sorted(keys)
    vs = {f: flat(keys[f]) for f in fr}
    n = len(vs[fr[0]])
    return unflat([pchip([(f, vs[f][c]) for f in fr], frame) for c in range(n)])


def measure(rig, body, vs=None):
    d = rig.dst
    pyaw, cyaw = rig.pelvis_yaw(), rig.chest_yaw()
    hm = rig.head_m()
    root = lambda v: [round(-v.y, 4), round(v.x, 4), round(v.z, 4)]
    fL, fR = wk_grip.fist_center(rig, "Left"), wk_grip.fist_center(rig, "Right")
    axis = (fR - fL).normalized() if (fR - fL).length > 1e-6 else wk_grip.fist_axis(rig, "Left")
    ring = fL + axis * wk_grip.RING
    row = dict(lean=round(sagittal_lean(d), 1), pelvis_yaw=round(pyaw, 1), chest_yaw=round(cyaw, 1),
               twist=round((cyaw - pyaw + 180) % 360 - 180, 1), relP=hm["relP"], head_relY=hm["relY"], face_yaw=hm["faceYaw"],
               hips_z=round(rig.P("Hips").z, 3), forearm_twist={s: round(rig.forearm_twist(s), 1) for s in SIDES},
               fistL=root(fL), fistR=root(fR), ring=root(ring), grip_axis=root(axis), hand_gap=round((fR - fL).length, 3),
               axis_err={s: round(min(math.degrees(wk_grip.fist_axis(rig, s).angle(axis)), 180 - math.degrees(wk_grip.fist_axis(rig, s).angle(axis))), 1)
                         for s in SIDES})
    for s in SIDES:
        row[s] = dict(ankle=root(rig.P(s + "Foot")), toe_z=round(rig.toe_z(s), 4))
    return row


def bone_deltas(sa, sb):
    worst = (0.0, "")
    for n, (la, qa) in sa.items():
        ang = math.degrees(qa.rotation_difference(sb[n][1]).angle); ang = min(ang, 360 - ang)
        if ang > worst[0]: worst = (ang, n.replace("mixamorig:", ""))
    return worst


def big_bone_deltas(sa, sb):
    """Наибольший поворот среди крупных костей (без пальцев) — для стыков (предел 5°)."""
    worst = (0.0, "")
    for n, (la, qa) in sa.items():
        if any(x in n for x in ("Thumb", "Index", "Middle", "Ring", "Pinky", "_End")): continue
        ang = math.degrees(qa.rotation_difference(sb[n][1]).angle); ang = min(ang, 360 - ang)
        if ang > worst[0]: worst = (ang, n.replace("mixamorig:", ""))
    return worst
