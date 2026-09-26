"""Howl spline pass: key poses -> quarter-frame samples (final_samples.json).

Every key is completed into one channel vector (pelvis, spine, neck, head, clavicles,
knee planes, wrist targets, elbow poles, hand rotations). Channels are interpolated
with monotone cubic Hermite curves (no overshoot between keys, eased extremes). Rules:

* feet: leg IK onto the Idle plants at every sample (zero slide by construction);
* claws: inside a contact interval the wrist is derived from the claw-tip ground point
  and the (constant) hand rotation, so planted tips cannot slide or float;
* slam: wrist/torso channels arrive at the contact key with speed (no ease-in) and
  stop dead; the skull/antlers lag through a damped spring and nod past the pose after
  the impact (decaying kick), the shoulders sink into the planted arms;
* hold: slow heave of the chest (heavy breathing) while the claws stay planted;
* frame 0 and frame 48 are exactly the Idle@0 pose.
"""
import json
import numpy as np
from howl_common import HERE, fk, skin, F, SIDE, UP, com_bones
from howl_pose import build, build_key, tip_offset, rv, cf, HAND_ROT, IDLE, IDLE_T, IX, ELBOW0, WRIST0, TIP_V, HAND_V
from pose_keys import KEYS, CONTACT_FRAME, END_FRAME, HOLD_END, REF_OF_KEY

SUB = 4                                  # samples per frame (quarter frames, as Leap/Death)
IMPACT_GAIN = 1.0                        # arrive at the contact key with the full incoming secant speed (no ease-in)
BONES3 = ['spine_01', 'spine_02', 'neck', 'head']
CONTACTS = {'plant': (CONTACT_FRAME, HOLD_END)}


def fsu(v):
    return np.array([v @ F, v @ SIDE, v @ UP])


def complete(P):
    """Full channel dict for one key (fills Idle defaults; resolves the centre-of-mass
    height target and claw tips)."""
    if P:
        x, info = build_key(P, True)
        P = dict(info['P'])
        for s, w in info.get('wrists', {}).items():
            P[s + '_wrist'] = w; P.pop(s + '_reach', None)
    c = {}
    pel = P.get('pelvis', {})
    c['pel_off'] = np.array(pel.get('off', (0, 0, 0)), float)
    c['pel_rot'] = np.array(pel.get('rot', (0, 0, 0)), float)
    for b in BONES3:
        c[b] = np.array(P.get(b, (0, 0, 0)), float)
    for s in 'LR':
        c[s + '_clav'] = np.array(P.get(s + '_clav', (0, 0)), float)
        c[s + '_knee'] = np.array(P.get(s + '_knee', P.get('knee', (0.0, 55.0))), float)
        c[s + '_hand'] = np.array(P.get(s + '_hand', (0, 0, 0)), float)
        c[s + '_elbow'] = np.array(P[s + '_elbow'], float) if s + '_elbow' in P else fsu(ELBOW0[s])
        c[s + '_elbow_out'] = float(P.get(s + '_elbow_out', 0.0))
        if s + '_tip' in P:
            c[s + '_tip'] = np.array(P[s + '_tip'], float)
            c[s + '_depth'] = float(P.get(s + '_contact', .015))
            off = tip_offset(s, rv(c[s + '_hand']) @ HAND_ROT[s])
            c[s + '_wrist'] = fsu(cf((*c[s + '_tip'], -c[s + '_depth'])) - off)
        elif s + '_wrist' in P:
            c[s + '_wrist'] = np.array(P[s + '_wrist'], float)
        else:
            # FK arm of this key (Idle arm carried by the torso)
            x = build({k: v for k, v in P.items() if not k.startswith(s + '_') or k == s + '_clav'})
            c[s + '_wrist'] = fsu(fk(x)[IX[s + '_hand'], :3, 3])
            c[s + '_hand'] = np.zeros(3)   # keeps the Idle hand world rotation
    return c


def to_P(c, tips=None):
    P = {'pelvis': {'off': tuple(c['pel_off']), 'rot': tuple(c['pel_rot'])}}
    for b in BONES3:
        P[b] = tuple(c[b])
    for s in 'LR':
        P[s + '_clav'] = tuple(c[s + '_clav']); P[s + '_knee'] = tuple(c[s + '_knee'])
        P[s + '_hand'] = tuple(c[s + '_hand']); P[s + '_elbow'] = tuple(c[s + '_elbow'])
        P[s + '_elbow_out'] = c[s + '_elbow_out']; P[s + '_wrist'] = tuple(c[s + '_wrist'])
    return P


def flatten(c, keys):
    return np.concatenate([np.atleast_1d(c[k]).astype(float) for k in keys])


def unflatten(v, template, keys):
    out, i = {}, 0
    for k in keys:
        n = np.atleast_1d(template[k]).size
        out[k] = v[i:i + n] if n > 1 else float(v[i])
        i += n
    return out


def hermite_tangents(t, y, impact=None):
    """Fritsch-Carlson monotone tangents per channel; zero at both ends.
    impact: {key index: True} -> arrive with IMPACT_GAIN x the incoming secant (no ease-in:
    the claws strike at speed), leave with the ordinary monotone tangent (a planted channel
    that stays constant after the strike leaves with zero slope, so nothing drifts).
    Returns (left, right) tangents."""
    n = len(t); d = np.diff(y, axis=0) / np.diff(t)[:, None]
    m = np.zeros_like(y)
    for i in range(1, n - 1):
        a, b = d[i - 1], d[i]
        same = (a * b) > 0
        w1 = 2 * (t[i + 1] - t[i]) + (t[i] - t[i - 1]); w2 = (t[i + 1] - t[i]) + 2 * (t[i] - t[i - 1])
        with np.errstate(divide='ignore', invalid='ignore'):
            m[i] = np.where(same, (w1 + w2) / (w1 / np.where(a == 0, 1, a) + w2 / np.where(b == 0, 1, b)), 0)
    ml, mr = m.copy(), m.copy()
    for i in (impact or {}):
        ml[i] = IMPACT_GAIN * d[i - 1]
        mr[i] = np.where(d[i] == 0, 0, m[i])
    return ml, mr


def hermite(t, y, mlr, tq):
    ml, mr = mlr
    out = np.zeros((len(tq), y.shape[1]))
    for j, q in enumerate(tq):
        i = min(np.searchsorted(t, q, side='right') - 1, len(t) - 2)
        h = t[i + 1] - t[i]; s = (q - t[i]) / h
        h00 = 2 * s ** 3 - 3 * s ** 2 + 1; h10 = s ** 3 - 2 * s ** 2 + s; h01 = -2 * s ** 3 + 3 * s ** 2; h11 = s ** 3 - s ** 2
        out[j] = h00 * y[i] + h10 * h * mr[i] + h01 * y[i + 1] + h11 * h * ml[i + 1]
    return out


def spring(signal, dt, freq=3.2, zeta=.42):
    """Damped follower: lag and overshoot of a channel (secondary motion)."""
    w = 2 * np.pi * freq
    y = signal[0].copy(); v = np.zeros_like(y); out = [y.copy()]
    for s in signal[1:]:
        for _ in range(4):
            a = w * w * (s - y) - 2 * zeta * w * v
            v = v + a * dt / 4; y = y + v * dt / 4
        out.append(y.copy())
    return np.array(out)


def game_time(f):
    """Sim drives the clip by phase: start->impact = 1.0 s (1x), impact->end = 0.8 s (1.25x)."""
    return f / 24 if f <= CONTACT_FRAME else 1.0 + (f - CONTACT_FRAME) / 30


# Centre-of-mass height (segment mass model) as a physically possible curve, in game time.
# Knots: (frame, height m, vertical speed m/s). With the feet planted the body may fall at
# no more than ~1 g and must brake a rise at no more than ~1 g; pushes/landings may exceed it.
#   0-6   gather: -0.095 m, min-jerk-like cubic (peak 0.93 g down)
#   6-12  rise: 1.1 g push, braked at 0.95 g to the howl height
#   12-15 howl moving hold
#   15-21 knees give: fall at 0.93 g to 2.3 m/s
#   21-24 legs brake the fall to 0.9 m/s as the claws strike (contact at frame 24)
#   24-29 compression on four limbs (peak 1.1 g up), 29-36 heaving hold
#   36-40 push off claws and legs (1.9 g), 40-48 rise braked at 0.95 g into the Idle height
COM_KNOTS = [(0, 1.3711, 0), (6, 1.276, 0), (8.8, 1.3445, 1.20), (12, 1.414, 0), (15, 1.417, 0),
             (21, 1.130, -2.30), (24, None, -0.9), (29, .84, 0), (32.5, .872, 0), (36, .862, 0),
             (39.96, 1.035, 2.5), (48, 1.3711, 0)]


class _Curve:
    def __init__(self, knots):
        self.t = np.array([game_time(k[0]) for k in knots]); self.h = np.array([k[1] for k in knots], float); self.v = np.array([k[2] for k in knots], float)

    def __call__(self, t):
        i = min(np.searchsorted(self.t, t, side='right') - 1, len(self.t) - 2)
        T = self.t[i + 1] - self.t[i]; s = (t - self.t[i]) / T
        h00 = 2 * s ** 3 - 3 * s ** 2 + 1; h10 = s ** 3 - 2 * s ** 2 + s; h01 = -2 * s ** 3 + 3 * s ** 2; h11 = s ** 3 - s ** 2
        return h00 * self.h[i] + h10 * T * self.v[i] + h01 * self.h[i + 1] + h11 * T * self.v[i + 1]


COM_CURVE = None


def com_retarget(P, target):
    """Slide the pelvis vertically so the centre of mass sits on the designed curve
    (bracketed Brent solve; above the straight-leg limit the highest reachable pose is used)."""
    from scipy.optimize import brentq
    pel = dict(P['pelvis']); off = list(pel['off'])
    def run(u):
        Q = dict(P); Q['pelvis'] = {'off': (off[0], off[1], u), 'rot': pel['rot']}
        x, info = build(Q, True)
        return x, info, com_bones(x) @ UP - target
    lo, hi = off[2] - .6, off[2] + .25
    xl, il, el = run(lo); xh, ih, eh = run(hi)
    if eh <= 0:
        ih['pelvis_u'] = hi - ih['pelvis_lowered']; ih['com_err'] = eh
        return xh, ih
    u = brentq(lambda v: run(v)[2], lo, hi, xtol=1e-6)
    x, info, e = run(u)
    info['pelvis_u'] = u; info['com_err'] = e
    return x, info


def sample():
    global COM_CURVE
    frames = sorted(KEYS)
    comp = {f: complete(KEYS[f]) for f in frames}
    # the contact height is the solved contact key's own centre of mass
    knots = [(f, com_bones(build(to_P(comp[CONTACT_FRAME]))) @ UP if h is None else h, v) for f, h, v in COM_KNOTS]
    COM_CURVE = _Curve(knots)
    names_ch = [k for k in comp[frames[0]] if not k.endswith('_tip') and not k.endswith('_depth')]
    Y = np.array([flatten(comp[f], names_ch) for f in frames])
    t = np.array(frames, float)
    ci = frames.index(CONTACT_FRAME)
    M = hermite_tangents(t, Y, impact={ci: True})
    tq = np.arange(0, END_FRAME * SUB + 1) / SUB
    S = hermite(t, Y, M, tq)
    ch = [unflatten(row, comp[frames[0]], names_ch) for row in S]

    # skull/antler follow-through: neck and head pitch follow a damped spring of the
    # keyed curve (driven by the chest's own motion). Faded out before the last frames.
    dt = 1 / (24 * SUB)
    for b, gain in (('neck', .55), ('head', 1.0)):
        raw = np.array([c[b] for c in ch])
        chest = np.array([c['spine_02'][0] + c['pel_rot'][0] for c in ch])
        driven = raw.copy(); driven[:, 0] = raw[:, 0] - gain * (chest - spring(chest[:, None], dt)[:, 0])   # inertia: lags the chest
        lagged = spring(driven, dt, freq=3.6, zeta=.5)
        fade = np.clip((END_FRAME - 2 - tq) / 4, 0, 1)[:, None] * np.clip(tq / 2, 0, 1)[:, None]
        for i, c in enumerate(ch):
            c[b] = raw[i] + (lagged[i] - raw[i]) * fade[i]

    # impact follow-through: the skull and antlers keep travelling when the claws stop
    # the body - a decaying nod past the pose (head 11 deg, neck 5 deg), shoulders sink
    for i, q in enumerate(tq):
        if q > CONTACT_FRAME:
            u = (q - CONTACT_FRAME) / 24.0                  # seconds of clip time
            w = 2 * np.pi * 2.6
            kick = np.exp(-4.2 * u) * np.sin(w * u)
            ch[i]['head'] = ch[i]['head'] + np.array([11.0 * kick, 0, 0])
            ch[i]['neck'] = ch[i]['neck'] + np.array([5.0 * np.exp(-4.2 * u) * np.sin(w * u - .35), 0, 0])
            for s in 'LR':
                ch[i][s + '_clav'] = ch[i][s + '_clav'] + np.array([-3.0 * kick, 0])

    # heavy breathing in the low hold: the chest heaves (the matching centre-of-mass
    # bob is in COM_KNOTS), claws stay planted
    for i, q in enumerate(tq):
        if 27 <= q <= 38:
            env = np.sin(np.pi * (q - 27) / 11) ** 2
            ph = 2 * np.pi * (q - 28) / 8.0
            ch[i]['spine_02'] = ch[i]['spine_02'] + np.array([-2.0 * np.sin(ph), 0, 0]) * env
            ch[i]['neck'] = ch[i]['neck'] + np.array([1.2 * np.sin(ph - .7), 0, 0]) * env

    xs, meta = [], []
    for i, q in enumerate(tq):
        c = ch[i]
        # exact claw contacts: wrist from the tip ground point and the hand rotation
        for s in 'LR':
            for kind, (a, b) in CONTACTS.items():
                if a <= q <= b:
                    ka, kb = comp[a], comp[b]
                    u = 0 if b == a else (q - a) / (b - a)
                    u = u * u * (3 - 2 * u)
                    tip = ka[s + '_tip'] * (1 - u) + kb[s + '_tip'] * u
                    depth = ka[s + '_depth'] * (1 - u) + kb[s + '_depth'] * u
                    off = tip_offset(s, rv(c[s + '_hand']) @ HAND_ROT[s])
                    c[s + '_wrist'] = fsu(cf((*tip, -depth)) - off)
        P = to_P(c)
        x, info = com_retarget(P, float(COM_CURVE(game_time(q))))
        if q == 0 or q == END_FRAME:
            x = IDLE.copy()
        xs.append(x.tolist())
        meta.append({'frame': q, 'pelvis_lowered': info['pelvis_lowered'], 'com_err': float(info.get('com_err', 0)), 'pelvis_u': float(info.get('pelvis_u', 0)),
                     'hand_err': {s: info.get(s + '_hand_err', 0) for s in 'LR'},
                     'foot_err': {s: info.get(s + '_foot_err', 0) for s in 'LR'}})
    return tq, xs, meta


if __name__ == '__main__':
    tq, xs, meta = sample()
    (HERE / 'final_samples.json').write_text(json.dumps(xs))
    (HERE / 'sample_meta.json').write_text(json.dumps(meta))
    (HERE / 'key_frames.json').write_text(json.dumps(sorted(KEYS)))
    worst = max(max(m['hand_err'].values()) for m in meta)
    print('samples', len(xs), 'worst hand IK miss', round(worst, 5), 'max pelvis auto-lower', round(max(m['pelvis_lowered'] for m in meta), 4))
