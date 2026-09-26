"""Howl reference camera: fitted to the accepted howl.mp4 frame 0 with the Idle start pose.

Landmark vertices are the same surface points used for Claw/Leap (visible_landmarks.json);
frame-0 pixel positions were read off breakdown/grid_000.png (960 x 960).
"""
import json
import numpy as np
from scipy.optimize import least_squares
from howl_common import HERE, skin, project

marks = json.loads((HERE.parent / 'reference_match_claw' / 'visible_landmarks.json').read_text())
keys = list(marks)
ids = [marks[k] for k in keys]
idle = np.array(json.loads((HERE / 'stance_samples.json').read_text())['poses']['Wendigo_Idle@0'])

base = {
    'nose': (560, 383), 'brow': (574, 262), 'horn_R': (505, 40), 'horn_L': (610, 40),
    'R_shoulder': (389, 280), 'R_elbow': (303, 420), 'R_wrist': (233, 558), 'R_tip': (248, 757), 'R_inner': (304, 708),
    'L_elbow': (655, 420), 'L_wrist': (725, 571), 'L_tip': (683, 746), 'waist': (492, 430),
    'R_knee': (446, 608), 'R_ankle': (346, 792), 'R_toe': (320, 905),
    'L_knee': (575, 575), 'L_ankle': (621, 758), 'L_toe': (683, 817)}

if __name__ == '__main__':
    pts = skin(idle, ids)
    use = [k for k in base]
    ix = [keys.index(k) for k in use]
    xy = np.array([base[k] for k in use], float)
    claw = json.loads((HERE.parent / 'reference_match_claw' / 'landmarks.json').read_text())['camera']
    fit = least_squares(lambda c: (project(pts[ix], c) - xy).ravel(), np.array(claw),
                        bounds=([.4, 0, 2.6, 250, 250], [1.8, .7, 4.4, 700, 700]))
    res = (project(pts[ix], fit.x) - xy)
    per = {k: round(float(np.linalg.norm(r)), 1) for k, r in zip(use, res)}
    rms = float(np.sqrt(np.mean(np.sum(res ** 2, 1))))
    (HERE / 'landmarks.json').write_text(json.dumps({'keys': keys, 'vertex_ids': ids, 'camera': fit.x.tolist(),
        'camera_rms_px': rms, 'per_landmark_px': per, 'base_targets': base,
        'note': 'Camera fitted on howl.mp4 frame 0 against the Idle@0 pose (the clip start pose).'}, indent=2))
    print('camera', np.round(fit.x, 4), 'rms', round(rms, 2), per)
