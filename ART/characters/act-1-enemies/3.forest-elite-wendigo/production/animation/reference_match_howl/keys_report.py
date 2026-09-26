"""Print key-pose sanity numbers and write key_poses.json for rendering."""
import json, sys
import numpy as np
from howl_common import HERE, fk, skin, names
from howl_pose import build_key, IX, TIP_V, HAND_V, lowest_non_tip, LEG_REACH, ANKLE
from pose_keys import KEYS
out = {}
for f, P in sorted(KEYS.items()):
    x, info = build_key(P, True)
    T = fk(x); xyz = skin(x)
    reach = {s: np.linalg.norm(T[IX[s + '_hand'], :3, 3] - T[IX[s + '_arm_upper'], :3, 3]) for s in 'LR'}
    legs = {s: np.linalg.norm(ANKLE[s] - T[IX[s + '_leg_upper'], :3, 3]) / LEG_REACH[s] for s in 'LR'}
    tipz = {s: float(skin(x, TIP_V[s])[:, 2].min()) for s in 'LR'}
    print(f'{f:2d} pelvis_z {T[1,2,3]:.3f} lowered {info["pelvis_lowered"]:.3f} minz {xyz[:,2].min():+.3f} ({names[xyz[:,2].argmin() and 0]}) '
          f'armreach L{reach["L"]:.3f} R{reach["R"]:.3f} leg% L{legs["L"]:.3f} R{legs["R"]:.3f} '
          f'handerr L{info.get("L_hand_err",0):.3f} R{info.get("R_hand_err",0):.3f} tips L{tipz["L"]:+.3f} R{tipz["R"]:+.3f} '
          f'nontip L{lowest_non_tip(x,"L"):+.3f} R{lowest_non_tip(x,"R"):+.3f} wrist {info["resolved"]}')
    out[str(f)] = x.tolist()
(HERE / 'key_poses.json').write_text(json.dumps({'frames': out}))
