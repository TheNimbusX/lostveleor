"""Howl key poses (clip frames at 24 fps; contact = frame 24; end = frame 48).

Reference stations (howl.mp4 frame -> clip frame) are listed per key. Readable
controls only; howl_pose.build() turns them into rig rotations with planted feet.
Wrist targets are (forward, right, up) metres in the creature frame; the
claw-contact keys (24, 29, 32, 36, 38) are solved by solve_contact_keys.py and merged below.

Timing is set by the game contract and by gravity (feet stay planted, so the body
can only fall at 1 g and must brake its rise at no more than 1 g):
  0-6   gather: hips sit back, chest folds, claws flare out and back, clear of the legs
  6-11  explosive rise to full height, arms fling wide
  11-16 howl: chest arched, skull and antlers thrown back (moving hold)
  16-24 slam: knees give and the body drops (0.7 g), arms lift then drive both claws down
  24    CONTACT - claws hit the ground, ring erupts
  24-28 impact compression into the planted claws, skull/antlers follow through
  28-37 low heavy vulnerable hold, heaving breath, claws stay planted
'com_u' keys pin the centre-of-mass height (segment mass model) - the pelvis slides to it.
  37-48 push off the claws and rise back into the stalking idle
"""

CONTACT_FRAME = 24
END_FRAME = 48
HOLD_END = 38

# ref frame of howl.mp4 matched to each key (reference breakdown)
REF_OF_KEY = {0: 0, 3: 6, 6: 12, 8: 16, 11: 20, 14: 28, 16: 36, 19: 39, 21: 41, 22: 42,
              24: 43, 29: 50, 32: 58, 36: 70, 38: 75, 41: 80, 44: 86, 48: 96}

KEYS = {
    0: {},
    # gather: weight sinks, hips back over the heels, claws start to sweep back
    3: {'com_u': 1.33, 'pelvis': {'off': (-.03, 0, -.07), 'rot': (12, 0, 0)}, 'spine_01': (6, 0, 0), 'spine_02': (5, 0, 0),
        'neck': (4, 0, 0), 'head': (6, 0, 0), 'knee': (.7, 55), 'L_clav': (-4, 2), 'R_clav': (-4, 2),
        'L_reach': (0, -.75, -1, .95), 'R_reach': (0, .75, -1, .95),
        'L_hand': (0, -14, 0), 'R_hand': (0, -14, 0), 'L_elbow': (-1, -.25, 0), 'R_elbow': (-1, .25, 0)},
    # bottom of the gather: chest folded over the knees, claws swept out and back past the thighs
    6: {'com_u': 1.26, 'pelvis': {'off': (-.06, 0, -.15), 'rot': (24, 0, 0)}, 'spine_01': (10, 0, 0), 'spine_02': (8, 0, 0),
        'neck': (8, 0, 0), 'head': (10, 0, 0), 'knee': (1, 58), 'L_clav': (-8, 0), 'R_clav': (-8, 0),
        'L_reach': (-.3, -1.2, -1, .92), 'R_reach': (-.3, 1.2, -1, .92),
        'L_hand': (6, -34, 0), 'R_hand': (-6, -34, 0), 'L_elbow': (-1, -.35, .25), 'R_elbow': (-1, .35, .25)},
    # explosive rise: legs drive, chest opens, arms fling out and up
    8: {'com_u': 1.36, 'pelvis': {'off': (-.01, 0, -.08), 'rot': (8, 0, 0)}, 'spine_01': (0, 0, 0), 'spine_02': (-4, 0, 0),
        'neck': (-6, 0, 0), 'head': (-8, 0, 0), 'knee': (.8, 52), 'L_clav': (4, -2), 'R_clav': (4, -2),
        'L_reach': (.15, -1, -.5, .93), 'R_reach': (.15, 1, -.5, .93),
        'L_hand': (55, -10, 0), 'R_hand': (-55, -10, 0), 'L_elbow': (-1, -.1, -.35), 'R_elbow': (-1, .1, -.35)},
    # full height: hips forward under the arched chest, arms wide, skull tipping back
    11: {'pelvis': {'off': (.08, 0, 0), 'rot': (-6, 0, 0)}, 'spine_01': (-8, 0, 0), 'spine_02': (-10, 0, 0),
         'neck': (-18, 0, 0), 'head': (-30, 0, 0), 'L_clav': (8, -4), 'R_clav': (8, -4),
         'L_wrist': (-.02, -1.30, 1.62), 'R_wrist': (-.02, 1.30, 1.62),
         'L_hand': (85, 0, -10), 'R_hand': (-85, 0, 10), 'L_elbow': (-1, 0, -.8), 'R_elbow': (-1, 0, -.8)},
    # howl peak: chest arched, skull and antlers thrown back, claws splayed
    14: {'pelvis': {'off': (.11, 0, 0), 'rot': (-9, 0, 0)}, 'spine_01': (-10, 0, 0), 'spine_02': (-14, 0, 0),
         'neck': (-26, 0, 0), 'head': (-42, 0, 0), 'L_clav': (12, -6), 'R_clav': (12, -6),
         'L_wrist': (-.06, -1.32, 1.70), 'R_wrist': (-.06, 1.30, 1.70),
         'L_hand': (95, 0, -15), 'R_hand': (-95, 0, 15), 'L_elbow': (-1, 0, -.8), 'R_elbow': (-1, 0, -.8)},
    # end of the howl (moving hold): arms still rising, skull at its furthest back
    16: {'com_u': 1.41, 'knee': (.5, 58), 'pelvis': {'off': (.11, 0, 0), 'rot': (-9, 0, 0)}, 'spine_01': (-11, 0, 0), 'spine_02': (-15, 0, 0),
         'neck': (-25, 0, 0), 'head': (-45, 0, 0), 'L_clav': (14, -6), 'R_clav': (14, -6),
         'L_wrist': (-.04, -1.30, 1.80), 'R_wrist': (-.04, 1.28, 1.80),
         'L_hand': (100, 0, -15), 'R_hand': (-100, 0, 15), 'L_elbow': (-1, 0, -.8), 'R_elbow': (-1, 0, -.8)},
    # slam: knees give (body starts to fall), arms lift for the strike, skull comes forward
    19: {'com_u': 1.33, 'pelvis': {'off': (.10, 0, -.05), 'rot': (2, 0, 0)}, 'spine_01': (-6, 0, 0), 'spine_02': (-8, 0, 0),
         'neck': (-10, 0, 0), 'head': (-26, 0, 0), 'knee': (.6, 58), 'L_clav': (16, 4), 'R_clav': (16, 4),
         'L_wrist': (.32, -1.16, 1.98), 'R_wrist': (.32, 1.14, 1.98),
         'L_hand': (80, 30, 0), 'R_hand': (-80, 30, 0), 'L_elbow': (-1, -.2, -.6), 'R_elbow': (-1, .2, -.6)},
    21: {'com_u': 1.21, 'pelvis': {'off': (.08, 0, -.16), 'rot': (18, 0, 0)}, 'spine_01': (4, 0, 0), 'spine_02': (-4, 0, 0),
         'neck': (6, 0, 0), 'head': (-22, 0, 0), 'knee': (1, 60), 'L_clav': (6, 8), 'R_clav': (6, 8),
         'L_wrist': (.56, -1.20, 1.74), 'R_wrist': (.54, 1.18, 1.74),
         'L_hand': (55, 32, 0), 'R_hand': (-55, 32, 0), 'L_elbow': (-1, -.4, -.2), 'R_elbow': (-1, .4, -.2)},
    22: {'com_u': 1.13, 'pelvis': {'off': (.07, 0, -.24), 'rot': (28, 0, 0)}, 'spine_01': (7, 0, 0), 'spine_02': (-8, 0, 0),
         'neck': (16, 0, 0), 'head': (-34, 0, 0), 'knee': (1, 61), 'L_clav': (-2, 8), 'R_clav': (-2, 8),
         'L_wrist': (.74, -1.10, 1.30), 'R_wrist': (.72, 1.08, 1.28),
         'L_hand': (38, 30, 0), 'R_hand': (-40, 26, 0), 'L_elbow': (-.8, -.8, .2), 'R_elbow': (-.8, .8, .2)},
    # 24, 29, 32, 36, 38: solved contact keys (see contact_solutions.json)
    # push off the claws and rise
    41: {'com_u': 1.10, 'pelvis': {'off': (.04, 0, -.24), 'rot': (20, 0, 0)}, 'spine_01': (5, 0, 0), 'spine_02': (-2, 0, 0),
         'neck': (10, 0, 0), 'head': (-14, 0, 0), 'knee': (.9, 58), 'L_clav': (-4, 5), 'R_clav': (-4, 5),
         'L_reach': (.25, -.55, -1, .9), 'R_reach': (.25, .55, -1, .9),
         'L_hand': (6, 12, 0), 'R_hand': (-8, 12, 0), 'L_elbow': (-.8, -.6, .2), 'R_elbow': (-.8, .6, .2)},
    44: {'com_u': 1.30, 'pelvis': {'off': (.01, 0, -.07), 'rot': (6, 0, 0)}, 'spine_01': (2, 0, 0), 'spine_02': (1, 0, 0),
         'neck': (3, 0, 0), 'head': (-2, 0, 0), 'knee': (.5, 55),
         'L_wrist': (.16, -.90, 1.00), 'R_wrist': (-.10, .94, 1.05),
         'L_hand': (0, 4, 0), 'R_hand': (0, 4, 0)},
    48: {},
}

# Claw-contact keys (24, 29, 32, 36, 38) come from solve_contact_keys.py: torso, clavicles and
# hand rotation solved so both claws reach their ground points with bent elbows, the
# body stays clear of the ground and the centre of mass sits between feet and claws.
import json as _json
from pathlib import Path as _Path
_sol = _Path(__file__).resolve().parent / 'contact_solutions.json'
if _sol.exists():
    for _k, _v in _json.loads(_sol.read_text()).items():
        _P = {}
        for _n, _val in _v['P'].items():
            _P[_n] = {kk: tuple(vv) for kk, vv in _val.items()} if isinstance(_val, dict) else (tuple(_val) if isinstance(_val, list) else _val)
        KEYS[int(_k)] = _P
