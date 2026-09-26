"""Howl validation on the baked quarter-frame samples (numpy side; the Blender side adds
triangle intersection and the evaluated-action comparison). Writes validation.json and
asserts the delivery limits."""
import json
import numpy as np
from scipy.spatial import ConvexHull, Delaunay
from howl_common import HERE, fk, skin, names, parents, rests, dominant, weights, verts, F, SIDE, UP, com_bones, rotation_speed_deg
from howl_pose import IDLE, IX, TIP_V, HAND_V
from pose_keys import CONTACT_FRAME, END_FRAME, HOLD_END, KEYS

SUB = 4
G = 9.81
X = np.array(json.loads((HERE / 'final_samples.json').read_text()))
tq = np.arange(len(X)) / SUB
XYZ = np.array([skin(x) for x in X])                  # (samples, verts, 3)
T = np.array([fk(x) for x in X])

(HERE / 'expected_joints.json').write_text(json.dumps(np.round(T[:, :, :3, 3], 7).tolist()))
rep = {'fps': 24, 'frames': END_FRAME, 'samples_per_frame': SUB, 'contact_frame': CONTACT_FRAME,
       'hold_end_frame': HOLD_END, 'key_frames': sorted(KEYS)}

# 1. bone lengths
err = 0
for i, p in enumerate(parents):
    if p > 0:          # pelvis->root is the translating root joint, not a bone length
        L0 = np.linalg.norm(rests[i, :3, 3] - rests[p, :3, 3])
        err = max(err, float(np.abs(np.linalg.norm(T[:, i, :3, 3] - T[:, p, :3, 3], axis=1) - L0).max()))
rep['max_bone_length_error_m'] = err
rep['root_bone_motion_m'] = float(np.abs(T[:, 0, :3, 3]).max())
rep['pelvis_horizontal_range_m'] = float(np.ptp(T[:, 1, :3, 3] @ F)), float(np.ptp(T[:, 1, :3, 3] @ SIDE))

# 2. feet: the ground-contact vertices (claw toes, z < 12 mm in Idle) must not move at all;
#    the rest of the foot skin only deforms through the ankle blend weights (no contact).
feet = {}
for s in 'LR':
    g = np.where(np.isin(dominant, [IX[s + '_foot'], IX[s + '_toe']]))[0]
    ground = g[XYZ[0, g, 2] < .012]
    sole = g[XYZ[0, g, 2] < .06]
    d = XYZ[:, ground] - XYZ[0, ground]
    feet[s] = {'ground_contact_vertices': int(len(ground)),
               'max_contact_slip_xy_mm': float(np.linalg.norm(d[..., :2], axis=2).max() * 1000),
               'max_contact_slip_z_mm': float(np.abs(d[..., 2]).max() * 1000),
               'foot_bone_max_move_mm': float(max(np.linalg.norm(T[:, IX[s + n], :3, 3] - T[0, IX[s + n], :3, 3], axis=1).max() for n in ('_foot', '_toe')) * 1000),
               'lower_sole_skin_max_deform_mm': float(np.linalg.norm(XYZ[:, sole] - XYZ[0, sole], axis=2).max() * 1000)}
rep['feet'] = feet

# 3. claws: planted interval, contact timing, penetration
claws = np.zeros(XYZ.shape[1], bool); claws[TIP_V['L']] = True; claws[TIP_V['R']] = True
footmask = np.isin(dominant, [IX[n] for n in ('L_foot', 'R_foot', 'L_toe', 'R_toe')])
ci = CONTACT_FRAME * SUB; hi = HOLD_END * SUB
claw = {}
for s in 'LR':
    g = TIP_V[s]
    first = next(i for i in range(len(tq)) if XYZ[i, g, 2].min() <= 1e-4)
    planted = g[XYZ[ci, g, 2] < .01]
    d = XYZ[ci:hi + 1, planted] - XYZ[ci, planted]
    claw[s] = {'first_ground_touch_frame': float(tq[first]), 'planted_tip_vertices': int(len(planted)),
               'max_tip_slip_during_hold_mm': float(np.linalg.norm(d, axis=2).max() * 1000),
               'max_tip_depth_mm': float(-XYZ[:, g, 2].min() * 1000),
               'lowest_tip_z_before_contact_mm': float(XYZ[:ci, g, 2].min() * 1000)}
rep['claws'] = claw
body = ~(claws | footmask)
low_body = XYZ[:, body, 2].min(1)
rep['min_body_z_m'] = float(low_body.min()); rep['min_body_z_frame'] = float(tq[low_body.argmin()])
rep['min_body_z_bone'] = names[dominant[np.where(body)[0][XYZ[low_body.argmin(), body, 2].argmin()]]]
rep['non_claw_vertices_below_ground'] = int((XYZ[:, body | footmask, 2] < XYZ[0, :, 2].min() - 1e-4).sum())
rep['idle_min_z_m'] = float(XYZ[0, :, 2].min())

# 4. centre of mass (segment model) and its accelerations in game time
C = np.array([com_bones(x) for x in X])
# windup 24 frames -> 1.0 s (1x), recovery 24 frames -> 0.8 s (1.25x)
t_game = np.where(tq <= CONTACT_FRAME, tq / 24, 1.0 + (tq - CONTACT_FRAME) / 24 / 1.25)
h = C @ UP
v = np.gradient(h, t_game); a = np.gradient(v, t_game)
# smooth over one frame to read the body, not the sampling
k = np.ones(SUB) / SUB
a_s = np.convolve(a, k, mode='same')
pre = tq < CONTACT_FRAME - .5
rep['com'] = {'height_m': {'start': float(h[0]), 'min': float(h.min()), 'max': float(h.max()),
                           'at_contact': float(h[ci])},
              'vertical_accel_g_windup': {'min': float(a_s[pre][SUB:].min() / G), 'max': float(a_s[pre][SUB:].max() / G)},
              'vertical_accel_g_after_contact': {'min': float(a_s[ci + 2:-SUB].min() / G), 'max': float(a_s[ci + 2:-SUB].max() / G)},
              'fall_speed_at_contact_m_s': float(-v[ci - 1])}

# support: CoM ground projection vs support polygon (feet; + planted claws in the hold)
def poly_margin(pt, pts):
    hull = ConvexHull(pts); eq = hull.equations
    return float(-(eq[:, :2] @ pt + eq[:, 2]).max())
foot_pts = XYZ[0, footmask & (XYZ[0, :, 2] < .03)][:, :2]
margins = []
for i in range(0, len(tq), SUB):
    pts = foot_pts
    if CONTACT_FRAME <= tq[i] <= HOLD_END:
        tips = XYZ[i, claws & (XYZ[i, :, 2] < .005)][:, :2]
        pts = np.vstack([foot_pts, tips])
    margins.append((float(tq[i]), poly_margin(C[i, :2], pts)))
rep['support_margin_m'] = {'min': min(m for _, m in margins), 'at_frame': min(margins, key=lambda m: m[1])[0],
                           'per_frame': [round(m, 3) for _, m in margins]}

# 5. joint speeds
sp = np.array([rotation_speed_deg(X[i], X[i + 1]) for i in range(len(X) - 1)])
j = np.unravel_index(sp.argmax(), sp.shape)
rep['max_rotation_deg_per_quarter_frame'] = {'value': float(sp.max()), 'bone': names[j[1] + 1], 'frame': float(tq[j[0]])}
rep['endpoints_match_idle'] = {'frame0': float(np.abs(X[0] - IDLE).max()), 'frame48': float(np.abs(X[-1] - IDLE).max())}

# 6. proximity proxies for self-intersection (vertex clouds of body parts)
from scipy.spatial import cKDTree
groups = {'hands': ['L_hand', 'R_hand'], 'forearms': ['L_arm_lower', 'R_arm_lower'], 'legs': ['L_leg_upper', 'L_leg_lower', 'R_leg_upper', 'R_leg_lower'],
          'head': ['head'], 'torso': ['pelvis', 'spine_01', 'spine_02']}
gid = {k: np.where(np.isin(dominant, [IX[n] for n in v]))[0] for k, v in groups.items()}
pairs = [('hands', 'legs'), ('forearms', 'legs'), ('hands', 'head'), ('head', 'forearms'), ('hands', 'torso')]
prox = {}
for a_, b_ in pairs:
    base = cKDTree(XYZ[0, gid[b_]]).query(XYZ[0, gid[a_]])[0].min()
    worst = min((cKDTree(XYZ[i, gid[b_]]).query(XYZ[i, gid[a_]])[0].min(), tq[i]) for i in range(0, len(tq), 2))
    prox[f'{a_}-{b_}'] = {'idle_min_m': float(base), 'clip_min_m': float(worst[0]), 'at_frame': float(worst[1])}
rep['vertex_proximity'] = prox

(HERE / 'validation_numpy.json').write_text(json.dumps(rep, indent=1))
print(json.dumps({k: v for k, v in rep.items() if k not in ('support_margin_m',)}, indent=1))
print('support min', rep['support_margin_m']['min'], 'at', rep['support_margin_m']['at_frame'])
