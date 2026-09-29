"""Круг когтей (Sweep): построение позы из читаемых каналов.

Та же параметризация, что у воя и когтя (reference_match_howl/howl_common):
x = смещение таза (3) + вектор поворота каждой кости после корня (3 на кость).
Отличия от howl_pose.build:
  * стопы не прибиты к стойке Idle: '<L|R>_foot' — смещение лодыжки (f, s, u)
    от места в Idle, '<L|R>_foot_rot' — поворот стопы (f, s, u, градусы);
    опорная стопа на круге уходит под таз, левая нога поднята;
  * руки — '<L|R>_reach' = (f, s, u, доля длины руки) от плеча в текущей позе
    корпуса: направление держится относительно тела и на обороте;
  * 'spin' (градусы, > 0 — поворот влево, против часовой сверху) вращает всю
    позу вокруг вертикали через таз стойки Idle — зверь крутится на месте.
Оси существа: F — вперёд (FacingGuide), S — вправо, U — вверх.
"""
import sys
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / 'reference_match_howl'))
from howl_common import names, fk, skin, assign, world_turn, two_bone, bone_len, sl, rests, F, SIDE, UP, weights, verts  # noqa: E402
from howl_pose import IDLE, IDLE_T, IX, ANKLE, FOOT_ROT, HAND_ROT, OUT, LEG_REACH, KNEE0, ELBOW0, cf, rv, turn  # noqa: E402

ARM_LEN = {s: bone_len(s + '_arm_upper', s + '_arm_lower') + bone_len(s + '_arm_lower', s + '_hand') for s in 'LR'}
SHOULDER0 = {s: IDLE_T[IX[s + '_arm_upper'], :3, 3].copy() for s in 'LR'}
WRIST0 = {s: IDLE_T[IX[s + '_hand'], :3, 3].copy() for s in 'LR'}
# Ось круга: таз стойки Idle над землёй.
PIVOT = (rests[IX['pelvis'], :3, 3] + IDLE[:3]).copy()
PIVOT[2] = 0.0


def fsu(v):
    return np.array([v @ F, v @ SIDE, v @ UP])


def idle_reach(s):
    """Рука стойки Idle в виде (f, s, u, доля): IK по ней повторяет Idle."""
    d = WRIST0[s] - SHOULDER0[s]
    return np.r_[fsu(d / np.linalg.norm(d)), np.linalg.norm(d) / ARM_LEN[s]]


def build(P):
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
    ankle = {s: ANKLE[s] + cf(P.get(s + '_foot', (0, 0, 0))) for s in 'LR'}
    lowered = 0.0
    for _ in range(30):
        T = fk(x)
        excess = max(np.linalg.norm(ankle[s] - T[IX[s + '_leg_upper'], :3, 3]) - (LEG_REACH[s] - 1e-6) for s in 'LR')
        if excess <= 0: break
        x[2] -= excess + 1e-5; lowered += excess + 1e-5
    err = {}
    for s in 'LR':
        w, splay = P.get(s + '_knee', (0.0, 55.0))
        crouch = -F * np.cos(np.deg2rad(splay)) + OUT[s] * np.sin(np.deg2rad(splay))
        pole = (1 - w) * KNEE0[s] + w * crouch
        err[s + '_foot'] = two_bone(x, s + '_leg_upper', s + '_leg_lower', s + '_foot', ankle[s], pole)
        assign(x, s + '_foot', rv(P.get(s + '_foot_rot', (0, 0, 0))) @ FOOT_ROT[s])
        x[sl(s + '_toe')] = IDLE[sl(s + '_toe')]
    for s in 'LR':
        T = fk(x)
        reach = np.asarray(P.get(s + '_reach', idle_reach(s)), float)
        d = cf(reach[:3]); d /= np.linalg.norm(d)
        wrist = T[IX[s + '_arm_upper'], :3, 3] + d * reach[3] * ARM_LEN[s]
        pole = cf(P[s + '_elbow']) if s + '_elbow' in P else ELBOW0[s]
        err[s + '_hand'] = two_bone(x, s + '_arm_upper', s + '_arm_lower', s + '_hand', wrist, pole)
        assign(x, s + '_hand', rv(P.get(s + '_hand', (0, 0, 0))) @ HAND_ROT[s])
    return x, {'pelvis_lowered': lowered, **err}


def spin(x, degrees):
    """Поворот всей позы вокруг вертикали через PIVOT (кости ниже таза — дети таза)."""
    if abs(degrees) < 1e-12:
        return x
    x = np.array(x, float)
    R = Rotation.from_rotvec(UP * np.deg2rad(degrees)).as_matrix()
    T = fk(x)
    assign(x, 'pelvis', R @ T[IX['pelvis'], :3, :3])
    p = rests[IX['pelvis'], :3, 3] + x[:3]
    x[:3] = PIVOT + R @ (p - PIVOT) - rests[IX['pelvis'], :3, 3]
    return x
