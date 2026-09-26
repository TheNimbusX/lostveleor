"""Bake both turn directions to deform bones only (armature space => carrier yaw removed).

Same method as brake/bake_brake.py: sample every 1/16 frame, strip IK/controls,
key loc + quaternion (sign-continuous), BEZIER auto-clamped.
"""
import bpy, json, math
from pathlib import Path
from mathutils import Matrix
HERE = Path(__file__).resolve().parent
FRAMES = 15
STEPS = FRAMES * 16


def assign(actions):
    for obj_name, act_name in actions.items():
        o = bpy.data.objects[obj_name]
        o.animation_data_create()
        act = bpy.data.actions[act_name]
        o.animation_data.action = act
        if len(act.slots):
            o.animation_data.action_slot = act.slots[0]


def goto(scene, f):
    fl = math.floor(f)
    scene.frame_set(int(fl), subframe=f - fl)
    bpy.context.view_layer.update()


def bake():
    bpy.ops.wm.open_mainfile(filepath=str(HERE / 'Stonehoof_Turn_r01.blend'))
    bpy.context.preferences.filepaths.save_version = 0
    scene = bpy.context.scene
    arm = bpy.data.objects['ARM_ForestStonehoof']
    table = json.loads(scene['turn_actions'])
    names = [b.name for b in arm.data.bones if b.use_deform]
    samples = {}
    for direction, actions in table.items():
        assign(actions)
        rows = []
        for i in range(STEPS + 1):
            f = i / 16
            goto(scene, f)
            rows.append((f, {n: arm.pose.bones[n].matrix.copy() for n in names}))
        samples[direction] = rows
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
    report = {'deforming_bones': len(names), 'sample_step_frames': 1 / 16, 'root_yaw_removed': True,
              'scale_curves': False, 'owner_approved': False, 'clips': {}}
    baked = {}
    for direction, rows in samples.items():
        act = bpy.data.actions.new('AN_Stonehoof_' + direction)
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
        baked[direction] = act
        err = ang = 0.0
        for f, pose in rows:
            goto(scene, f)
            for n in names:
                m = arm.pose.bones[n].matrix
                err = max(err, (m.translation - pose[n].translation).length)
                d = abs(m.to_quaternion().normalized().dot(pose[n].to_quaternion().normalized()))
                ang = max(ang, math.degrees(2 * math.acos(min(1, d))))
        goto(scene, 0)
        a = {n: arm.pose.bones[n].matrix.copy() for n in names}
        goto(scene, FRAMES)
        seam = max((arm.pose.bones[n].matrix.translation - a[n].translation).length for n in names)
        report['clips'][act.name] = {'frames': FRAMES, 'seconds': FRAMES / 30,
                                     'max_joint_error_m': err, 'max_rotation_error_deg': ang,
                                     'loop_seam_position_m': seam}
    arm.animation_data.action = None
    for direction, act in baked.items():
        track = arm.animation_data.nla_tracks.new()
        track.name = direction + ' - 90 deg in place, no yaw'
        strip = track.strips.new(act.name, 0, act)
        strip.action_frame_start, strip.action_frame_end = 0, FRAMES
        strip.extrapolation = 'NOTHING'
        track.mute = direction != 'TurnLeft'
    arm['runtime_actions'] = 'AN_Stonehoof_TurnLeft, AN_Stonehoof_TurnRight (0.5 s = 90 deg, phase-driven)'
    arm['root_motion'] = 'None. Carrier yaw removed; the sim rotates the entity 6 deg/tick.'
    scene.frame_start, scene.frame_end = 0, FRAMES
    goto(scene, 0)
    bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'Stonehoof_Turn_Baked_r01.blend'))
    (HERE / 'bake_validation.json').write_text(json.dumps(report, indent=2))
    print('TURN_BAKED', json.dumps(report))
    for c in report['clips'].values():
        assert c['max_joint_error_m'] < .001, report
    return report


if __name__ == '__main__':
    bake()
