"""Measure arm reach, foot contact, and the claw path in the art revision."""
from pathlib import Path
import json
import math
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
mesh_obj = next(o for o in scene.objects if o.type == 'MESH' and o.find_armature() == rig)
groups = {g.name: g.index for g in mesh_obj.vertex_groups}
hand_group = groups['R_hand']
hand_vids = [v.index for v in mesh_obj.data.vertices
             if any(g.group == hand_group and g.weight >= .25 for g in v.groups)]
foot_vids = {}
for side in ('L', 'R'):
    matches = {groups[n] for n in (f'{side}_foot', f'{side}_toe') if n in groups}
    foot_vids[side] = [v.index for v in mesh_obj.data.vertices
                       if any(g.group in matches and g.weight >= .25 for g in v.groups)]

report = {'source_blend': bpy.data.filepath, 'frames': [],
          'weighted_hand_vertex_count': len(hand_vids),
          'weighted_foot_vertex_counts': {s: len(ids) for s, ids in foot_vids.items()}}
for f in range(31):
    scene.frame_set(f)
    bones = rig.pose.bones
    wp = lambda n: rig.matrix_world @ bones[n].head
    wrist = wp('R_hand')
    deps = bpy.context.evaluated_depsgraph_get()
    obj = mesh_obj.evaluated_get(deps)
    m = obj.to_mesh(preserve_all_data_layers=False, depsgraph=deps)
    foot_zs = {s: sorted((obj.matrix_world @ m.vertices[i].co).z for i in ids)
               for s, ids in foot_vids.items()}
    foot_min = {s: round(zs[0], 4) for s, zs in foot_zs.items()}
    foot_q10 = {s: round(zs[int(len(zs)*.1)],4) for s,zs in foot_zs.items()}
    foot_q50 = {s: round(zs[int(len(zs)*.5)],4) for s,zs in foot_zs.items()}
    foot_under_5cm = {s: sum(z<=.05 for z in zs) for s,zs in foot_zs.items()}
    hand_points = [obj.matrix_world @ m.vertices[i].co for i in hand_vids]
    hand_min_z = round(min(p.z for p in hand_points),4)
    claw_tip = max(hand_points, key=lambda p: (p-wrist).length)
    obj.to_mesh_clear()
    shoulder = wp('R_arm_upper')
    target = wp('CTRL_R_hand')
    left_shoulder = wp('L_arm_upper')
    left_wrist = wp('L_hand')
    left_target = wp('CTRL_L_hand')
    shoulder_to_target = (shoulder-target).length
    arm_length = bones['R_arm_upper'].bone.length + bones['R_arm_lower'].bone.length
    row = {
      'frame': f,
      'feet_min_z': foot_min,
      'feet_q10_z': foot_q10,
      'feet_q50_z': foot_q50,
      'feet_vertices_under_5cm': foot_under_5cm,
      'right_foot_ctrl': [round(x,4) for x in wp('CTRL_R_foot')],
      'left_foot_ctrl': [round(x,4) for x in wp('CTRL_L_foot')],
      'right_wrist': [round(x,4) for x in wrist],
      'right_hand_min_z': hand_min_z,
      'right_claw_tip': [round(x,4) for x in claw_tip],
      'right_target': [round(x,4) for x in target],
      'right_shoulder': [round(x,4) for x in shoulder],
      'right_elbow': [round(x,4) for x in wp('R_arm_lower')],
      'right_elbow_pole': [round(x,4) for x in wp('CTRL_R_elbow')],
      'left_shoulder': [round(x,4) for x in left_shoulder],
      'left_wrist': [round(x,4) for x in left_wrist],
      'left_target': [round(x,4) for x in left_target],
      'left_wrist_target_error_m': round((left_wrist-left_target).length,4),
      'left_shoulder_target_m': round((left_shoulder-left_target).length,4),
      'left_arm_length_m': round(bones['L_arm_upper'].bone.length + bones['L_arm_lower'].bone.length,4),
      'right_wrist_target_error_m': round((wrist-target).length,4),
      'right_shoulder_target_m': round(shoulder_to_target,4),
      'right_arm_length_m': round(arm_length,4),
      'mask_head': [round(x,4) for x in wp('head')],
      'rig_root_xy': [round(rig.location.x,6), round(rig.location.y,6)],
    }
    report['frames'].append(row)

report['summary'] = {
  'max_wrist_target_error_m': max(r['right_wrist_target_error_m'] for r in report['frames']),
  'max_left_wrist_target_error_m': max(r['left_wrist_target_error_m'] for r in report['frames']),
  'max_arm_reach_ratio': round(max(r['right_shoulder_target_m']/r['right_arm_length_m'] for r in report['frames']),3),
  'min_contact_foot_z_m': min(min(r['feet_min_z'].values()) for r in report['frames']),
  'max_support_right_foot_z_m': max(r['feet_min_z']['R'] for r in report['frames'][:26]),
  'max_right_foot_control_drift_m': max(math.dist(r['right_foot_ctrl'], report['frames'][0]['right_foot_ctrl']) for r in report['frames']),
  'max_right_foot_control_xy_drift_m': max(math.dist(r['right_foot_ctrl'][:2], report['frames'][0]['right_foot_ctrl'][:2]) for r in report['frames']),
  'right_hand_contact_min_z_m': report['frames'][18]['right_hand_min_z'],
  'right_wrist_sweep_degrees_f15_to_f18': round((math.degrees(math.atan2(report['frames'][18]['right_wrist'][1], report['frames'][18]['right_wrist'][0])) - math.degrees(math.atan2(report['frames'][15]['right_wrist'][1], report['frames'][15]['right_wrist'][0])))%360, 1),
  'contact_frame': 18,
  'clip_end_frame': 30,
}
(HERE/'audit_v1.json').write_text(json.dumps(report, indent=2))
for f in (0,7,10,14,15,16,17,18,19,22,25,30):
    r = report['frames'][f]
    print('AUDIT', f, 'feet', r['feet_min_z'], 'wrist', r['right_wrist'],
          'target', r['right_target'], 'error', r['right_wrist_target_error_m'],
          'reach', r['right_shoulder_target_m'], '/', r['right_arm_length_m'])
print('SUMMARY', report['summary'])
