import sys, json, numpy as np
sys.path.insert(0, '..'); sys.path.insert(0, '.')
from scipy.spatial import cKDTree
import pose_keys
from howl_common import skin, dominant, names
from howl_pose import IX
import build_howl
arms = {s: np.where(np.isin(dominant, [IX[s + '_hand'], IX[s + '_arm_lower']]))[0] for s in 'LR'}
legs = np.where(np.isin(dominant, [IX[n] for n in ('L_leg_upper', 'L_leg_lower', 'R_leg_upper', 'R_leg_lower', 'L_foot', 'R_foot')]))[0]
def test(k3, k6, label):
    pose_keys.KEYS[3].update(k3); pose_keys.KEYS[6].update(k6)
    build_howl.KEYS = pose_keys.KEYS
    tq, xs, meta = build_howl.sample()
    worst = []
    for i in range(0, 45):
        P = skin(np.array(xs[i]))
        tree = cKDTree(P[legs])
        d = min(tree.query(P[arms[s]])[0].min() for s in 'LR')
        worst.append((round(float(d), 3), i / 4))
    print(label, 'min arm-leg gap', min(worst))
opts = [
    ({'L_reach': (-.15, -.45, -1, .95), 'R_reach': (-.15, .45, -1, .95)}, {'L_reach': (-.42, -.72, -1, .92), 'R_reach': (-.42, .72, -1, .92)}, 'current'),
    ({'L_reach': (-.05, -.6, -1, .95), 'R_reach': (-.05, .6, -1, .95)}, {'L_reach': (-.35, -1.0, -1, .92), 'R_reach': (-.35, 1.0, -1, .92)}, 'wider'),
    ({'L_reach': (0, -.75, -1, .95), 'R_reach': (0, .75, -1, .95)}, {'L_reach': (-.3, -1.2, -1, .92), 'R_reach': (-.3, 1.2, -1, .92)}, 'widest'),
]
for a, b, l in opts:
    test(a, b, l)
