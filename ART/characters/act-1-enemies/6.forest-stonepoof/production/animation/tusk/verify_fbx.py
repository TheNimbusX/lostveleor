"""Проверка дубля Stonehoof_Tusk в выгруженном ForestStonehoof.fbx.

Запуск: blender -b --factory-startup --python tusk/verify_fbx.py -- FBX OUT.json
Импортирует FBX в пустую сцену и на дубле *Stonehoof_Tusk проверяет:
  - диапазон кадров (FBX-импорт сдвигает на +1: 0–26 -> 1–27);
  - корень на месте: ключи объекта арматуры постоянны, копыта стоят (дрейф
    мировых позиций leg_*_bot2 < 2 мм), кость body в начале и в конце совпадает;
  - контакт: ведущий (левый) клык быстрее всего около кадра 14 и вершина
    проноса после него; кадр 0 и кадр 26 совпадают со стойкой.
"""
import bpy, sys, json, math
from pathlib import Path
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
fbx, out = argv[0], Path(argv[1])
REST_TIP = Vector((0.309, -0.808, 0.58))  # вершина 516 мастера в покое (левый клык)
CONTACT, FRAMES = 14, 26

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
scene = bpy.context.scene
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
takes = [a for a in bpy.data.actions if a.name.endswith('Stonehoof_Tusk')]
assert len(takes) == 1, [a.name for a in bpy.data.actions]
act = takes[0]
start = int(round(act.frame_range[0]))
arm.animation_data.action = act
if len(act.slots):
    arm.animation_data.action_slot = act.slots[0]

# Ближайшая к кончику левого клыка вершина в покое (индексы после FBX другие).
scene.frame_set(start)
bpy.context.view_layer.update()
arm.data.pose_position = 'REST'
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
me = mesh.evaluated_get(dg).to_mesh()
tip = min(range(len(me.vertices)), key=lambda i: ((mesh.matrix_world @ me.vertices[i].co) - REST_TIP).length)
tip_rest_error = ((mesh.matrix_world @ me.vertices[tip].co) - REST_TIP).length
mesh.evaluated_get(dg).to_mesh_clear()
arm.data.pose_position = 'POSE'

hooves = [b.name for b in arm.pose.bones if b.name.endswith('_bot2') and b.name.startswith('leg_')]
anchor, drift, tips, body = {}, 0.0, [], []
for f in range(FRAMES + 1):
    scene.frame_set(start + f)
    bpy.context.view_layer.update()
    for n in hooves:
        p = arm.matrix_world @ arm.pose.bones[n].head
        anchor.setdefault(n, p.copy())
        drift = max(drift, (p - anchor[n]).length)
    body.append((arm.matrix_world @ arm.pose.bones['body'].head).copy())
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    tips.append((mesh.matrix_world @ me.vertices[tip].co).copy())
    ev.to_mesh_clear()
speed = [0.0] + [(tips[f] - tips[f - 1]).length * 30 for f in range(1, len(tips))]
fastest = max(range(len(speed)), key=lambda f: speed[f])
apex = max(range(len(tips)), key=lambda f: tips[f].z)
# FBX-экспорт ставит ключи трансформа объекта в каждом дубле (force start/end
# keying) — они обязаны быть постоянными: корень не едет и не крутится.
object_keys = {'%s[%d]' % (fc.data_path, fc.array_index): max(k.co[1] for k in fc.keyframe_points) - min(k.co[1] for k in fc.keyframe_points)
               for lay in act.layers for st in lay.strips for bag in st.channelbags
               for fc in bag.fcurves if not fc.data_path.startswith('pose.bones')}
report = {
    'take': act.name, 'range': list(act.frame_range),
    'object_level_keys': object_keys,
    'hoof_world_drift_m': drift,
    'body_start_end_m': (body[0] - body[-1]).length,
    'tip_vertex': tip, 'tip_rest_match_m': tip_rest_error,
    'tip_speed_m_s': [round(s, 3) for s in speed],
    'fastest_frame': fastest, 'apex_frame': apex,
    'contact_speed_share': speed[CONTACT] / max(speed),
    'tip_start_end_m': (tips[0] - tips[-1]).length,
}
ok = (report['range'] == [1.0, 27.0] and max(object_keys.values(), default=0) < 1e-6 and drift < .002
      and report['body_start_end_m'] < 1e-4 and tip_rest_error < .01
      and fastest in (13, 14) and apex > CONTACT and report['contact_speed_share'] > .6)
report['ok'] = ok
out.write_text(json.dumps(report, indent=2), encoding='utf8')
print('TUSK_FBX', json.dumps(report), flush=True)
assert ok, report
