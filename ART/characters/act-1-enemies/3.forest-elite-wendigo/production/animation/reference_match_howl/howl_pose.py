"""Parametric pose builder for the Howl key poses.

A key pose is a small table of readable controls (degrees / metres in the
creature frame F=forward, S=right, U=up). The builder turns it into the rig
parameterisation x starting from the Idle@0 stance:

  pelvis offset + pitch/yaw/roll, spine/neck/head pitch/yaw/roll,
  clavicle elevation/protraction, wrist targets with elbow poles (2-bone IK),
  hand world rotation, and fixed-length leg IK onto the Idle ankle positions
  with the Idle foot/toe world rotations (feet cannot slide by construction).

Pitch > 0 bends forward/down, yaw > 0 turns left, roll > 0 leans right.
"""
import json
import numpy as np
from scipy.spatial.transform import Rotation
from howl_common import (HERE, names, fk, skin, assign, world_turn, two_bone, bone_len, sl,
                         F, SIDE, UP, weights, verts)

IDLE = np.array(json.loads((HERE / 'stance_samples.json').read_text())['poses']['Wendigo_Idle@0'])
IDLE_T = fk(IDLE)
IX = {n: i for i, n in enumerate(names)}
AX = {'F': F, 'S': SIDE, 'U': UP}
PITCH, YAW, ROLL = -SIDE, UP, F
ANKLE = {s: IDLE_T[IX[s + '_foot'], :3, 3].copy() for s in 'LR'}
FOOT_ROT = {s: IDLE_T[IX[s + '_foot'], :3, :3].copy() for s in 'LR'}
HAND_ROT = {s: IDLE_T[IX[s + '_hand'], :3, :3].copy() for s in 'LR'}
WRIST0 = {s: IDLE_T[IX[s + '_hand'], :3, 3].copy() for s in 'LR'}
OUT = {'L': -SIDE, 'R': SIDE}
LEG_REACH = {s: bone_len(s + '_leg_upper', s + '_leg_lower') + bone_len(s + '_leg_lower', s + '_foot') for s in 'LR'}


def _bend_dir(a, b, c):
    h, k, e = (IDLE_T[IX[n], :3, 3] for n in (a, b, c))
    ax = (e - h) / np.linalg.norm(e - h)
    off = (k - h) - ax * ((k - h) @ ax)
    return off / np.linalg.norm(off)


KNEE0 = {s: _bend_dir(s + '_leg_upper', s + '_leg_lower', s + '_foot') for s in 'LR'}
ELBOW0 = {s: _bend_dir(s + '_arm_upper', s + '_arm_lower', s + '_hand') for s in 'LR'}

# Hand geometry: every vertex that follows the hand, and the claw blades beyond the
# knuckles (hand-bone local y > 0.26 m). Only claw blades may touch/scratch the ground.
HAND_V = {s: np.where(weights[:, IX[s + '_hand']] > .3)[0] for s in 'LR'}
from howl_common import inverses as _inv
TIP_V = {}
for s in 'LR':
    g = HAND_V[s]
    local_y = (_inv[IX[s + '_hand']] @ verts[g].T)[1]
    TIP_V[s] = g[local_y > .26]


def cf(v):
    """creature-frame (f, s, u) -> world"""
    return v[0] * F + v[1] * SIDE + v[2] * UP


def rv(deg_fsu):
    return Rotation.from_rotvec(np.deg2rad(cf(deg_fsu))).as_matrix()


def turn(x, bone, pyr):
    p, y, r = pyr
    if p: world_turn(x, bone, PITCH, np.deg2rad(p))
    if y: world_turn(x, bone, YAW, np.deg2rad(y))
    if r: world_turn(x, bone, ROLL, np.deg2rad(r))


def build(P, return_info=False):
    x = IDLE.copy()
    pel = P.get('pelvis', {})
    x[:3] = IDLE[:3] + cf(pel.get('off', (0, 0, 0)))
    turn(x, 'pelvis', pel.get('rot', (0, 0, 0)))
    for b in ('spine_01', 'spine_02', 'neck', 'head'):
        turn(x, b, P.get(b, (0, 0, 0)))
    for s, sign in (('L', 1), ('R', -1)):
        elev, prot = P.get(s + '_clav', (0, 0))
        if elev: world_turn(x, s + '_clavicle', F * sign, np.deg2rad(elev))
        if prot: world_turn(x, s + '_clavicle', -UP * sign, np.deg2rad(prot))
    # legs: keep the ankles on the Idle plant; lower the pelvis if a hip would out-reach
    info = {'pelvis_lowered': 0.0}
    for it in range(30):
        T = fk(x)
        excess = max(np.linalg.norm(ANKLE[s] - T[IX[s + '_leg_upper'], :3, 3]) - (LEG_REACH[s] - 1e-6) for s in 'LR')
        if excess <= 0: break
        x[2] -= excess + 1e-5; info['pelvis_lowered'] += excess + 1e-5
    for s in 'LR':
        # Reverse (digitigrade) knee: flexes backwards; the accepted Leap/Death crouches also splay it out.
        # knee = (w, splay): w blends from the Idle knee plane (w=0 reproduces Idle exactly).
        w, splay = P.get(s + '_knee', P.get('knee', (0.0, 55.0)))
        crouch = -F * np.cos(np.deg2rad(splay)) + OUT[s] * np.sin(np.deg2rad(splay))
        pole = (1 - w) * KNEE0[s] + w * crouch
        info[s + '_foot_err'] = two_bone(x, s + '_leg_upper', s + '_leg_lower', s + '_foot', ANKLE[s], pole)
        assign(x, s + '_foot', FOOT_ROT[s])
        x[sl(s + '_toe')] = IDLE[sl(s + '_toe')]
    # arms
    for s in 'LR':
        T = fk(x)
        if s + '_reach' in P:
            # free arm: wrist along a creature-frame direction from the shoulder at a
            # fraction of the arm length (always reachable, elbow never locks)
            d = cf(P[s + '_reach'][:3]); d /= np.linalg.norm(d)
            L = bone_len(s + '_arm_upper', s + '_arm_lower') + bone_len(s + '_arm_lower', s + '_hand')
            w = T[IX[s + '_arm_upper'], :3, 3] + d * P[s + '_reach'][3] * L
            P = dict(P); P[s + '_wrist'] = (w @ F, w @ SIDE, w @ UP)
            info.setdefault('wrists', {})[s] = P[s + '_wrist']
        tgt = P.get(s + '_wrist')
        if tgt is None:
            continue
        pole = (cf(P[s + '_elbow']) if s + '_elbow' in P else ELBOW0[s]) + OUT[s] * P.get(s + '_elbow_out', 0.0)
        info[s + '_hand_err'] = two_bone(x, s + '_arm_upper', s + '_arm_lower', s + '_hand', cf(tgt), pole)
        assign(x, s + '_hand', rv(P.get(s + '_hand', (0, 0, 0))) @ HAND_ROT[s])
    return (x, info) if return_info else x


def hand_contact_height(x, s, depth=.012):
    """Wrist-height correction that puts the lowest claw tip `depth` below z=0."""
    p = skin(x, TIP_V[s])
    return -depth - p[:, 2].min()


def lowest_non_tip(x, s):
    g = np.setdiff1d(HAND_V[s], TIP_V[s])
    return float(skin(x, g)[:, 2].min())


def tip_offset(s, hand_rot_world):
    """Rigid offset wrist -> lowest claw vertex for a hand world rotation (hand-space constant)."""
    x = IDLE.copy(); assign(x, s + '_hand', hand_rot_world)
    T = fk(x); p = skin(x, HAND_V[s]) - T[IX[s + '_hand'], :3, 3]
    return p[p[:, 2].argmin()]


def build_key(P, return_info=False):
    """build() plus claw-ground contact. '<side>_tip': (f, s) puts that hand's lowest claw
    vertex on this ground point, `<side>_contact` metres under z=0 (default 1.5 cm);
    the wrist target follows rigidly from the hand rotation."""
    P = {k: (dict(v) if isinstance(v, dict) else v) for k, v in P.items()}
    for s in 'LR':
        if s + '_tip' in P:
            tip = P[s + '_tip']
            off = tip_offset(s, rv(P.get(s + '_hand', (0, 0, 0))) @ HAND_ROT[s])
            w = cf((tip[0], tip[1], -P.get(s + '_contact', .015))) - off
            P[s + '_wrist'] = (w @ F, w @ SIDE, w @ UP)
    x, info = build(P, True)
    if 'com_u' in P:
        # centre-of-mass height target: slide the pelvis vertically (secant iterations)
        from howl_common import com_bones
        pel = dict(P.get('pelvis', {})); off = list(pel.get('off', (0, 0, 0)))
        def err(u):
            pel['off'] = (off[0], off[1], u); P['pelvis'] = dict(pel)
            return com_bones(build(P)) @ UP - P['com_u']
        u0, u1 = off[2], off[2] - .05
        e0, e1 = err(u0), err(u1)
        for _ in range(12):
            if abs(e1) < 1e-5 or e1 == e0: break
            u0, u1, e0 = u1, u1 - e1 * (u1 - u0) / (e1 - e0), e1
            e1 = err(u1)
        pel['off'] = (off[0], off[1], u1); P['pelvis'] = pel
        x, info = build(P, True)
        info['pelvis_u'] = u1
    info['resolved'] = {s: tuple(np.round(P[s + '_wrist'], 3)) if s + '_wrist' in P else None for s in 'LR'}
    info['P'] = P
    return (x, info) if return_info else x
