"""ForestRootSnarer_Walk: in-place 16-frame loop for 2.4 m/s (1.28 m per cycle).

Knuckle-walk: each slab plants once per cycle (L at frame 0, R at frame 8) for 5.5 frames
(0.44 m of ground: the long rigid slab cannot reach further without folding the shoulder
junction); the body shifts over the planted slab, the free shoulder rises and the free slab
swings forward as a pendulum around the shoulder (it has no elbow). The short hind legs
shuffle two quick steps per cycle. Everything is periodic in 16 frames (frame 16 == frame 0).

Planted contacts are rolling contacts measured on the mesh as the game skins it (check r01):
the slab / sole vertex touching the ground moves back with the ground at exactly 0.08 m/frame
and the lowest one stays on z = 0 (_roll_stance, _prepare_hind). Pinning the knuckle socket
instead let the slab bottom skate up to 4 cm per stance, and the rigid-foot approximation let
the L hind sole (part-weighted to the shin) skate 3-5 cm.
"""
import math
import stance
from stance import add3, rot_add

N = 16
V = 2.4 / 30.0                     # ground speed, m per frame (ground moves +Y under the mob)
ARM = {"L": {"down": 0.0, "x": 0.62, "yc": -0.50}, "R": {"down": 8.0, "x": -0.62, "yc": -0.53}}
ARM_DUTY = 5.5 / 16
SWING_ABD, SWING_SHRINK = 30.0, 0.0   # pendulum swing: tip outward (deg) and radius pull-in (m)
ROLL_STEPS = 4                      # solver steps per frame for the rolling slab contact
HIND = {"L": {"down": 5.5, "x": 0.53, "yc": 0.55}, "R": {"down": 1.5, "x": -0.52, "yc": 0.51}}
HIND_PERIOD, HIND_DUTY, HIND_LIFT = N / 2, 0.55, 0.075
TAB = {}


def limb(f, down, period, duty, yc, lift):
    """-> (y, height, swing progress or None). Stance slides +Y at V exactly."""
    u = ((f - down) / period) % 1.0
    span = V * period * duty
    y0 = yc - span / 2
    if u < duty:
        return y0 + V * period * u, 0.0, None
    w = (u - duty) / (1 - duty)
    s = w * w * (3 - 2 * w)
    return y0 + span * (1 - s), lift * math.sin(math.pi * w) ** 1.3, w


def arm_phase(f, s):
    a = ARM[s]
    u = ((f - a["down"]) / N) % 1.0
    return (None if u < ARM_DUTY else (u - ARM_DUTY) / (1 - ARM_DUTY)), u


def body_pose(f):
    P = stance.base()
    ph = 2 * math.pi * f / N
    over_l = math.sin(ph + math.pi / 2 - 2 * math.pi * 2.75 / N)   # +1 at mid L-stance
    bob = -math.cos(2 * ph - 2 * math.pi * 2 * 1.0 / N)            # lowest 1 frame after each slab plant
    P["pelvis_off"] = add3(P["pelvis_off"], (0.028 * over_l, 0.0, -0.04 + 0.014 * bob))
    P["pelvis_rot"] = add3(P["pelvis_rot"], (-8.0 + 1.5 * bob, 3.0 * over_l, -2.5 * over_l))
    rot_add(P, "spine_01", (1.0, 1.0 * over_l, 2.0 * over_l))
    rot_add(P, "spine_02", (-1.5 * bob, 2.5 * over_l, 5.5 * over_l))
    rot_add(P, "neck", (4.0, -1.5 * over_l, -3.0 * over_l))
    rot_add(P, "head", (4.0 + 1.5 * bob, -3.0 * over_l, -3.5 * over_l))
    rot_add(P, "jaw", (3.0, 0, 0))
    for s in ("L", "R"):
        w, u = arm_phase(f, s)
        sg = 1 if s == "L" else -1
        reach = math.cos(2 * math.pi * u)                              # +1 at touchdown (arm forward)
        lift = math.sin(math.pi * w) if w is not None else 0.0
        rot_add(P, f"{s}_clavicle", (0, -sg * 7.0 * lift, -sg * 7.0 * reach))
    for s, hnd in HIND.items():
        y, h, w = limb(f, hnd["down"], HIND_PERIOD, HIND_DUTY, hnd["yc"], HIND_LIFT)
        P["foot"][s] = (hnd["x"], y, h)
        pitch = 0.0 if w is None else 18.0 * math.sin(math.pi * min(1.0, w * 1.4)) - 6.0 * math.sin(math.pi * w)
        P["foot_rot"][s] = (pitch, 0.0, 0.0)
        hind = TAB.get("hind")
        if hind:
            k = int(math.floor((f - hnd["down"]) / HIND_PERIOD)) % 2      # which of the two steps
            t = hind[(s, k)]
            u = ((f - hnd["down"]) / HIND_PERIOD) % 1.0
            if w is None:   # planted: ankle from the rolling table (skinned sole moves at exactly V)
                x = u / HIND_DUTY * (len(t["ankles"]) - 1)
                i = min(int(x), len(t["ankles"]) - 2)
                P.setdefault("ankle", {})[s] = tuple(t["ankles"][i].lerp(t["ankles"][i + 1], x - i))
            else:           # lift-off correction fades out, next touchdown's fades in (no pop)
                sm = w * w * (3 - 2 * w)
                P["foot"][s] = add3(add3(P["foot"][s], t["corr_lo"], 1 - sm), hind[(s, (k + 1) % 2)]["corr_td"], sm)
    return P


def _prepare_hind(rig):
    """Hind stances as rolling contacts under skinning: the foot mesh is weighted up to ~40% to the
    shin, so a rigid-foot plant let the L sole skate 3-5 cm while the shin swung back. Stepping
    through each stance, the skinned sole vertex that ends a step lowest has moved with the ground
    (+V) and the lowest one sits on z = 0. -> ankle targets per step + lift-off correction."""
    from mathutils import Vector
    TAB.pop("hind", None)
    tab = {}
    for s, hnd in HIND.items():
        for k in (0, 1):
            f_td = hnd["down"] + k * HIND_PERIOD
            n = int(math.ceil(HIND_PERIOD * HIND_DUTY * ROLL_STEPS))
            dt = HIND_PERIOD * HIND_DUTY / n
            M, info = rig.solve(body_pose(f_td))
            free_td = Vector(info["ankle_target"][s])
            pts = rig.skinned_sole(M, s)
            c = min(range(len(pts)), key=lambda i: pts[i].z)
            P = body_pose(f_td)                       # touchdown snapped so the skinned sole is on z = 0
            P["foot_pin"] = {s: (c, (pts[c].x, pts[c].y))}
            M, info = rig.solve(P)
            ankles = [Vector(info["ankle_target"][s])]
            prev = rig.skinned_sole(M, s)
            for j in range(1, n + 1):
                for _ in range(4):
                    P = body_pose(f_td + j * dt)
                    P["ankle"] = {s: tuple(ankles[-1] + Vector((0, V * dt, 0)))}
                    P["foot_pin"] = {s: (c, (prev[c].x, prev[c].y + V * dt))}
                    M, info = rig.solve(P)
                    pts = rig.skinned_sole(M, s)
                    c_new = min(range(len(pts)), key=lambda i: pts[i].z)
                    if c_new == c:
                        break
                    c = c_new
                ankles.append(Vector(info["ankle_target"][s]))
                prev = pts
            _, free = rig.solve(body_pose(f_td + HIND_PERIOD * HIND_DUTY - 1e-4))
            tab[(s, k)] = {"ankles": ankles, "corr_lo": tuple(ankles[-1] - Vector(free["ankle_target"][s])),
                           "corr_td": tuple(ankles[0] - free_td)}
    TAB["hind"] = tab


def _stance_pose(f, s, y, pin=None):
    P = body_pose(f)
    for o in ("L", "R"):
        P["hand"][o] = (ARM[o]["x"], y if o == s else ARM[o]["yc"], 0.10)
        P["hand_ground"][o] = 0.0
    if pin is not None:
        P["hand_pin"] = {s: pin}
    return P


def _stance_socket(rig, f, s, y, pin=None):
    return rig.freeze(_stance_pose(f, s, y, pin))["hand"][s]


def _roll_stance(rig, s):
    """Rolling slab stance, stepped in 1/ROLL_STEPS frames from touchdown to lift-off: the slab
    vertex touching the ground moves with the ground (+V per frame) and the lowest vertex stays on
    z = 0, so the contact rolls along the slab bottom instead of skating (the knuckle socket then
    moves at V plus the roll). -> socket targets per step (touchdown socket == old ground snap)."""
    from mathutils import Vector
    a = ARM[s]
    y0 = a["yc"] - V * N * ARM_DUTY / 2
    M, info = rig.solve(_stance_pose(a["down"], s, y0))
    socks = [Vector(info["hand_target"][s])]
    lo = f"{s}_arm_lower"
    c = rig.slab_low_rest(M[lo], s)
    step = Vector((0, V / ROLL_STEPS, 0))
    for k in range(1, int(round(N * ARM_DUTY * ROLL_STEPS)) + 1):
        M_prev = M
        for _ in range(4):   # the vertex that ends the step lowest is the one that moved with the ground
            P = _stance_pose(a["down"] + k / ROLL_STEPS, s, y0)
            P["hand"][s] = tuple(socks[-1] + step)
            P["hand_pin"] = {s: (c, rig.slab_point(M_prev[lo], s, c) + step, "xy")}
            M, info = rig.solve(P)
            c_new = rig.slab_low_rest(M[lo], s)
            if (c_new - c).length < 1e-6:
                break
            c = c_new
        socks.append(Vector(info["hand_target"][s]))
    return socks


def prepare(rig):
    from mathutils import Vector
    TAB.clear()
    _prepare_hind(rig)
    span = V * N * ARM_DUTY
    for s, a in ARM.items():
        y_front, y_back = a["yc"] - span / 2, a["yc"] + span / 2
        f_td, f_lo = a["down"], a["down"] + N * ARM_DUTY
        roll = _roll_stance(rig, s)
        TAB[s] = {"roll": roll, "front": roll[0], "back": roll[-1],
                  "S_td": rig.shoulder(body_pose(f_td), s), "S_lo": rig.shoulder(body_pose(f_lo), s)}
    TAB["S"] = {}
    for i in range(4 * N + 1):
        f = i / 4
        B = body_pose(f)
        TAB["S"][i] = {s: rig.shoulder(B, s) for s in ("L", "R")}


def _shoulder(f, s):
    i = int(round((f % N) * 4))
    return TAB["S"][i][s]


def walk_pose(f):
    from mathutils import Quaternion, Vector
    P = body_pose(f)
    for s, a in ARM.items():
        w, u = arm_phase(f, s)
        if w is None:
            roll = TAB[s]["roll"]
            x = u * N * ROLL_STEPS
            i = min(int(x), len(roll) - 2)
            P["hand"][s] = tuple(roll[i].lerp(roll[i + 1], x - i))
            P["hand_ground"][s] = None
            continue
        t = TAB[s]
        vb, vf = t["back"] - t["S_lo"], t["front"] - t["S_td"]
        sm = w * w * (3 - 2 * w)
        d = vb.normalized().slerp(vf.normalized(), sm)
        r = vb.length + (vf.length - vb.length) * sm - SWING_SHRINK * math.sin(math.pi * w)
        r = max(r, 0.525)                    # keep the shoulder junction from folding flat
        sg = 1 if s == "L" else -1
        d = Quaternion(Vector((0, 1, 0)), math.radians(-sg * SWING_ABD * math.sin(math.pi * w) ** 0.6)) @ d
        P["hand"][s] = tuple(_shoulder(f, s) + d * r)
        P["hand_ground"][s] = None
        P["hand_twist"][s] = -sg * 8.0 * math.sin(math.pi * w)
    return P


TAKES = {"ForestRootSnarer_Walk": {"frames": N, "loop": True, "pose": walk_pose, "prepare": prepare,
                                   "events": {"L_slab_plant": 0, "R_slab_plant": 8, "R_hind_plant": [1.5, 9.5], "L_hind_plant": [5.5, 13.5]},
                                   "speed_mps": 2.4}}
