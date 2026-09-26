"""Quick numeric probe for a key-pose candidate (reach, ground, contact)."""
import sys, json
import numpy as np
from howl_pose import *
from howl_common import *
def fsu(p): return tuple(float(v) for v in np.round([p@F,p@SIDE,p@UP],2))
def probe(P, label=''):
    x, info = build_key(P, True); T = fk(x); xyz = skin(x)
    feet = np.isin(dominant, [IX[n] for n in ('L_foot','R_foot','L_toe','R_toe')])
    claws = np.zeros(len(xyz), bool); claws[TIP_V['L']] = True; claws[TIP_V['R']] = True
    body = ~(feet | claws)
    low = np.argsort(xyz[body, 2])[:1]
    reach = {s: np.linalg.norm(T[IX[s+'_hand'],:3,3]-T[IX[s+'_arm_upper'],:3,3])/(bone_len(s+'_arm_upper',s+'_arm_lower')+bone_len(s+'_arm_lower',s+'_hand')) for s in 'LR'}
    legs = {s: np.linalg.norm(ANKLE[s]-T[IX[s+'_leg_upper'],:3,3])/LEG_REACH[s] for s in 'LR'}
    bi = np.where(body)[0][low[0]]
    print(label, 'pelvis_z %.2f'%T[1,2,3], 'head', fsu(T[IX['head'],:3,3]), 'Lsh', fsu(T[IX['L_arm_upper'],:3,3]), 'Rsh', fsu(T[IX['R_arm_upper'],:3,3]),
          'arm%% L%.2f R%.2f'%(reach['L'],reach['R']), 'leg%% L%.2f R%.2f'%(legs['L'],legs['R']),
          'handerr L%.3f R%.3f'%(info.get('L_hand_err',0),info.get('R_hand_err',0)),
          'body minz %.3f (%s)'%(xyz[bi,2], names[dominant[bi]]), 'claw minz %.3f'%xyz[claws,2].min())
    return x
if __name__ == '__main__':
    from pose_keys import KEYS
    for k in sys.argv[1].split(','):
        probe(KEYS[int(k)], k)
