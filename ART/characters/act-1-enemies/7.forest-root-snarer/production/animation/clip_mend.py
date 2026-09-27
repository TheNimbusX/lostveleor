"""ForestRootSnarer_Mend (50 frames) - support heal «Волна из корней» (owner, 27.09).
Sim contract: t0 the plant starts (stab); channel t0-30 with both slabs in the ground; the heal ring
goes out on t30; recovery t30-50 (pull the slabs out, settle onto Idle frame 0 by 50).
Target frame: production/vfx_target_frames_2026-09-27/1-mend-ring.png (both slabs planted, head low
between them, mushroom dome up, the ring runs out from the slabs).

0-5 short lift (slabs to knee height, no rearing up) | 5-8 stab, CONTACT 8 (both slabs in the
ground) | 8-30 channel: rooted, body higher than in Slam, head lowered between the slabs, slow
calm pulse (swell 15 / push 20 / swell 24 / deepest push 28), no tremble | RELEASE 30: the body
heaves up, back and dome swell (the mushrooms on the back puff) | 30-38 stands open, slabs still in
the ground | 38-50 pulls the slabs out and settles onto Idle frame 0.
Built on the Slam contact pose (clip_slam._pose / _pinned), different rhythm.
"""
import copy
import math
import keys
import stance
from stance import add3, rot_add
from clip_idle_hit import idle_pose, prepare_idle
from clip_slam import _pose, _hands_from_shoulder, _masks

N = 50
CONTACT, RELEASE, PULL = 8, 30, 38
PIN = {"L": (0.69, -0.58), "R": (-0.69, -0.62)}
DEPTH_CONTACT, DEPTH_HOLD = -0.09, -0.12
FACE_CLEAR = 0.03      # the lowered head stays at least this far above the ground
_K = []
INFO = {}


def shrug(a):
    return {"L_clavicle": (0, -a, 0), "R_clavicle": (0, a, 0)}


def _pinned(rig, P, depth):
    for s, (x, y) in PIN.items():
        P["hand"][s] = (x, y, 0.1)
        P["hand_ground"][s] = depth
    return rig.freeze(P)


def prepare(rig):
    _K.clear()
    INFO.clear()
    prepare_idle(rig)                       # Idle frame 0 exactly as the Idle take solves it
    B0 = rig.freeze(idle_pose(0))
    k = [(0, B0, "smooth")]
    # 0-5 short lift: weight back a little, slabs swing forward-up to knee height
    P = _pose(B0, (0, 0.025, -0.01), (-4, 0, 0), {"spine_02": (-3, 0, 0), "neck": (2, 0, 0), "head": (-3, 0, 0), **shrug(3)})
    # 27.09 check fix: 3 is a passing pose, so ease IN to it ("smooth" stopped the lift on 3 and the
    # "out" segment restarted it at full speed: knuckle 9 -> 16 -> 9 -> 27 cm/frame, a hitch)
    k.append((3, _hands_from_shoulder(rig, P, {"L": (0.40, -0.40, -0.26), "R": (-0.40, -0.40, -0.26)}), "in"))
    P = _pose(B0, (0, 0.035, -0.01), (-9, 0, 0), {"spine_02": (-5, 0, 0), "neck": (2, 0, 0), "head": (-5, 0, 0), "jaw": (3, 0, 0), **shrug(7)})
    k.append((5, _hands_from_shoulder(rig, P, {"L": (0.46, -0.32, 0.02), "R": (-0.46, -0.32, 0.02)}), "out"))
    # 5-8 stab straight down, CONTACT 8 (Slam contact pose, body higher, head already lowering)
    P = _pose(B0, (0, -0.005, -0.06), (6, 0, 0), {"spine_02": (5, 0, 0), "neck": (4, 0, 0), "head": (6, 0, 0), "jaw": (4, 0, 0)})
    k.append((CONTACT, _pinned(rig, P, DEPTH_CONTACT), "in"))
    P = _pose(B0, (0, -0.01, -0.07), (7, 0, 0), {"spine_02": (6, 0, 0), "neck": (5, 0, 0), "head": (9, 0, 0), "jaw": (3, 0, 0), **shrug(-3)})
    pin = _pinned(rig, P, DEPTH_HOLD)
    k.append((10, pin, "out"))
    hands = {s: pin["hand"][s] for s in "LR"}

    def hold(off, prot, rot):
        P = _pose(B0, off, prot, rot)
        for s in "LR":
            P["hand"][s], P["hand_ground"][s] = hands[s], None
        return P
    # 8-30 channel: calm pulse (p = +1 swell, -1 push into the ground), rooted, head kept low
    def chan(p):
        return hold((0, -0.008 + 0.005 * p, -0.066 + 0.016 * p), (5.5 - 1.0 * p, 0, 0),
                    {"spine_01": (-3 * (1 + p), 0, 0), "spine_02": (4 + 2 * p, 0, 0), "neck": (5, 0, 0),
                     "head": (10 + 2 * p, 0, 0), "jaw": (2, 0, 0), **shrug(5 * p)})
    k.append((15, chan(1.0), "smooth"))
    k.append((20, chan(-1.0), "smooth"))
    k.append((24, chan(1.0), "smooth"))
    gather = chan(-1.4)
    k.append((28, gather, "smooth"))
    # RELEASE 30: heave up, back / dome swell (mushrooms puff), mouth opens; shoulders stay down on
    # the anchored slabs (the heave comes from the hips and the back, not from the shoulders)
    full = hold((0, -0.02, -0.01), (-1, 0, 0), {"spine_01": (-12, 0, 0), "spine_02": (6, 0, 0), "neck": (3, 0, 0), "head": (5, 0, 0), "jaw": (10, 0, 0), **shrug(-10)})
    k.append((RELEASE, _heave(rig, gather, full), "out"))
    # 30-38 stands open, slabs still in the ground
    k.append((33, hold((0, -0.004, -0.065), (4, 0, 0), {"spine_01": (1, 0, 0), "spine_02": (3, 0, 0), "neck": (2, 0, 0), "head": (4, 0, 0), "jaw": (4, 0, 0), **shrug(-2)}), "smooth"))
    k.append((PULL, hold((0, 0, -0.05), (2, 0, 0), {"spine_01": (-2, 0, 0), "spine_02": (1, 0, 0), "neck": (1, 0, 0), "head": (1, 0, 0), "jaw": (2, 0, 0), **shrug(2)}), "smooth"))
    # 38-50 pull the slabs out and settle onto Idle frame 0
    P = _pose(B0, (0, 0.035, -0.04), (-5, 0, 0), {"spine_02": (-4, 0, 0), "head": (-4, 0, 0), "jaw": (4, 0, 0), **shrug(7)})
    P["hand"] = {"L": (0.68, -0.56, 0.26), "R": (-0.68, -0.60, 0.26)}
    P["hand_ground"] = {"L": None, "R": None}
    k.append((42, P, "out"))
    P = _pose(B0, (0, 0.01, -0.02), (2, 0, 0), {"spine_02": (1, 0, 0), "jaw": (2, 0, 0)})
    P["hand"] = {s: add3(B0["hand"][s], (0, 0, 0.05)) for s in "LR"}
    P["hand_ground"] = {"L": None, "R": None}
    k.append((46, P, "in"))
    k.append((48, _pose(B0, (0, 0, -0.012), (1, 0, 0), {"spine_02": (1, 0, 0)}), "out"))
    k.append((N, B0, "smooth"))
    face, chest = _masks(rig)
    INFO["face_clearance"] = {}
    for i, (f, P, e) in enumerate(k):
        if CONTACT <= f <= PULL:
            k[i] = (f, _clear_face(rig, P, face, chest, f), e)
    _K.extend(k)


def _reach(rig, P):
    return max(rig.solve(P)[1]["reach_err"].values())


def _heave(rig, gather, full):
    """Largest heave (blend gather -> full) that keeps both slabs on their pinned sockets and the hind
    feet planted (IK reach error <= 0.2 mm)."""
    lo, hi = 0.0, 1.0
    if _reach(rig, stance.blend(gather, full, 1.0)) > 2e-4:
        for _ in range(16):
            mid = (lo + hi) / 2
            lo, hi = (mid, hi) if _reach(rig, stance.blend(gather, full, mid)) <= 2e-4 else (lo, mid)
        hi = lo
    INFO["release_heave_fraction"] = round(hi, 3)
    return stance.blend(gather, full, hi)


def _clear_face(rig, P, face, chest, f):
    """Keep the lowered head: only lift the head a little (<= 6 deg), then raise the pelvis."""
    P = copy.deepcopy(P)
    head = 0.0
    z0 = P["pelvis_off"][2]
    for _ in range(14):
        rig.apply(P)
        co = rig.mesh_co()
        fz, cz = float(co[face][:, 2].min()), float(co[chest][:, 2].min())
        need = FACE_CLEAR - min(fz, cz) + 0.002
        if need <= 0.002:
            break
        if head < 6.0 and fz <= cz:
            d = min(math.degrees(need / 0.35), 6.0 - head)
            rot_add(P, "head", (-d, 0, 0))
            head += d
        else:
            P["pelvis_off"] = add3(P["pelvis_off"], (0, 0, need))
    INFO["face_clearance"][f] = {"head_up_deg": round(head, 1), "pelvis_raised_m": round(P["pelvis_off"][2] - z0, 4),
                                 "face_min_z": round(fz, 4), "chest_min_z": round(cz, 4)}
    return P


def mend_pose(f):
    P = keys.at(_K, f)
    # calm breath under the channel: slow, small, no muscle tremble (that is the Slam)
    env = keys.ease((f - 10) / 4, "smooth") * (1 - keys.ease((f - 26) / 3, "smooth"))
    if env > 0:
        w = keys.wobble(f, 3, (0.8, 1.3))
        rot_add(P, "head", (0, 1.2 * w, 0.8 * w), env)
        rot_add(P, "spine_02", (0, 0, 0.8 * keys.wobble(f, 5, (0.7, 1.1))), env)
    return P


TAKES = {"ForestRootSnarer_Mend": {"frames": N, "loop": False, "pose": mend_pose, "prepare": prepare,
                                   "events": {"contact": CONTACT, "release": RELEASE, "pull_out": PULL}}}
