"""ForestRootSnarer_Death (45 frames). Reference death.mp4: recoils, rears, then collapses flat
on the belly with the slabs splayed out. 0-8 recoil/stagger | 8-19 lurch and buckle |
25 belly hits the ground (GROUND contact) | 28 bounce | 32-45 lies still, touching the ground.
The floor pose is snapped so the belly rests on z = 0 with the chin on the ground too (no floating)."""
import copy
import keys
from stance import add3, rot_add
from clip_idle_hit import idle_pose

N = 45
GROUND = 25
HEAD_UP_MAX, PITCH_UP_MAX = 12.0, 15.0
_K = []
INFO = {}


def _pose(B0, off=(0, 0, 0), prot=(0, 0, 0), rot=None):
    P = copy.deepcopy(B0)
    P["pelvis_off"] = add3(P["pelvis_off"], off)
    P["pelvis_rot"] = add3(P["pelvis_rot"], prot)
    for b, e in (rot or {}).items():
        rot_add(P, b, e)
    return P


def _floor(B0, dz, head_up=0.0, pitch_up=0.0):
    P = _pose(B0, (0.0, -0.02, dz), (3 - pitch_up, 3, 0), {"spine_01": (2, 0, 0), "spine_02": (3, 0, -2),
                                                "neck": (10 - 0.4 * head_up, 0, 4),
                                                "head": (10 - 0.6 * head_up, 6, 12), "jaw": (8, 0, 0),
                                                "L_clavicle": (0, 6, 0), "R_clavicle": (0, -6, 0)})
    P["hand"] = {"L": (0.98, -0.46, 0.1), "R": (-0.96, -0.50, 0.1)}
    P["hand_ground"] = {"L": 0.0, "R": 0.0}
    P["hand_twist"] = {"L": -25.0, "R": 25.0}
    P["foot"] = {"L": (0.68, 0.74, 0.0), "R": (-0.66, 0.68, 0.0)}
    P["foot_rot"] = {"L": (-20.0, -20.0, 20.0), "R": (-20.0, 20.0, -20.0)}
    return P


def _masks(rig):
    import numpy as np
    me = rig.mesh
    gi = {g.index: g.name for g in me.vertex_groups}
    dom = [gi[max(v.groups, key=lambda g: g.weight).group] for v in me.data.vertices]
    chest = np.array([d in ("pelvis", "spine_01", "spine_02") for d in dom])
    face = np.array([d in ("neck", "head", "jaw") for d in dom])
    return chest, face


def _snap_feet(rig, P, target=-0.002):
    """Hind soles as the game skins them: the foot mesh is part-weighted to the shin, so the rigid
    sole approximation left the L foot 1 cm in the ground and the R foot 9 mm up. Shift each sole
    target until the lowest skinned foot vertex sits on `target`."""
    import numpy as np
    gi = {g.index: g.name for g in rig.mesh.vertex_groups}
    dom = np.array([gi[max(v.groups, key=lambda g: g.weight).group] for v in rig.mesh.data.vertices])
    P = copy.deepcopy(P)
    for _ in range(5):
        rig.apply(P)
        co = rig.mesh_co()
        err = {s: target - float(co[dom == f"{s}_foot"][:, 2].min()) for s in ("L", "R")}
        if max(abs(e) for e in err.values()) < 5e-4:
            break
        for s, e in err.items():
            P["foot"][s] = add3(P["foot"][s], (0.0, 0.0, e))
    return P


def _snap_floor(rig, B0):
    """Drop the pelvis until the belly (lowest pelvis/spine vertex) rests at z = -3 mm; the throat
    and chin would then sink, so the head comes nose-up (up to HEAD_UP_MAX) and then the whole
    body pitches nose-up (up to PITCH_UP_MAX) until the face rests on the ground too, instead of
    propping the chest 9 cm up on the chin (reference: flat on the belly, face between the slabs)."""
    import math
    chest, face = _masks(rig)
    dz, head, pitch = -0.28, 0.0, 0.0
    for _ in range(24):
        rig.apply(_floor(B0, dz, head, pitch))
        co = rig.mesh_co()
        cz, fz = float(co[chest][:, 2].min()), float(co[face][:, 2].min())
        if abs(cz + 0.003) < 5e-4 and fz > -0.0045:
            break
        dz -= cz + 0.003
        need = -0.003 - fz
        if need > 0.0015:
            if head < HEAD_UP_MAX:
                head = min(HEAD_UP_MAX, head + math.degrees(need / 0.35))
            else:
                pitch = min(PITCH_UP_MAX, pitch + math.degrees(need / 0.8))
    INFO["floor_pelvis_drop_m"] = round(dz, 4)
    INFO["floor_head_up_deg"] = round(head, 2)
    INFO["floor_pitch_up_deg"] = round(pitch, 2)
    INFO["floor_belly_face_min_z_m"] = [round(cz, 4), round(fz, 4)]
    return dz, head, pitch


def prepare(rig):
    _K.clear()
    B0 = rig.freeze(idle_pose(0))
    dz, hu, pu = _snap_floor(rig, B0)
    k = [(0, B0, "smooth")]
    P = _pose(B0, (0, 0.06, 0.015), (-10, 0, -3), {"spine_02": (-8, 0, 4), "neck": (-6, 0, 0), "head": (-14, 0, -6),
                                                   "jaw": (12, 0, 0), "L_clavicle": (0, -8, 0), "R_clavicle": (0, 8, 0)})
    P["hand"]["L"] = add3(B0["hand"]["L"], (0.05, 0.10, 0.22))
    P["hand"]["R"] = add3(B0["hand"]["R"], (0.0, 0.17, 0.08))
    k.append((3, P, "out"))
    P = _pose(B0, (-0.03, 0.08, 0.0), (-16, -4, -6), {"spine_02": (-6, 3, 6), "neck": (-4, 0, 0), "head": (-10, 6, -10),
                                                       "jaw": (10, 0, 0), "L_clavicle": (0, -10, 0), "R_clavicle": (0, 6, 0)})
    P["hand"]["L"] = add3(B0["hand"]["L"], (0.12, 0.05, 0.34))
    P["hand"]["R"] = add3(B0["hand"]["R"], (-0.02, 0.21, 0.13))
    k.append((8, P, "smooth"))
    P = _pose(B0, (-0.05, -0.04, -0.05), (6, 6, 8), {"spine_02": (6, -4, -4), "neck": (2, 0, 0), "head": (-2, -4, 8), "jaw": (8, 0, 0)})
    P["hand"] = {"L": (0.82, -0.58, 0.1), "R": (-0.74, -0.64, 0.1)}
    P["hand_ground"] = {"L": 0.0, "R": 0.0}
    k.append((13, rig.freeze(P), "in"))
    P = _pose(B0, (-0.03, -0.02, -0.12), (4, 8, 4), {"spine_02": (4, -2, -2), "neck": (3, 0, 0), "head": (0, 0, 10), "jaw": (8, 0, 0)})
    P["hand"] = {"L": (0.88, -0.55, 0.1), "R": (-0.84, -0.60, 0.1)}
    P["hand_ground"] = {"L": 0.0, "R": 0.0}
    P["foot"] = {"L": (0.62, 0.70, 0.0), "R": (-0.62, 0.62, 0.0)}
    k.append((19, rig.freeze(P), "smooth"))
    k.append((GROUND, rig.freeze(_snap_feet(rig, _floor(B0, dz, hu, pu))), "in"))
    P = _floor(B0, dz + 0.025, hu, pu)
    rot_add(P, "head", (-6, 0, 0))
    rot_add(P, "neck", (-3, 0, 0))
    k.append((28, rig.freeze(_snap_feet(rig, P)), "out"))
    P = _floor(B0, dz, hu, pu)
    rot_add(P, "head", (2, 0, 0))
    k.append((32, rig.freeze(_snap_feet(rig, P)), "in"))
    P = _floor(B0, dz - 0.002, hu, pu)
    rot_add(P, "head", (2, 0, 0))
    rot_add(P, "jaw", (-1, 0, 0))
    k.append((N, rig.freeze(_snap_feet(rig, P)), "smooth"))
    _K.extend(k)


def death_pose(f):
    return keys.at(_K, f)


TAKES = {"ForestRootSnarer_Death": {"frames": N, "loop": False, "pose": death_pose, "prepare": prepare,
                                    "events": {"ground": GROUND}}}
