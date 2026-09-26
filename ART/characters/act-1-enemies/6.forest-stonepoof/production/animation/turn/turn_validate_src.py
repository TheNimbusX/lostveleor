"""Editable-file checks for validate_turn.py: explicit constraint influences,
IK target error, ground/reach, and deform-bone samples in armature space
(to compare with the baked clip). Read-only: never saves."""
import bpy, json, math
from pathlib import Path
HERE = Path(__file__).resolve().parent
FRAMES = 15


def goto(scene, f):
    fl = math.floor(f)
    scene.frame_set(int(fl), subframe=f - fl)
    bpy.context.view_layer.update()


def fcurves(act):
    out = []
    for lay in act.layers:
        for st in lay.strips:
            for bag in st.channelbags:
                out.extend(bag.fcurves)
    return out


def assign(actions):
    for obj_name, act_name in actions.items():
        o = bpy.data.objects[obj_name]
        o.animation_data_create()
        act = bpy.data.actions[act_name]
        o.animation_data.action = act
        if len(act.slots):
            o.animation_data.action_slot = act.slots[0]


def check_source(times):
    bpy.ops.wm.open_mainfile(filepath=str(HERE / 'Stonehoof_Turn_r01.blend'))
    scene = bpy.context.scene
    arm = bpy.data.objects['ARM_ForestStonehoof']
    carrier = bpy.data.objects['CTRL_PreviewMotion_Only']
    table = json.loads(scene['turn_actions'])
    names = [b.name for b in arm.data.bones if b.use_deform]
    cons = [(p.name, c.name) for p in arm.pose.bones for c in p.constraints]
    tags = ('front_left', 'front_right', 'hind_left', 'hind_right')
    result, samples = {}, {}
    for direction, actions in table.items():
        rig = fcurves(bpy.data.actions[actions['ARM_ForestStonehoof']])
        paths = {fc.data_path: fc for fc in rig}
        missing, unkeyed = [], []
        for pb, cn in cons:
            fc = paths.get('pose.bones["%s"].constraints["%s"].influence' % (pb, cn))
            if fc is None:
                missing.append(pb + '/' + cn)
                continue
            keyed = {round(k.co[0], 4) for k in fc.keyframe_points}
            if not {0.0, 15.0} <= keyed:
                unkeyed.append(pb + '/' + cn)
        assign(actions)
        ik_err, reach, rows = (0, None), {}, {}
        for f in times:
            goto(scene, f)
            for t in tags:
                b = arm.pose.bones['leg_' + t + '_bot2']
                tgt = bpy.data.objects['CTRL_Hoof_' + t].matrix_world.translation
                e = ((arm.matrix_world @ b.head) - tgt).length
                if e > ik_err[0]:
                    ik_err = (e, f, t)
                up = arm.pose.bones['MCH_Upper_' + t]
                lo = arm.pose.bones['MCH_Lower_' + t]
                d = (arm.matrix_world.inverted() @ tgt - up.head).length
                slack = up.length + lo.length - d
                if t not in reach or slack < reach[t][0]:
                    reach[t] = (slack, f)
            rows[f] = {n: (arm.pose.bones[n].head.copy(), arm.pose.bones[n].tail.copy()) for n in names}
        samples[direction] = rows
        result[direction] = {
            'constraints': len(cons),
            'influence_fcurves_missing': missing,
            'influence_not_keyed_at_0_and_15': unkeyed,
            'constraint_influences_explicit': not missing and not unkeyed,
            'max_ik_target_error_m': ik_err,
            'min_ik_reach_slack_m': reach,
        }
    return result, samples
