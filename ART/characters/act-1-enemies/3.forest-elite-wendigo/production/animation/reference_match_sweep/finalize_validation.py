"""Сводка проверок круга когтей в validation.json с порогами поставки."""
import json, hashlib
from pathlib import Path
HERE = Path(__file__).resolve().parent
n = json.loads((HERE / 'validation_numpy.json').read_text())
b = json.loads((HERE / 'blender_checks.json').read_text())
fx = json.loads((HERE / 'fbx_sweep_check.json').read_text()) if (HERE / 'fbx_sweep_check.json').exists() else None
pk = json.loads((HERE / 'fbx_package_check.json').read_text()) if (HERE / 'fbx_package_check.json').exists() else None
sha = lambda p: hashlib.sha256((HERE / p).read_bytes()).hexdigest()

limits = {
    'planted_sole_slip_mm_max': (max(n['planted_sole_slip_mm'].values()), 2.0),
    'hand_ik_miss_m_max': (n['max_hand_ik_miss_m'], 1e-4),
    'foot_ik_miss_m_max': (n['max_foot_ik_miss_m'], 1e-4),
    'pelvis_auto_lower_m_max': (n['max_pelvis_auto_lower_m'], 1e-6),
    'lowest_non_foot_vertex_m_min (>= 0)': (n['lowest_non_foot_vertex_m'], 0.0),
    'endpoint_vs_idle_max': (max(n['endpoints_vs_idle'].values()), 1e-9),
    'new_triangle_intersections_frames': (len(b['frames_with_new_overlaps']), 0),
    'baked_action_vs_solve_m_max': (b['evaluated_bone_vs_solve_max_m'], 1e-4),
}
if fx: limits['exported_fbx_sweep_vs_solve_m_max'] = (fx['max_joint_error_m'], 1e-4)
if pk: limits['accepted_takes_changed_in_fbx'] = (sum(not v['unchanged'] for v in pk['takes'].values()), 0)
passed = {}
for k, (v, lim) in limits.items():
    ok = v >= lim if k.startswith('lowest_non_foot') else v <= lim
    passed[k] = {'value': v, 'limit': lim, 'pass': bool(ok)}
report = {
    'clip': 'Wendigo_Sweep', 'action_in_blend': 'AN_ForestWendigo_Sweep_Baked', 'revision': 'r01',
    'frame_unit': 'sim tick (1/30 s); package scene stays 24 fps, the view drives the clip by phase',
    'frame_range': [0, 39], 'contact_frame': 21,
    'game_timing': {'windup_ticks': 21, 'recovery_ticks': 18, 'ticks_per_second': 30, 'contact_phase': 21 / 39},
    'spin': {'direction': 'left (counter-clockwise from above), right claw leads through the front as in Claw',
             'feet_off_ground_frames': [11.5, 24.5], 'degrees_at_contact': n['spin_at_contact_deg']},
    'root_motion': False, 'samples_per_frame': 4,
    'limits': passed, 'all_pass': all(v['pass'] for v in passed.values()),
    'numpy': n, 'blender': b, 'fbx_sweep': fx, 'fbx_package': pk,
    'sources_sha256': {p: sha(p) for p in ('Sweep_r01.blend', 'Sweep_Baked_r01.blend', 'final_samples.json', 'sweep_keys.py',
                                            'sweep_pose.py', 'build_sweep.py')},
    'notes': [
        'Reference: refs/wendigo-sweep-360.mp4 (Higgsfield e5ee213d, approved 29.09). The reference spin lasts ~0.5 s; '
        'in game the turn is compressed to frames 11.5-24.5 so the circle closes at the impact tick 21.',
        'Feet are planted (slip checked) on 0-11.5 and after landing; during the turn the right foot pivots under the pelvis '
        'and the left leg is lifted, so the whole pose turns about the Idle pelvis axis.',
        'The windup twist (-22 deg) and the follow-through (+16 deg) are pelvis/chest yaw only - planted feet do not slide.',
    ],
}
(HERE / 'validation.json').write_text(json.dumps(report, indent=1))
print(json.dumps(passed, indent=1)); print('ALL PASS', report['all_pass'])
