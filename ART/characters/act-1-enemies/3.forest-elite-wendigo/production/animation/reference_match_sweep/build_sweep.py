"""Круг когтей: каналы ключей -> выборки на четверть кадра (final_samples.json) и проверки.

Каждый канал (sweep_keys.TRACKS) — свои ключи и монотонный Эрмит (Фрич-Карлсон,
без перелёта между ключами, касательные 0 на концах). Оборот 'spin' — Эрмит
по заданным скоростям. Поза в выборке: sweep_pose.build(каналы) -> spin(угол).
Кадры 0 и 39 — ровно Idle@0.

Пишет:
  final_samples.json     векторы позы (howl_common: таз + повороты костей)
  expected_joints.json   мировые головы костей (для проверки Blender и FBX)
  claw_paths.json        пути кончиков когтей в осях корня Unity (x вправо, y вверх,
                         z вперёд) за окно оборота — запасной путь лент VFX
  validation_numpy.json  промахи IK, земля, скольжение опорных стоп, концы = Idle
"""
import json
import numpy as np
from sweep_pose import HERE, build, spin, idle_reach, fsu, IDLE, IX, fk, skin, weights, verts, F, SIDE, UP, ELBOW0
from sweep_keys import TRACKS, SPIN, CONTACT_FRAME, END_FRAME, SPIN_START, SPIN_END, IDLE_ARM

SUB = 4


def resolve(channel, value):
    if isinstance(value, str):
        side = channel[0]
        if channel.endswith('_reach'): return idle_reach(side)
        if channel.endswith('_elbow'): return fsu(ELBOW0[side])
        raise ValueError((channel, value))
    return np.atleast_1d(np.asarray(value, float))


def monotone(t, y, q):
    """Фрич-Карлсон по каждому столбцу y; нулевые касательные на концах."""
    t = np.asarray(t, float); y = np.asarray(y, float)
    if len(t) == 1: return np.repeat(y, len(q), 0)
    d = np.diff(y, axis=0) / np.diff(t)[:, None]
    m = np.zeros_like(y)
    for i in range(1, len(t) - 1):
        a, b = d[i - 1], d[i]
        w1 = 2 * (t[i + 1] - t[i]) + (t[i] - t[i - 1]); w2 = (t[i + 1] - t[i]) + 2 * (t[i] - t[i - 1])
        with np.errstate(divide='ignore', invalid='ignore'):
            m[i] = np.where(a * b > 0, (w1 + w2) / (w1 / np.where(a == 0, 1, a) + w2 / np.where(b == 0, 1, b)), 0)
    return hermite(t, y, m, m, q)


def hermite(t, y, ml, mr, q):
    out = np.zeros((len(q), y.shape[1]))
    for j, v in enumerate(q):
        i = min(max(np.searchsorted(t, v, side='right') - 1, 0), len(t) - 2)
        h = t[i + 1] - t[i]; s = np.clip((v - t[i]) / h, 0, 1)
        h00 = 2 * s ** 3 - 3 * s ** 2 + 1; h10 = s ** 3 - 2 * s ** 2 + s; h01 = -2 * s ** 3 + 3 * s ** 2; h11 = s ** 3 - s ** 2
        out[j] = h00 * y[i] + h10 * h * mr[i] + h01 * y[i + 1] + h11 * h * ml[i + 1]
    return out


def spin_curve(q):
    t = np.array([k[0] for k in SPIN], float); y = np.array([[k[1]] for k in SPIN], float)
    m = np.array([[k[2]] for k in SPIN], float)
    return hermite(t, y, m, m, q)[:, 0]


def channels(q):
    out = {}
    for ch, keys in TRACKS.items():
        t = [k[0] for k in keys]
        y = np.array([resolve(ch, k[1]) for k in keys])
        out[ch] = monotone(t, y, q)
    return out


def pose_at(ch, i, yaw):
    P = {'pelvis': {'off': tuple(ch['pel_off'][i]), 'rot': tuple(ch['pel_rot'][i])}}
    for b in ('spine_01', 'spine_02', 'neck', 'head'): P[b] = tuple(ch[b][i])
    for s in 'LR':
        for k in ('clav', 'knee', 'foot', 'foot_rot', 'reach', 'elbow', 'hand'):
            if s + '_' + k in ch: P[s + '_' + k] = tuple(ch[s + '_' + k][i])
    x, info = build(P)
    return spin(x, yaw), info


def claw_tip(side):
    """Кончик когтя: вершина кисти дальше всех от запястья в стойке (как у VFX Claw)."""
    hand = IX[side + '_hand']
    idx = np.where(weights[:, hand] > .8)[0]
    p = skin(IDLE, idx); w = fk(IDLE)[hand, :3, 3]
    return int(idx[np.argmax(np.linalg.norm(p - w, axis=1))])


def unity(p):
    return [float(p @ SIDE), float(p @ UP), float(p @ F)]


def sample():
    q = np.arange(0, END_FRAME * SUB + 1) / SUB
    ch = channels(q); yaw = spin_curve(q)
    xs, meta = [], []
    for i, f in enumerate(q):
        x, info = pose_at(ch, i, yaw[i])
        if f == 0 or f == END_FRAME: x = IDLE.copy()
        xs.append(np.asarray(x)); meta.append({'frame': float(f), 'spin': float(yaw[i]),
                                               **{k: float(v) for k, v in info.items()}})
    return q, xs, meta


def validate(q, xs, meta):
    idle_v = skin(IDLE)
    # Подошва — вершины стопы у земли в стойке Idle (как в проверке воя).
    sole = {s: np.where((weights[:, IX[s + '_foot']] + weights[:, IX[s + '_toe']] > .5) & (idle_v[:, 2] < .012))[0] for s in 'LR'}
    feet_idx = np.unique(np.r_[sole['L'], sole['R'], np.where(weights[:, IX['L_foot']] + weights[:, IX['L_toe']]
                                                             + weights[:, IX['R_foot']] + weights[:, IX['R_toe']] > .3)[0]])
    other = np.setdiff1d(np.arange(len(verts)), feet_idx)
    planted = {'L': [(0, 11.5), (25.5, 39)], 'R': [(0, 11), (28.5, 39)]}
    rest_sole = {s: skin(IDLE, sole[s]) for s in 'LR'}
    slip = {s: 0.0 for s in 'LR'}; lowest_body = 1e9; lowest_feet = 1e9; lowest_frame = None
    for f, x in zip(q, xs):
        v = skin(x)
        if v[other, 2].min() < lowest_body: lowest_body, lowest_frame = float(v[other, 2].min()), float(f)
        lowest_feet = min(lowest_feet, float(v[feet_idx, 2].min()))
        for s in 'LR':
            if any(a <= f <= b for a, b in planted[s]):
                slip[s] = max(slip[s], float(np.abs(v[sole[s]] - rest_sole[s]).max()))
    ends = {str(f): float(np.abs(np.asarray(xs[i]) - IDLE).max()) for i, f in ((0, 0), (len(xs) - 1, END_FRAME))}
    return {
        'max_hand_ik_miss_m': max(max(m['L_hand'], m['R_hand']) for m in meta),
        'max_foot_ik_miss_m': max(max(m['L_foot'], m['R_foot']) for m in meta),
        'max_pelvis_auto_lower_m': max(m['pelvis_lowered'] for m in meta),
        'planted_sole_slip_mm': {s: slip[s] * 1000 for s in 'LR'},
        'lowest_non_foot_vertex_m': lowest_body, 'lowest_non_foot_frame': lowest_frame,
        'lowest_foot_vertex_m': lowest_feet,
        'endpoints_vs_idle': ends,
        'spin_at_contact_deg': float(spin_curve(np.array([CONTACT_FRAME]))[0]),
    }


if __name__ == '__main__':
    q, xs, meta = sample()
    (HERE / 'final_samples.json').write_text(json.dumps([x.tolist() for x in xs]))
    (HERE / 'sample_meta.json').write_text(json.dumps(meta))
    (HERE / 'expected_joints.json').write_text(json.dumps([fk(x)[:, :3, 3].round(7).tolist() for x in xs]))
    tips = {s: claw_tip(s) for s in 'LR'}
    lo, hi = SPIN_START * SUB, SPIN_END * SUB
    paths = {s: [unity(skin(xs[i], [tips[s]])[0]) for i in range(lo, hi + 1)] for s in 'LR'}
    (HERE / 'claw_paths.json').write_text(json.dumps({'frames': [SPIN_START, SPIN_END], 'step': 1 / SUB, 'tip_vertex': tips,
                                                      'axes': 'Unity root local: x right, y up, z forward', 'paths': paths}, indent=1))
    report = validate(q, xs, meta)
    (HERE / 'validation_numpy.json').write_text(json.dumps(report, indent=1))
    print(json.dumps(report, indent=1))
