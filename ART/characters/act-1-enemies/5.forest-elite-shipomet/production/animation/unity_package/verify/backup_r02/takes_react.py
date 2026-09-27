"""Hit and Death takes."""
import math

from anim_keys import Track
from anim_pose import DIRS
from takes_common import K, add, idle, norm


def hit_take(D):
    I = idle(D, 0)
    flinch = dict(hip=(0.0, 0.07, -0.07), hip_rot=(-5.0, 2.0, 4.0), spine=(-11.0, 3.0, 6.0), neck=(-4.0, 0, 0), head=(-16.0, 4.0, 8.0),
                  L_tip=add(I["L_tip"], (0.22, 0.10, 0.30)), L_dir=norm((0.45, -0.2, -0.8)), L_shrug=10.0,
                  R_tip=add(I["R_tip"], (-0.20, 0.12, 0.26)), R_dir=norm((-0.45, -0.2, -0.8)), R_shrug=10.0)
    keys = [K(I, 0), K(I, 3, "out", **flinch),
            K(I, 6, "auto", **dict(flinch, spine=(-7.0, 2.0, 4.0), head=(-9.0, 2.0, 5.0), hip=(0.0, 0.05, -0.06))),
            K(I, 12, "auto")]
    return {"frames": 12, "loop": False, "params": Track(keys, D, DIRS), "events": {"impact": 0, "peak": 3}}


def _feet(D, lx, ly, lz, lp, rx, ry, rz, rp, knee):
    return dict(L_ank=(lx, ly, lz), L_foot=(lp, 0.0, 0.0), R_ank=(rx, ry, rz), R_foot=(rp, 0.0, 0.0),
                L_knee=knee[0], R_knee=knee[1])


def death_take(D):
    I = idle(D, 0)
    la, ra = D["L_ank"], D["R_ank"]
    fwd = (norm((0.35, -1, 0)), norm((-0.35, -1, 0)))
    kneel_knee = (norm((0.2, -1, -0.35)), norm((-0.2, -1, -0.35)))
    down = (norm((0.3, 0.0, -1.0)), norm((-0.3, 0.0, -1.0)))
    stagger = dict(hip=(0.02, 0.10, -0.06), hip_rot=(-7.0, 5.0, 6.0), spine=(-16.0, 7.0, 10.0), neck=(-4.0, 0, 0), head=(-24.0, 8.0, 12.0),
                   L_tip=(1.25, -0.30, 1.95), L_dir=norm((0.7, -0.3, 0.4)), L_shrug=12.0,
                   R_tip=(-1.05, 0.35, 1.55), R_dir=norm((-0.6, 0.5, -0.3)), R_shrug=8.0)
    lurch = dict(hip=(-0.02, -0.02, -0.22), hip_rot=(12.0, -3.0, -4.0), spine=(20.0, -5.0, -8.0), neck=(6.0, 0, 0), head=(12.0, -4.0, -6.0),
                 L_tip=(0.95, -0.50, 1.25), L_dir=norm((0.2, -0.5, -1.0)), R_tip=(-0.90, -0.30, 1.15), R_dir=norm((-0.2, -0.3, -1.0)),
                 L_knee=fwd[0], R_knee=fwd[1])
    buckle = dict(lurch, hip=(0.0, 0.02, -0.62), hip_rot=(8.0, 3.0, 0.0), spine=(14.0, 4.0, 4.0), head=(4.0, 4.0, 4.0),
                  L_wtip=(0.78, -0.62, 0.0), L_wdir=norm((0.15, -0.3, -1.0)), R_wtip=(-0.75, -0.50, 0.0), R_wdir=norm((-0.15, -0.2, -1.0)),
                  **_feet(D, la[0], la[1] + 0.05, la[2] + 0.07, 12.0, ra[0], ra[1] + 0.02, ra[2] + 0.06, 8.0, fwd))
    kneel = dict(hip=(0.0, 0.10, -0.98), hip_rot=(4.0, 3.0, 0.0), spine=(18.0, 3.0, 3.0), neck=(10.0, 0, 0), head=(18.0, 6.0, 8.0),
                 L_lock=1.0, L_wtip=(0.78, -0.62, 0.0), L_wdir=norm((0.15, -0.3, -1.0)), R_lock=1.0, R_wtip=(-0.75, -0.50, 0.0), R_wdir=norm((-0.15, -0.2, -1.0)),
                 L_toe=-15.0, R_toe=-15.0, **_feet(D, la[0] + 0.02, 0.36, 0.26, 106.0, ra[0] - 0.02, 0.34, 0.26, 106.0, kneel_knee))
    slump = dict(kneel, spine=(30.0, 5.0, 5.0), head=(28.0, 8.0, 10.0), hip=(0.0, 0.08, -1.02))
    fall = dict(hip=(0.02, -0.18, -1.12), hip_rot=(60.0, 3.0, 2.0), spine=(22.0, 4.0, 4.0), neck=(0.0, 0, 0), head=(-10.0, 8.0, 18.0),
                L_lock=1.0, L_wtip=(0.88, -1.25, 0.05), L_wdir=norm((0.35, -0.8, -0.3)), R_lock=1.0, R_wtip=(-0.92, -0.95, 0.05), R_wdir=norm((-0.45, -0.7, -0.3)),
                L_toe=-15.0, R_toe=-15.0, **_feet(D, la[0] + 0.06, 0.84, 0.24, 106.0, ra[0] - 0.06, 0.82, 0.24, 106.0, down))
    lie = dict(hip=(0.03, -0.34, -1.36), hip_rot=(86.0, 4.0, 3.0), spine=(3.0, 4.0, 4.0), neck=(-6.0, 0, 0), head=(-14.0, 10.0, 28.0),
               L_lock=1.0, L_wtip=(1.00, -1.55, 0.12), L_wdir=norm((0.45, -0.85, -0.12)), L_pole=norm((1, 0, 2.5)),
               R_lock=1.0, R_wtip=(-1.05, -0.95, 0.05), R_wdir=norm((-0.75, -0.6, -0.1)), R_pole=norm((-1, 0, 1)),
               L_toe=-15.0, R_toe=-15.0, **_feet(D, la[0] + 0.12, 0.90, 0.22, 104.0, ra[0] - 0.12, 0.88, 0.22, 104.0, down))
    settle = dict(lie, hip=(0.03, -0.34, -1.38), head=(-16.0, 10.0, 30.0))
    keys = [K(I, 0), K(I, 5, "out", **stagger), K(I, 11, "auto", **lurch), K(I, 17, "in", **buckle),
            K(I, 22, "in", **kneel), K(I, 24, "out", **dict(kneel, hip=(0.0, 0.10, -1.01))), K(I, 29, "auto", **slump),
            K(I, 35, "in", **fall), K(I, 39, "in", **lie), K(I, 41, "out", **dict(lie, hip=(0.03, -0.34, -1.33))),
            K(I, 44, "auto", **settle), K(I, 48, "auto", **settle)]
    # feet: tip the claws back before the shins land so they never dig into the ground
    base = Track(keys, D, DIRS)
    for fr, pitch, lift in ((18, 34.0, 0.12), (19, 60.0, 0.12), (20, 82.0, 0.10), (21, 98.0, 0.05)):
        p = dict(base(fr))
        for sd in "LR":
            a = p[sd + "_ank"]
            p[sd + "_ank"] = (a[0], a[1], a[2] + lift)
            p[sd + "_foot"] = (pitch, 0.0, 0.0)
            p[sd + "_toe"] = -15.0 * pitch / 106.0
        keys.append((fr, p, "auto"))
    # knees slide back while the torso tips: lift the pelvis a little so the knees stay on top of the ground
    for fr in (31, 33):
        p = dict(base(fr))
        h = p["hip"]
        p["hip"] = (h[0], h[1], h[2] + 0.115 * math.sin(math.pi * (fr - 29) / 6))
        keys.append((fr, p, "auto"))
    track = Track(keys, D, DIRS)

    def params(f):
        # r02: knees/thighs dug up to 4.7 cm into the ground at 33-38 -> lift the pelvis a little while the torso tips,
        # then keep +3 cm for the lying pose (thighs rest on z~0, constant over the still frames 44-48)
        lift = _death_lift(f)
        p = track(f)
        if lift:
            p = dict(p)
            h = p["hip"]
            p["hip"] = (h[0], h[1], h[2] + lift)
        return p

    return {"frames": 48, "loop": False, "params": params,
            "events": {"stagger": [0, 11], "knees": 22, "falls": [29, 39], "ground": 39, "still": [44, 48]}}


def _death_lift(f):
    def sm(t):
        t = max(0.0, min(1.0, t))
        return t * t * (3 - 2 * t)
    if f <= 34.0:
        return 0.055 * sm((f - 31.5) / 2.5)
    return 0.055 - 0.025 * sm((f - 34.0) / 3.0)
