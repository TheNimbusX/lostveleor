"""Крушение v3, вращение корпуса на 360° (Swing2 и др.): поправки wk_pose под полный оборот — рысканье по кратчайшему углу,
наклон в плоскости груди (замер chest_lean вместо наклона по оси корня). Импорт подменяет wk_pose.body/head и wk_sample.sagittal_lean;
RIG_[0] = rig — после создания рига."""
import math
from mathutils import Vector, Quaternion
import wk_pose
import wk_sample
from s_lib import rotate_world, translate_world, foot_pitch, two_bone
from wk_rig import M as _M, yaw_of as _yaw_of

_UP = Vector((0, 0, 1))


def _wrap(e):
    return (e + 180.0) % 360.0 - 180.0


def _chest_left(rig):
    a = math.radians(rig.chest_yaw())
    return Vector((math.cos(a), math.sin(a), 0.0)), Vector((math.sin(a), -math.cos(a), 0.0))


def chest_lean(d, rig=None):
    """Наклон вперёд по взгляду груди (°): шея от таза в плоскости «вверх — взгляд груди»."""
    r = RIG_[0]
    v = r.P("Neck") - r.P("Hips"); _, fw = _chest_left(r)
    return math.degrees(math.atan2(v.dot(fw), v.z))


RIG_ = [None]


def body_spin(rig, p):
    """wk_pose.body: рысканье по кратчайшему углу, наклон вокруг левого бока груди (замер chest_lean)."""
    d = rig.dst; B = rig.B; W = p["W"]
    rig.reset()
    hips = d.pose.bones[_M("Hips")]
    translate_world(d, hips, Vector((0, 0, max(wk_pose.DZ_MIN, p["dz"] * W))))
    for _ in range(3):
        e = _wrap(wk_pose.lerp(B["pyaw"], p["pyaw"], W) - rig.pelvis_yaw())
        if abs(e) < .05: break
        rotate_world(d, hips, Quaternion(_UP, math.radians(e)), rig.P("Hips"))
    L0, _ = _chest_left(rig)
    rotate_world(d, hips, Quaternion(L0, math.radians(.3 * (p["lean"] - B["lean"]) * W)), rig.P("Hips"))
    for _ in range(4):
        e = _wrap(wk_pose.lerp(B["cyaw"], p["cyaw"], W) - rig.chest_yaw())
        if abs(e) < .2: break
        for n, w in wk_pose.SPINE_W:
            rotate_world(d, d.pose.bones[_M(n)], Quaternion(_UP, math.radians(e * w)), rig.P(n))
    goal = wk_pose.lerp(B["lean"], p["lean"], W)
    for _ in range(8):
        e = goal - chest_lean(d)
        if abs(e) < .15: break
        Lx, _ = _chest_left(rig)
        for n, w in wk_pose.SPINE_W:
            rotate_world(d, d.pose.bones[_M(n)], Quaternion(Lx, math.radians(e * w)), rig.P(n))
    for s in ("Left", "Right"):
        f, l, u, fy, fp = p["feet"][s]
        target = B["ankle"][s].lerp(wk_pose.fl(f, l, u), W)
        want_yaw = wk_pose.lerp(B["foot_yaw"][s], fy, W); want_p = B["pitch"][s] + fp * W
        foot = d.pose.bones[_M(s + "Foot")]
        for _ in range(4):
            two_bone(d, [_M(s + "UpLeg"), _M(s + "Leg"), _M(s + "Foot")], target, pole_hint=p.get("knee", {}).get(s), keep_end=True)
            cur_p, horiz = foot_pitch(d, s)
            rotate_world(d, foot, Quaternion(_UP, math.radians(_wrap(want_yaw - _yaw_of(horiz)))), rig.P(s + "Foot"))
            cur_p, horiz = foot_pitch(d, s)
            ax = _UP.cross(horiz)
            if ax.length > 1e-6:
                rotate_world(d, foot, Quaternion(ax.normalized(), math.radians(want_p - cur_p)), rig.P(s + "Foot"))
            low = rig.toe_z(s)
            if low >= rig.toe_floor - 1e-4: break
            target = target + Vector((0, 0, rig.toe_floor - low))


def head_spin(rig, p):
    d = rig.dst
    look = p.get("look", rig.B["head"]["faceYaw"])
    goal = rig.head_m()["faceYaw"] + _wrap(look - rig.head_m()["faceYaw"]) * p["W"]; fix = 0.0
    for _ in range(12):
        e = _wrap(goal - rig.head_m()["faceYaw"])
        e = max(-wk_pose.HEAD_YAW_MAX - fix, min(wk_pose.HEAD_YAW_MAX - fix, e))
        if abs(e) < .05: break
        cu = rig.hframe("Spine2")[1]
        for n in ("Neck", "Head"):
            rotate_world(d, d.pose.bones[_M(n)], Quaternion(cu, math.radians(e * .5)), rig.P(n))
        fix += e
    return fix


wk_pose.body = body_spin; wk_pose.head = head_spin
wk_sample.sagittal_lean = chest_lean
