"""ForestRootSnarer_Slam (72 frames). Sim contract: slam lands on tick 15 (circle opens under the
hero), roots erupt on tick 36, 36-tick punish window. Reference: slam.mp4 (rear up, slabs
overhead 0.3-1.5 s, slam 1.6 s, stays pinned) retimed to the contract.

0-3 gather | 3-12 rear up on the hind legs, slabs overhead | 12-15 slam, CONTACT 15 (slabs
buried) | 15-33 pinned, straining | 33-36 heave, roots erupt 36 | 36-60 pinned, straining
(punish) | 60-66 wrench the slabs out | 66-72 settle onto Idle frame 0.
"""
import copy
import keys
import stance
from stance import add3, rot_add
from clip_idle_hit import idle_pose

N = 72
CONTACT, ERUPT, RELEASE = 15, 36, 60
PIN = {"L": (0.80, -0.60), "R": (-0.80, -0.64)}
_K = []


def _pose(B0, off=(0, 0, 0), prot=(0, 0, 0), rot=None):
    P = copy.deepcopy(B0)
    P["pelvis_off"] = add3(P["pelvis_off"], off)
    P["pelvis_rot"] = add3(P["pelvis_rot"], prot)
    for b, e in (rot or {}).items():
        rot_add(P, b, e)
    return P


def _hands_from_shoulder(rig, P, offs):
    for s, o in offs.items():
        P["hand"][s] = tuple(rig.shoulder(P, s) + stance_vec(o))
        P["hand_ground"][s] = None
    return P


def stance_vec(o):
    from mathutils import Vector
    return Vector(o)


def _pinned(rig, P, depth):
    for s, (x, y) in PIN.items():
        P["hand"][s] = (x, y, 0.1)
        P["hand_ground"][s] = depth
    return rig.freeze(P)


def prepare(rig):
    _K.clear()
    B0 = rig.freeze(idle_pose(0))
    shrug = lambda a: {"L_clavicle": (0, -a, 0), "R_clavicle": (0, a, 0)}
    k = []
    k.append((0, B0, "smooth"))
    P = _pose(B0, (0, 0.03, -0.035), (5, 0, 0), {"spine_02": (5, 0, 0), "head": (-6, 0, 0), **shrug(-3)})
    k.append((3, P, "smooth"))
    P = _pose(B0, (0, 0.04, -0.04), (-12, 0, 0), {"spine_01": (-2, 0, 0), "spine_02": (-1, 0, 0), "neck": (4, 0, 0), "head": (3, 0, 0), **shrug(2)})
    k.append((5, _hands_from_shoulder(rig, P, {"L": (0.28, -0.60, -0.30), "R": (-0.28, -0.60, -0.30)}), "in"))
    P = _pose(B0, (0, 0.05, -0.04), (-28, 0, 0), {"spine_01": (-5, 0, 0), "spine_02": (-4, 0, 0), "neck": (8, 0, 0), "head": (8, 0, 0), **shrug(6)})
    k.append((7, _hands_from_shoulder(rig, P, {"L": (0.22, -0.42, 0.42), "R": (-0.22, -0.42, 0.42)}), "out"))
    P = _pose(B0, (0, 0.07, -0.045), (-46, 0, 0), {"spine_01": (-8, 0, 0), "spine_02": (-6, 0, 0), "neck": (14, 0, 0), "head": (14, 0, 0), "jaw": (4, 0, 0), **shrug(10)})
    k.append((10, _hands_from_shoulder(rig, P, {"L": (0.10, 0.10, 0.76), "R": (-0.10, 0.10, 0.76)}), "out"))
    P = _pose(B0, (0, 0.075, -0.045), (-50, 0, 0), {"spine_01": (-9, 0, 0), "spine_02": (-9, 0, 0), "neck": (13, 0, 0), "head": (12, 0, 0), "jaw": (10, 0, 0), **shrug(12)})
    k.append((12, _hands_from_shoulder(rig, P, {"L": (0.12, 0.22, 0.72), "R": (-0.12, 0.22, 0.72)}), "smooth"))
    P = _pose(B0, (0, 0.05, -0.06), (-30, 0, 0), {"spine_01": (-4, 0, 0), "spine_02": (-2, 0, 0), "neck": (8, 0, 0), "head": (6, 0, 0), "jaw": (10, 0, 0), **shrug(8)})
    k.append((13, _hands_from_shoulder(rig, P, {"L": (0.16, -0.36, 0.68), "R": (-0.16, -0.36, 0.68)}), "in"))
    P = _pose(B0, (0, 0.02, -0.09), (-8, 0, 0), {"spine_02": (4, 0, 0), "neck": (2, 0, 0), "jaw": (12, 0, 0), **shrug(4)})
    k.append((14, _hands_from_shoulder(rig, P, {"L": (0.28, -0.62, 0.02), "R": (-0.28, -0.62, 0.02)}), "in"))
    P = _pose(B0, (0, -0.01, -0.09), (10, 0, 0), {"spine_02": (8, 0, 0), "neck": (-4, 0, 0), "head": (-8, 0, 0), "jaw": (12, 0, 0)})
    k.append((CONTACT, _pinned(rig, P, -0.11), "in"))
    P = _pose(B0, (0, -0.01, -0.115), (12, 0, 0), {"spine_02": (10, 0, 0), "neck": (2, 0, 0), "head": (-2, 0, 0), "jaw": (10, 0, 0), **shrug(-4)})
    pin = _pinned(rig, P, -0.15)
    k.append((16, pin, "out"))
    hands = {s: pin["hand"][s] for s in "LR"}

    def hold(off, prot, rot):
        P = _pose(B0, off, prot, rot)
        for s in "LR":
            P["hand"][s], P["hand_ground"][s] = hands[s], None
        return P
    strain = {"spine_02": (9, 0, 0), "neck": (6, 0, 0), "head": (-4, 0, 0), "jaw": (4, 0, 0), **shrug(8)}
    k.append((18, hold((0, -0.01, -0.095), (10, 0, 0), {**strain, "jaw": (8, 0, 0)}), "smooth"))
    k.append((23, hold((0, -0.01, -0.10), (9, 0, 0), strain), "smooth"))
    k.append((33, hold((0, 0.01, -0.085), (7, 0, 0), {"spine_02": (6, 0, 0), "neck": (2, 0, 0), "head": (-8, 0, 0), "jaw": (3, 0, 0), **shrug(4)}), "smooth"))
    k.append((ERUPT, hold((0, -0.03, -0.11), (11, 0, 0), {"spine_01": (3, 0, 0), "spine_02": (10, 0, 0), "neck": (-8, 0, 0), "head": (-14, 0, 0), "jaw": (10, 0, 0), **shrug(12)}), "in"))
    k.append((39, hold((0, -0.02, -0.105), (10, 0, 0), {"spine_02": (10, 0, 0), "head": (-8, 0, 0), "jaw": (9, 0, 0), **shrug(9)}), "out"))
    k.append((45, hold((0.01, -0.01, -0.11), (9, 3, 0), {**strain, "spine_02": (9, 2, -4)}), "smooth"))
    k.append((52, hold((-0.01, -0.01, -0.11), (9, -3, 0), {**strain, "spine_02": (9, -2, 4)}), "smooth"))
    k.append((58, hold((0, -0.01, -0.105), (10, 0, 0), strain), "smooth"))
    k.append((RELEASE, hold((0, -0.02, -0.12), (12, 0, 0), {**strain, "jaw": (6, 0, 0)}), "smooth"))
    P = _pose(B0, (0, 0.06, -0.07), (-10, 0, 0), {"spine_02": (-8, 0, 0), "neck": (-2, 0, 0), "head": (-10, 0, 0), "jaw": (8, 0, 0), **shrug(10)})
    P["hand"] = {"L": (0.72, -0.54, 0.40), "R": (-0.72, -0.58, 0.40)}
    P["hand_ground"] = {"L": None, "R": None}
    k.append((63, P, "out"))
    P = _pose(B0, (0, 0.01, -0.03), (3, 0, 0), {"spine_02": (2, 0, 0), "jaw": (4, 0, 0)})
    P["hand"] = {s: add3(B0["hand"][s], (0, 0, 0.07)) for s in "LR"}
    P["hand_ground"] = {"L": None, "R": None}
    k.append((67, P, "in"))
    k.append((69, _pose(B0, (0, 0, -0.018), (2, 0, 0), {"spine_02": (2, 0, 0)}), "out"))
    k.append((N, B0, "smooth"))
    face, chest = _masks(rig)
    INFO["face_clearance"] = {}
    for i, (f, P, e) in enumerate(k):
        if CONTACT <= f <= RELEASE:
            k[i] = (f, _clear_face(rig, P, face, chest, f), e)
    _K.extend(k)


FACE_CLEAR = 0.015   # chin / face stays this far above the ground while the slabs are buried
HEAD_MAX, PITCH_MAX = 12.0, 12.0
INFO = {}


def _masks(rig):
    import numpy as np
    me = rig.mesh
    gi = {g.index: g.name for g in me.vertex_groups}
    dom = [gi[max(v.groups, key=lambda g: g.weight).group] for v in me.data.vertices]
    face = np.array([d in ("head", "jaw", "neck") for d in dom])
    chest = np.array([d in ("spine_02", "spine_01", "pelvis", "L_clavicle", "R_clavicle") for d in dom])
    return face, chest


def _clear_face(rig, P, face, chest, f):
    """Lift the face off the ground while the slabs are buried, keeping the hips low:
    1) neck + head nose-up (negative X) up to HEAD_MAX, 2) pelvis pitch nose-up up to PITCH_MAX,
    3) only then raise the pelvis. Slab sockets are explicit targets here, so they stay put."""
    import math
    P = copy.deepcopy(P)
    head = pitch = 0.0
    z0 = P["pelvis_off"][2]
    for _ in range(14):
        rig.apply(P)
        co = rig.mesh_co()
        fz, cz = float(co[face][:, 2].min()), float(co[chest][:, 2].min())
        need = FACE_CLEAR - min(fz, cz) + 0.002
        if need <= 0.001:
            break
        if head >= HEAD_MAX and pitch >= PITCH_MAX:
            P["pelvis_off"] = add3(P["pelvis_off"], (0, 0, need))
        elif head < HEAD_MAX and fz <= cz:
            d = min(math.degrees(need / 0.35), HEAD_MAX - head)
            rot_add(P, "neck", (-0.4 * d, 0, 0))
            rot_add(P, "head", (-0.6 * d, 0, 0))
            head += d
        else:
            d = min(math.degrees(need / 0.9), PITCH_MAX - pitch)
            P["pelvis_rot"] = add3(P["pelvis_rot"], (-d, 0, 0))
            pitch += d
    INFO["face_clearance"][f] = {"head_neck_up_deg": round(head, 1), "pelvis_pitch_up_deg": round(pitch, 1),
                                 "pelvis_raised_m": round(P["pelvis_off"][2] - z0, 4), "face_min_z": round(fz, 4)}
    return P


def slam_pose(f):
    P = keys.at(_K, f)
    env = keys.ease((f - 16) / 4, "smooth") * (1 - keys.ease((f - 58) / 4, "smooth"))
    env *= 0.55 + 0.45 * keys.ease((f - 34) / 3, "smooth")
    if env > 0:
        P["pelvis_off"] = add3(P["pelvis_off"], (0.003 * keys.tremble(f, 1), 0.002 * keys.tremble(f, 2), 0.004 * keys.tremble(f, 3)), env)
        P["pelvis_rot"] = add3(P["pelvis_rot"], (0.8 * keys.tremble(f, 4), 0.8 * keys.tremble(f, 5), 0.5 * keys.tremble(f, 6)), env)
        rot_add(P, "spine_02", (1.2 * keys.tremble(f, 7), 0, 0.8 * keys.tremble(f, 8)), env)
        rot_add(P, "L_clavicle", (0, 1.8 * keys.tremble(f, 9), 0), env)
        rot_add(P, "R_clavicle", (0, 1.8 * keys.tremble(f, 10), 0), env)
        rot_add(P, "head", (1.2 * keys.tremble(f, 11), 1.0 * keys.tremble(f, 12), 0), env)
    return P


TAKES = {"ForestRootSnarer_Slam": {"frames": N, "loop": False, "pose": slam_pose, "prepare": prepare,
                                   "events": {"contact": CONTACT, "roots_erupt": ERUPT, "release": RELEASE}}}
