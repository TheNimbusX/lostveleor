"""Запекание Stonehoof_Tusk в кости деформации (как turn/bake_turn.py).

Каждые 1/16 кадра снимаются матрицы костей в пространстве арматуры, IK,
управляющие объекты и служебные кости убираются, ключи loc + кватернион
(знак непрерывен), BEZIER auto-clamped. Итог: Stonehoof_Tusk_Baked_r01.blend,
действие AN_Stonehoof_Tusk (0–26), bake_validation.json.
"""
import bpy, json, math
from pathlib import Path
from mathutils import Matrix
HERE = Path(__file__).resolve().parent
FRAMES = 26
STEPS = FRAMES * 16


def goto(scene, f):
    fl = math.floor(f)
    scene.frame_set(int(fl), subframe=f - fl)
    bpy.context.view_layer.update()


def bake():
    bpy.ops.wm.open_mainfile(filepath=str(HERE / 'Stonehoof_Tusk_r01.blend'))
    bpy.context.preferences.filepaths.save_version = 0
    scene = bpy.context.scene
    arm = bpy.data.objects['ARM_ForestStonehoof']
    names = [b.name for b in arm.data.bones if b.use_deform]
    rows = []
    for i in range(STEPS + 1):
        f = i / 16
        goto(scene, f)
        rows.append((f, {n: arm.pose.bones[n].matrix.copy() for n in names}))
    for o in bpy.data.objects:
        if o.animation_data:
            o.animation_data_clear()
    arm.parent = None
    arm.matrix_world = Matrix.Identity(4)
    for p in arm.pose.bones:
        for c in list(p.constraints):
            p.constraints.remove(c)
    for o in list(bpy.data.objects):
        if o.name.startswith('CTRL_'):
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for b in list(arm.data.edit_bones):
        if not b.use_deform:
            arm.data.edit_bones.remove(b)
    bpy.ops.object.mode_set(mode='OBJECT')
    for act in list(bpy.data.actions):
        bpy.data.actions.remove(act)
    act = bpy.data.actions.new('AN_Stonehoof_Tusk')
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    prev = {}
    for f, pose in rows:
        for n in names:
            b = arm.pose.bones[n]
            b.rotation_mode = 'QUATERNION'
            b.matrix = pose[n]
            b.scale = (1, 1, 1)
            q = b.rotation_quaternion.copy()
            if n in prev and q.dot(prev[n]) < 0:
                q.negate()
            b.rotation_quaternion = q
            prev[n] = q
            b.keyframe_insert('location', frame=f, group=n)
            b.keyframe_insert('rotation_quaternion', frame=f, group=n)
            bpy.context.view_layer.update()
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for k in fc.keyframe_points:
                        k.interpolation = 'BEZIER'
                        k.handle_left_type = k.handle_right_type = 'AUTO_CLAMPED'
    act.use_frame_range = True
    act.frame_start, act.frame_end = 0, FRAMES
    err = ang = 0.0
    for f, pose in rows:
        goto(scene, f)
        for n in names:
            m = arm.pose.bones[n].matrix
            err = max(err, (m.translation - pose[n].translation).length)
            d = abs(m.to_quaternion().normalized().dot(pose[n].to_quaternion().normalized()))
            ang = max(ang, math.degrees(2 * math.acos(min(1, d))))
    # Корень не едет: объект арматуры без ключей, кость body в начале и в конце на месте покоя.
    goto(scene, 0)
    a = {n: arm.pose.bones[n].matrix.copy() for n in names}
    goto(scene, FRAMES)
    seam = max((arm.pose.bones[n].matrix.translation - a[n].translation).length for n in names)
    object_keys = [fc.data_path for lay in act.layers for st in lay.strips for bag in st.channelbags
                   for fc in bag.fcurves if not fc.data_path.startswith('pose.bones')]
    report = {'deforming_bones': len(names), 'sample_step_frames': 1 / 16, 'scale_curves': False,
              'owner_approved': False,
              'clips': {act.name: {'frames': FRAMES, 'seconds': FRAMES / 30, 'contact_frame': 14,
                                   'max_joint_error_m': err, 'max_rotation_error_deg': ang,
                                   'start_end_position_m': seam, 'object_level_keys': object_keys}}}
    arm.animation_data.action = None
    track = arm.animation_data.nla_tracks.new()
    track.name = 'Tusk - 26 кадров на месте, контакт на 14'
    strip = track.strips.new(act.name, 0, act)
    strip.action_frame_start, strip.action_frame_end = 0, FRAMES
    strip.extrapolation = 'NOTHING'
    arm['runtime_actions'] = 'AN_Stonehoof_Tusk (0.87 s, контакт на кадре 14, фаза от тика sim)'
    arm['root_motion'] = 'Нет. Корень на месте; sim владеет позицией.'
    scene.frame_start, scene.frame_end = 0, FRAMES
    goto(scene, 0)
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'Stonehoof_Tusk_Baked_r01.blend'))
    (HERE / 'bake_validation.json').write_text(json.dumps(report, indent=2), encoding='utf8')
    print('TUSK_BAKED', json.dumps(report), flush=True)
    c = report['clips'][act.name]
    assert c['max_joint_error_m'] < .001 and not object_keys, report
    return report


if __name__ == '__main__':
    bake()
