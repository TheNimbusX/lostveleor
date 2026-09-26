"""Merge the numpy and Blender checks into validation.json, apply the delivery limits."""
import json, hashlib
from pathlib import Path
HERE = Path(__file__).resolve().parent
n = json.loads((HERE / 'validation_numpy.json').read_text())
b = json.loads((HERE / 'blender_checks.json').read_text())
fx = json.loads((HERE / 'fbx_howl_check.json').read_text()) if (HERE / 'fbx_howl_check.json').exists() else None
pk = json.loads((HERE / 'fbx_package_check.json').read_text()) if (HERE / 'fbx_package_check.json').exists() else None
sha = lambda p: hashlib.sha256((HERE / p).read_bytes()).hexdigest()

feet_slip = max(max(v['max_contact_slip_xy_mm'], v['max_contact_slip_z_mm']) for v in n['feet'].values())
claw_slip = max(v['max_tip_slip_during_hold_mm'] for v in n['claws'].values())
limits = {
    'foot_contact_slip_mm_max': (feet_slip, 2.0),
    'claw_tip_slip_during_hold_mm_max': (claw_slip, 2.0),
    'bone_length_error_m_max': (n['max_bone_length_error_m'], 1e-5),
    'root_bone_motion_m_max': (n['root_bone_motion_m'], 1e-9),
    'claw_tip_depth_mm_max': (max(v['max_tip_depth_mm'] for v in n['claws'].values()), 25.0),
    'non_claw_vertices_below_ground': (n['non_claw_vertices_below_ground'], 0),
    'claws_first_touch_frame': (max(v['first_ground_touch_frame'] for v in n['claws'].values()), 24.0),
    'new_triangle_intersections_frames': (len(b['frames_with_new_overlaps']), 0),
    'baked_action_vs_solve_m_max': (b['evaluated_bone_vs_solve_max_m'], 1e-4),
    'endpoint_vs_idle_max': (max(n['endpoints_match_idle'].values()), 1e-9),
    'com_fall_accel_g_min_windup (>= -1.1)': (n['com']['vertical_accel_g_windup']['min'], -1.1),
    'com_fall_accel_g_min_recovery (>= -1.1)': (n['com']['vertical_accel_g_after_contact']['min'], -1.1),
}
if fx: limits['exported_fbx_howl_vs_solve_m_max'] = (fx['max_joint_error_m'], 1e-4)
if pk: limits['accepted_takes_changed_in_fbx'] = (sum(not v['unchanged'] for v in pk['takes'].values()), 0)
passed = {}
for k, (v, lim) in limits.items():
    if k == 'claws_first_touch_frame':
        ok = v == lim
    elif k.startswith('com_fall'):
        ok = v >= lim
    else:
        ok = v <= lim
    passed[k] = {'value': v, 'limit': lim, 'pass': bool(ok)}
report = {
    'clip': 'Wendigo_Howl', 'action_in_blend': 'AN_ForestWendigo_Howl_Baked', 'revision': 'r01',
    'fps': 24, 'frame_range': [0, 48], 'contact_frame': 24, 'hold_end_frame': n['hold_end_frame'],
    'game_timing': {'windup_ticks': 30, 'recovery_ticks': 24, 'ticks_per_second': 30,
                    'clip_0_24_seconds': 1.0, 'clip_24_48_seconds': 0.8, 'recovery_playback_rate': 1.25},
    'root_motion': False, 'samples_per_frame': 4,
    'limits': passed, 'all_pass': all(v['pass'] for v in passed.values()),
    'numpy': n, 'blender': b, 'fbx_howl': fx, 'fbx_package': pk,
    'sources_sha256': {p: sha(p) for p in ('Howl_r01.blend', 'Howl_Baked_r01.blend', 'final_samples.json', 'pose_keys.py',
                                            'contact_solutions.json', 'build_howl.py')},
    'notes': [
        'Foot slip is measured on the ground-contact vertices (z < 12 mm in Idle); the upper heel skin of the right foot '
        'deforms up to %.0f mm through its existing ankle blend weights at the deepest knee bend (no ground contact).' % n['feet']['R']['lower_sole_skin_max_deform_mm'],
        'Claw tips dig 18 mm into the ground from frame 24 to %d (intended); no other vertex goes below the Idle foot level.' % n['hold_end_frame'],
        'Centre of mass uses a segment mass model; the design keeps falls at <= ~1 g with feet planted. Upward peaks are pushes/landings.',
        'Support: the centre of mass leaves the feet polygon only in frames 21-23 (the body tips forward onto the striking claws) and by 7 mm at frame 40 as the claws lift.',
    ],
}
(HERE / 'validation.json').write_text(json.dumps(report, indent=1))
print(json.dumps(passed, indent=1)); print('ALL PASS', report['all_pass'])
