"""Contact-pose solve: fit torso/clavicle/hand controls so both claws reach their ground
points with bent elbows, the body stays off the ground and the design targets
(pelvis height, chest pitch, face direction) are met as closely as possible.

Used for the claw-drag (4, 6) and the slam/hold keys (24..38). Prints the solved
controls; pose_keys.py stores the result (the solve is a blocking aid, the keys
remain readable and editable by hand).
"""
import json, sys
import numpy as np
from scipy.optimize import least_squares
from howl_pose import build_key, IX, TIP_V, ANKLE, LEG_REACH, rv, HAND_ROT, cf
from howl_common import fk, skin, bone_len, names, dominant, F, SIDE, UP, com_bones

REACH = {s: bone_len(s + '_arm_upper', s + '_arm_lower') + bone_len(s + '_arm_lower', s + '_hand') for s in 'LR'}
FEET = np.isin(dominant, [IX[n] for n in ('L_foot', 'R_foot', 'L_toe', 'R_toe')])
CLAW = np.zeros(len(dominant), bool); CLAW[TIP_V['L']] = True; CLAW[TIP_V['R']] = True
BODY = np.where(~(FEET | CLAW))[0]
VARS = ['pel_f', 'pel_u', 'pel_p', 's1', 's2', 'neck', 'head', 'Lce', 'Lcp', 'Rce', 'Rcp', 'Lhp', 'Lhr', 'Rhp', 'Rhr']


def to_P(v, base):
    d = dict(zip(VARS, v))
    P = dict(base)
    P['pelvis'] = {'off': (d['pel_f'], 0, d['pel_u']), 'rot': (d['pel_p'], 0, 0)}
    P['spine_01'] = (d['s1'], 0, 0); P['spine_02'] = (d['s2'], 0, 0)
    P['neck'] = (d['neck'], 0, 0); P['head'] = (d['head'], 0, 0)
    P['L_clav'] = (d['Lce'], d['Lcp']); P['R_clav'] = (d['Rce'], d['Rcp'])
    P['L_hand'] = (d['Lhr'], d['Lhp'], 0); P['R_hand'] = (-d['Rhr'], d['Rhp'], 0)
    return P


def _fixed(v, tg):
    v = np.array(v, float)
    for n, val in tg.get('fix', {}).items():
        v[VARS.index(n)] = val
    return v


def residuals(v, base, tg):
    v = _fixed(v, tg)
    P = to_P(v, base)
    x, info = build_key(P, True)
    T = fk(x)
    r = []
    for s in 'LR':
        r.append(info.get(s + '_hand_err', 0) * 60)
        ext = np.linalg.norm(T[IX[s + '_hand'], :3, 3] - T[IX[s + '_arm_upper'], :3, 3]) / REACH[s]
        r.append(max(0, ext - tg['arm_ext']) * 40)
    z = skin(x, BODY[::3])[:, 2]
    r.append(max(0, tg['clear'] - z.min()) * 30)
    chest = T[IX['spine_02'], :3, 1]       # bone Y axis = bone direction
    r.append((np.degrees(np.arctan2(chest @ F, chest @ UP)) - tg['chest']) / 6)
    head = T[IX['head'], :3, 1]
    r.append((np.degrees(np.arctan2(head @ UP, head @ F)) - tg['face']) / 8)
    r.append((T[IX['head'], :3, 3] @ UP - tg['head_u']) / .08)
    r.append((v[1] - tg['pel_u']) / .06)
    if 'com_f' in tg:
        r.append((com_bones(x) @ F - tg['com_f']) / .03)
    if 'com_u' in tg:
        r.append((com_bones(x) @ UP - tg['com_u']) / .02)
    prior = np.array([.05, 1, 10, 8, 8, 10, 12, 8, 8, 8, 8, 20, 20, 20, 20])
    r.extend(((v - tg['v0']) / prior) * .3)
    return np.array(r)


def solve(base, tg, v0):
    tg = dict(tg); tg['v0'] = np.array(v0, float)
    lo = [-.2, -.6, 0, -5, -10, -30, -60, -20, -10, -20, -10, -20, -30, -20, -30]
    hi = [.15, 0, 45, 30, 30, 30, 30, 15, 25, 15, 25, 70, 30, 70, 30]
    res = least_squares(residuals, np.clip(v0, lo, hi), bounds=(lo, hi), args=(base, tg), diff_step=1e-3, max_nfev=200)
    return _fixed(res.x, tg), residuals(res.x, base, tg)


def report(P):
    x, info = build_key(P, True); T = fk(x)
    ext = {s: round(float(np.linalg.norm(T[IX[s + '_hand'], :3, 3] - T[IX[s + '_arm_upper'], :3, 3]) / REACH[s]), 3) for s in 'LR'}
    z = skin(x)
    chest = T[IX['spine_02'], :3, 1]; head = T[IX['head'], :3, 1]
    return {'arm_ext': ext, 'hand_err': {s: round(float(info.get(s + '_hand_err', 0)), 4) for s in 'LR'},
            'body_min_z': round(float(z[BODY, 2].min()), 3), 'claw_min_z': round(float(z[CLAW, 2].min()), 4),
            'chest_pitch': round(float(np.degrees(np.arctan2(chest @ F, chest @ UP))), 1),
            'face_elev': round(float(np.degrees(np.arctan2(head @ UP, head @ F))), 1),
            'head_u': round(float(T[IX['head'], :3, 3] @ UP), 3), 'pelvis_z': round(float(T[1, 2, 3]), 3),
            'com_f': round(float(com_bones(x) @ F), 3), 'com_u': round(float(com_bones(x) @ UP), 3)}


def fmt(P):
    out = {}
    for k, v in P.items():
        if isinstance(v, dict):
            out[k] = {kk: tuple(round(float(a), 3) for a in vv) for kk, vv in v.items()}
        elif isinstance(v, tuple):
            out[k] = tuple(round(float(a), 3) if a is not None else None for a in v)
        else:
            out[k] = v
    return out


if __name__ == '__main__':
    spec = json.loads(sys.argv[1])
    base = {k: (tuple(v) if isinstance(v, list) else v) for k, v in spec['base'].items()}
    v, r = solve(base, spec['targets'], spec['v0'])
    P = to_P(v, base)
    print(json.dumps({'P': fmt(P), 'report': report(P), 'resid': float(np.sum(r ** 2))}))
