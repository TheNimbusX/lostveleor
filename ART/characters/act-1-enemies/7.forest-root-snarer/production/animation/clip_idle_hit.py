"""ForestRootSnarer_Idle (60-frame loop, heavy breathing) and ForestRootSnarer_Hit (12-frame flinch).
Both keep all four contacts planted; Hit starts and ends on Idle frame 0."""
import math
import stance
from stance import add3, rot_add

IDLE_N = 60
HIT_N = 12


IDLE_PIN = {}   # {side: (slab rest point, world point)} filled by prepare_idle
IDLE_PIN_MODE = "xy"
IDLE_PIN_SIDES = ("R",)   # L rocks between two far contacts on its flat bottom: pinning it is worse


def prepare_idle(rig):
    """Pin the R slab's frame-0 contact vertex in the ground plane (the snap keeps the lowest vertex
    on z = 0): the breathing rotates the slab about its contact instead of skating the slab tip
    2.6 cm back and forth under the fixed knuckle socket (32 -> 11 mm). Frame 0 is unchanged."""
    IDLE_PIN.clear()
    M, _ = rig.solve(_idle_raw(0))
    for s in IDLE_PIN_SIDES:
        lo = M[f"{s}_arm_lower"]
        c = rig.slab_low_rest(lo, s)
        IDLE_PIN[s] = (c, rig.slab_point(lo, s, c))


def idle_pose(f):
    P = _idle_raw(f)
    if IDLE_PIN:
        P["hand_pin"] = {s: (c, p, IDLE_PIN_MODE) for s, (c, p) in IDLE_PIN.items()}
    return P


def _idle_raw(f):
    P = stance.base()
    t = f / IDLE_N
    b = math.sin(2 * math.pi * 2 * t)            # two deep breaths per loop (inhale > 0)
    lag = math.sin(2 * math.pi * 2 * t - 0.7)    # head / jaw follow the chest a little late
    s = math.sin(2 * math.pi * t)                # one slow weight shift per loop
    P["pelvis_off"] = add3(P["pelvis_off"], (0.010 * s, 0.006 * b, 0.016 * b))
    P["pelvis_rot"] = add3(P["pelvis_rot"], (-1.0 * b, 1.4 * s, 0.8 * s))
    rot_add(P, "spine_01", (-1.5 * b, 0, 0))
    rot_add(P, "spine_02", (-3.5 * b, 0, 1.5 * s))
    rot_add(P, "L_clavicle", (0, -3.0 * b, 0))
    rot_add(P, "R_clavicle", (0, 3.0 * b, 0))
    rot_add(P, "neck", (1.2 * lag, 0, -1.0 * s))
    rot_add(P, "head", (-2.0 * lag, 0, -2.5 * s))
    rot_add(P, "jaw", (2.5 * (1 - lag), 0, 0))    # mouth works open on the exhale (<= 5 deg)
    return P


def hit_pose(f):
    P = idle_pose(0)
    P.pop("hand_pin", None)     # the slabs skid on purpose; frame 0 == pinned Idle frame 0
    k = f / 2.5
    env = k * math.exp(1 - k) * (1 - stance_smooth((f - 7.0) / 5.0))
    P["pelvis_off"] = add3(P["pelvis_off"], (0.012, 0.055, 0.008), env)
    P["pelvis_rot"] = add3(P["pelvis_rot"], (-5.0, -2.0, 3.0), env)
    rot_add(P, "spine_01", (-3.0, 0, 0), env)
    rot_add(P, "spine_02", (-6.0, 2.0, -4.0), env)
    rot_add(P, "neck", (-5.0, 0, 0), env)
    rot_add(P, "head", (-12.0, 4.0, 6.0), env)
    rot_add(P, "jaw", (10.0, 0, 0), env)
    rot_add(P, "L_clavicle", (0, -6.0, 0), env)
    rot_add(P, "R_clavicle", (0, 3.0, 0), env)
    # the slabs skid back a little with the jolt (the R slab sits near full reach)
    for s, dy, lift in (("L", 0.035, 0.02), ("R", 0.08, 0.08)):
        P["hand"][s] = add3(P["hand"][s], (0.0, dy, 0.0), env)
        P["hand_ground"][s] = lift * env
    return P


def stance_smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


TAKES = {
    "ForestRootSnarer_Idle": {"frames": IDLE_N, "loop": True, "pose": idle_pose, "prepare": prepare_idle},
    "ForestRootSnarer_Hit": {"frames": HIT_N, "loop": False, "pose": hit_pose},
}
