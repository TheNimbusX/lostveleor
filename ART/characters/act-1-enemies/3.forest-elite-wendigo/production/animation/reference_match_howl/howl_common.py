"""Shared rig maths for the Howl reference match (same parameterisation as Claw/Leap).

x = [pelvis world offset (3)] + [bone-local rotation vector (3) per bone after root].
Only rotations and the pelvis offset are posed, so bone lengths cannot change.
"""
import json
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation

HERE = Path(__file__).resolve().parent
src = json.loads((HERE / 'fit_source.json').read_text())
names = [b['name'] for b in src['bones']]
NB = len(names)
rests = np.array([b['matrix'] for b in src['bones']])
inverses = np.linalg.inv(rests)
parents = [names.index(b['parent']) if b['parent'] else -1 for b in src['bones']]
locals_ = np.array([inverses[p] @ rests[i] if p >= 0 else rests[i] for i, p in enumerate(parents)])
verts = np.c_[np.array(src['vertices']), np.ones(len(src['vertices']))]
tris = np.array(src['triangles'])
weights = np.zeros((len(verts), NB))
for i, gs in enumerate(src['weights']):
    for n, w in gs:
        weights[i, names.index(n)] = w
weights /= weights.sum(1, keepdims=True)
dominant = weights.argmax(1)

F = np.array([1., 1., 0.]) / np.sqrt(2)      # creature forward (FacingGuide)
SIDE = np.array([1., -1., 0.]) / np.sqrt(2)  # creature right
UP = np.array([0., 0., 1.])
PITCH_AXIS = -SIDE                           # +angle pitches the chest forward/down (as Leap/Death)


def sl(n):
    i = names.index(n)
    return slice(3 + (i - 1) * 3, 3 + i * 3)


def fk(x):
    basis = np.tile(np.eye(4), (NB, 1, 1))
    basis[1:, :3, :3] = Rotation.from_rotvec(np.asarray(x[3:]).reshape(-1, 3)).as_matrix()
    basis[1, :3, 3] = rests[1, :3, :3].T @ np.asarray(x[:3])
    pose = []
    for i, p in enumerate(parents):
        pose.append((pose[p] if p >= 0 else np.eye(4)) @ locals_[i] @ basis[i])
    return np.array(pose)


def skin(x, idx=None):
    v = verts if idx is None else verts[idx]
    w = weights if idx is None else weights[idx]
    return np.einsum('vb,bij,vj->vi', w, fk(x) @ inverses, v)[:, :3]


def assign(x, n, world_rot):
    """Set bone n so its armature-space rotation equals world_rot (parents fixed)."""
    i = names.index(n)
    parent = fk(x)[parents[i], :3, :3]
    x[sl(n)] = Rotation.from_matrix((parent @ locals_[i, :3, :3]).T @ world_rot).as_rotvec()


def world_turn(x, n, axis, radians):
    i = names.index(n)
    assign(x, n, Rotation.from_rotvec(np.asarray(axis) * radians).as_matrix() @ fk(x)[i, :3, :3])


def align(a, b):
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b)
    c = np.cross(a, b); s = np.linalg.norm(c)
    if s < 1e-12:
        return np.eye(3)
    return Rotation.from_rotvec(c / s * np.arctan2(s, a @ b)).as_matrix()


def bone_len(a, b):
    return float(np.linalg.norm(rests[names.index(b), :3, 3] - rests[names.index(a), :3, 3]))


def two_bone(x, upper, lower, end, target, pole):
    """Analytic IK: place the head of `end` on target without stretching; keeps end's world rotation."""
    ui, li, ei = names.index(upper), names.index(lower), names.index(end)
    keep = fk(x)[ei, :3, :3].copy()
    T = fk(x)
    hip = T[ui, :3, 3]
    l1 = bone_len(upper, lower); l2 = bone_len(lower, end)
    d = np.asarray(target) - hip; dist = np.linalg.norm(d)
    reach = l1 + l2 - 1e-7
    if dist > reach:
        d = d / dist * reach; dist = reach
    axis = d / dist
    pv = np.asarray(pole, float) - axis * (np.asarray(pole) @ axis); pv /= np.linalg.norm(pv)
    along = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist)
    knee = hip + axis * along + pv * np.sqrt(max(0.0, l1 * l1 - along * along))
    end_pt = hip + d
    # rotate upper so its child (lower head) goes to knee, then lower so end head goes to end_pt
    cur_u = T[ui, :3, :3]; v_now = T[li, :3, 3] - hip
    assign(x, upper, align(v_now, knee - hip) @ cur_u)
    T = fk(x); cur_l = T[li, :3, :3]; v_now = T[ei, :3, 3] - T[li, :3, 3]
    assign(x, lower, align(v_now, end_pt - knee) @ cur_l)
    assign(x, end, keep)
    return float(np.linalg.norm(fk(x)[ei, :3, 3] - target))


def camera_from(par):
    az, el, scale, cx, cy = par
    forward = np.array([np.sin(az) * np.cos(el), np.cos(az) * np.cos(el), np.sin(el)])
    right = np.cross([0, 0, 1], forward); right /= np.linalg.norm(right)
    up = np.cross(forward, right)
    return forward, right, up


def project(points, par):
    az, el, scale, cx, cy = par
    forward, right, up = camera_from(par)
    d = np.asarray(points)[..., :3] - [0, 0, 1.55]
    depth = d @ forward
    q = np.stack([d @ right, d @ up], -1)
    return q * np.array([960 / scale, -960 / scale]) * 5.5 / (5.5 - depth[..., None]) + [cx, cy]


def rotation_speed_deg(a, b):
    qa = Rotation.from_rotvec(np.asarray(a)[3:].reshape(-1, 3))
    qb = Rotation.from_rotvec(np.asarray(b)[3:].reshape(-1, 3))
    return np.degrees((qa.inv() * qb).magnitude())


# Mass proxy: skinned vertices weighted by their share of the surface area (the mesh
# has no volume data; area weighting avoids dense leaf clusters dominating).
_a = verts[tris[:, 1], :3] - verts[tris[:, 0], :3]
_b = verts[tris[:, 2], :3] - verts[tris[:, 0], :3]
_area = np.linalg.norm(np.cross(_a, _b), axis=1) / 2
MASS = np.zeros(len(verts))
for _k in range(3):
    np.add.at(MASS, tris[:, _k], _area / 3)
MASS /= MASS.sum()


def com(x):
    return MASS @ skin(x)


# Segment mass model (fractions of body mass, placed at each bone's midpoint): a woody
# biped with heavy hips/chest, light antlered skull and long light arms.
SEG_MASS = {'pelvis': .14, 'spine_01': .10, 'spine_02': .14, 'neck': .03, 'head': .07,
            'L_clavicle': .02, 'R_clavicle': .02, 'L_arm_upper': .035, 'R_arm_upper': .035,
            'L_arm_lower': .03, 'R_arm_lower': .03, 'L_hand': .025, 'R_hand': .025,
            'L_leg_upper': .08, 'R_leg_upper': .08, 'L_leg_lower': .04, 'R_leg_lower': .04,
            'L_foot': .015, 'R_foot': .015, 'L_toe': .005, 'R_toe': .005}
_seg = [(names.index(n), m) for n, m in SEG_MASS.items()]
_tot = sum(m for _, m in _seg)
_len = {i: np.linalg.norm(rests[i, :3, 1]) and float(np.linalg.norm(
    (rests[[j for j, p in enumerate(parents) if p == i][0], :3, 3] - rests[i, :3, 3])) if any(p == i for p in parents) else .1)
    for i, _ in _seg}


def com_bones(x):
    T = fk(x)
    c = np.zeros(3)
    for i, m in _seg:
        mid = T[i, :3, 3] + T[i, :3, 1] * _len[i] * .5
        c += m * mid
    return c / _tot
