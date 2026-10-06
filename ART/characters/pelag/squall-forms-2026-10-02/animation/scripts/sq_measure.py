"""Шквал v2: интерполяция ключей и замер пределов по кадру (скрутка, наклон, предплечье, голова, стопы)."""
import math
from mathutils import Vector
from b_common import blade
from s_lib import pchip, sagittal_lean, Body
from sq_rig import M, SIDES, yaw_of

FIELDS = ("W", "dz", "pyaw", "cyaw", "lean", "wB", "look", "sv", "tv", "wd", "wf")


def flat(p):
    v = [p[k] for k in FIELDS]
    for s in ("Left", "Right"): v += list(p["feet"][s])
    for s in ("Right", "Left"): v += list(p["hands"][s]) + list(p["poles"][s])
    return v + list(p["blade"])


def unflat(v):
    p = {k: v[i] for i, k in enumerate(FIELDS)}
    i = len(FIELDS)
    p["feet"] = {}
    for s in ("Left", "Right"): p["feet"][s] = tuple(v[i:i + 5]); i += 5
    p["hands"] = {}; p["poles"] = {}
    for s in ("Right", "Left"):
        p["hands"][s] = tuple(v[i:i + 3]); p["poles"][s] = tuple(v[i + 3:i + 6]); i += 6
    p["blade"] = tuple(v[i:i + 3])
    p["fixed"] = True
    return p


def sample(keys, frame):
    fr = sorted(keys)
    if frame in keys: return keys[frame]
    vs = {f: flat(keys[f]) for f in fr}
    n = len(vs[fr[0]])
    return unflat([pchip([(f, vs[f][c]) for f in fr], frame) for c in range(n)])


def measure(rig, body):
    """Замер позы: всё, что проверяется пределами задачи."""
    d = rig.dst
    pyaw, cyaw = rig.pelvis_yaw(), rig.chest_yaw()
    tw = (cyaw - pyaw + 180) % 360 - 180
    hm = rig.head_m()
    r, t = blade(d)
    vs = body.verts()
    row = dict(lean=round(sagittal_lean(d), 1), pelvis_yaw=round(pyaw, 1), chest_yaw=round(cyaw, 1), twist=round(tw, 1),
               relP=hm["relP"], relP_over_stance=round(hm["relP"] - rig.B["head"]["relP"], 2), head_relY=hm["relY"],
               face_yaw=hm["faceYaw"], hips_z=round(rig.P("Hips").z, 3),
               forearm_twist={s: round(rig.forearm_twist(s), 1) for s in SIDES},
               blade_dir=[round(c, 2) for c in (t - r).normalized()], blade_tip=[round(c, 3) for c in t],
               hand_R=[round(c, 3) for c in rig.P("RightHand")],
               arm_in_thigh={s: list(body.contact([s + "Hand", s + "ForeArm"], s + "Thigh", vs)[:3]) for s in SIDES},
               blade_yaw=round(yaw_of(t - r), 1))
    for s in SIDES:
        a = rig.P(s + "Foot")
        row[s] = dict(ankle=[round(c, 3) for c in a], toe_z=round(rig.toe_z(s), 4))
    return row


def bone_deltas(sa, sb):
    """Наибольший поворот кости (локально, °) между двумя снимками позы."""
    worst = (0.0, "")
    for n, (la, qa) in sa.items():
        qb = sb[n][1]
        ang = math.degrees(qa.rotation_difference(qb).angle)
        ang = min(ang, 360 - ang)
        if ang > worst[0]: worst = (ang, n.replace("mixamorig:", ""))
    return worst


def big_moves(sa, sb, lim=35.0):
    out = []
    for n, (la, qa) in sa.items():
        ang = math.degrees(qa.rotation_difference(sb[n][1]).angle); ang = min(ang, 360 - ang)
        if ang > lim: out.append((n.replace("mixamorig:", ""), round(ang)))
    return out
