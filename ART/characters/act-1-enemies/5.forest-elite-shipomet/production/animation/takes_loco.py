"""Idle and Walk takes (procedural, seamless loops)."""
import math

from anim_pose import foot_roll
from takes_common import IDLE_FRAMES, add, idle, norm, smooth

WALK_SPEED = 2.2          # m/s, sim speed
WALK_FRAMES = 24          # 0.8 s cycle -> 1.76 m per cycle, 0.88 m per step
WALK_DUTY = 0.56          # stance fraction per foot
ROLL_START = 0.62         # stance fraction where the heel starts to lift (toe roll)
ROLL_MAX = 38.0           # deg of heel lift at toe-off
LIFT = 0.30               # m, claw pivot height at mid swing (stilt step)


def idle_take(D):
    return {"frames": IDLE_FRAMES, "loop": True, "params": lambda f: idle(D, f)}


def _hermite(p0, p1, m0, m1, s):
    h00, h10, h01, h11 = 2 * s ** 3 - 3 * s ** 2 + 1, s ** 3 - 2 * s ** 2 + s, -2 * s ** 3 + 3 * s ** 2, s ** 3 - s ** 2
    return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1


def foot_state(sk, side, u):
    """Claw pivot (front contact) path, heel pitch and planted flag at cycle phase u in [0,1)."""
    T = WALK_FRAMES / 30.0
    D = WALK_SPEED * WALK_DUTY * T
    px, py, _ = sk.contact_front[side]
    if u < WALK_DUTY:
        s = u / WALK_DUTY
        y = -D / 2 + WALK_SPEED * T * u
        pitch = ROLL_MAX * smooth((s - ROLL_START) / (1 - ROLL_START))
        return (px, py + y, 0.0), pitch, True
    s = (u - WALK_DUTY) / (1 - WALK_DUTY)
    Ts = (1 - WALK_DUTY) * T
    # leave and land moving with the ground (no velocity pop at lift-off / touchdown)
    y = _hermite(D / 2, -D / 2, 0.9 * WALK_SPEED * Ts, 0.9 * WALK_SPEED * Ts, s)
    z = LIFT * 16 * (s * (1 - s)) ** 2 * (1 + 0.6 * (0.5 - s))
    pitch = ROLL_MAX * (1 - smooth(s / 0.8)) + 14 * math.sin(math.pi * s) ** 2
    return (px, py + y, z), pitch, False


def walk_params(sk, D, f):
    t = (f % WALK_FRAMES) / WALK_FRAMES
    w = math.tau * t
    p = idle(D, 0)
    p["hip"] = (0.028 * math.sin(math.tau * (t - 0.06)), 0.0, -0.085 - 0.035 * math.cos(2 * math.tau * (t - 0.03)))
    p["hip_rot"] = (5.0, 2.5 * math.sin(math.tau * (t - 0.06)), -7.0 * math.cos(w))
    p["spine"] = (6.0 + 1.5 * math.cos(2 * math.tau * (t - 0.1)), -2.0 * math.sin(math.tau * (t - 0.06)), 6.0 * math.cos(w))
    p["neck"] = (-1.0, 0.0, -2.0 * math.cos(w))
    p["head"] = (-5.0 + 1.5 * math.cos(2 * math.tau * (t - 0.12)), 0.0, 1.0 * math.cos(w))
    for side, s, off, sg in (("L", "Left", 0.0, 1), ("R", "Right", 0.5, -1)):
        u = (t + off) % 1.0
        pivot, pitch, planted = foot_state(sk, s, u)
        ank, foot = foot_roll(sk, s, pivot, pitch)
        p[side + "_ank"], p[side + "_foot"] = ank, foot
        p[side + "_knee"] = norm((0.25 * sg, -1.0, 0.0))
        # arm swings opposite to its leg: this leg forward (u=0) -> this arm back (+Y)
        a = math.cos(math.tau * u)
        lag = math.cos(math.tau * u - 0.6)
        p[side + "_tip"] = add(D[side + "_tip"], (-0.05 * sg, -0.06 + 0.24 * a, 0.05 + 0.05 * (1 - a) * 0.5))
        p[side + "_dir"] = norm(add(D[side + "_dir"], (0.0, -0.1 + 0.32 * lag, 0.0)))
        p[side + "_shrug"] = 2.0 - 2.0 * a
        p[side + "_toe"] = 0.0 if planted else 10.0 * math.sin(math.pi * (u - WALK_DUTY) / (1 - WALK_DUTY))
    return p


def walk_take(sk, D):
    return {"frames": WALK_FRAMES, "loop": True, "params": lambda f: walk_params(sk, D, f),
            "speed_mps": WALK_SPEED, "cycle_m": WALK_SPEED * WALK_FRAMES / 30.0}
