"""bpy helpers for the turn cycle: rig lookup, spine keys with yaw, yawed hoof keys."""
import bpy, math
from mathutils import Vector, Quaternion, Euler

TAGS = ('front_left', 'front_right', 'hind_left', 'hind_right')
SPINE = ('body', 'body_top0', 'body_top1', 'neck0', 'head0', 'body_bot', 'pelvis', 'tail0')


def setup():
    arm = bpy.data.objects['ARM_ForestStonehoof']
    mesh = bpy.data.objects['SM_ForestStonehoof_LOD0']
    carrier = bpy.data.objects['CTRL_PreviewMotion_Only']
    rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
    legs = {}
    for tag in TAGS:
        p = 'leg_' + tag + '_'
        gi = mesh.vertex_groups[p + 'bot2'].index
        ids = [v.index for v in mesh.data.vertices
               if any(g.group == gi and g.weight > .95 for g in v.groups)]
        legs[tag] = {'foot': bpy.data.objects['CTRL_Hoof_' + tag],
                     'pole': bpy.data.objects['CTRL_Pole_' + tag],
                     'ankle': Vector(arm.data.bones[p + 'bot2'].head_local),
                     'rest_rotation': rest[p + 'bot2'].to_quaternion(),
                     'hoof_ids': ids,
                     'sole_z': min(mesh.data.vertices[i].co.z for i in ids),
                     'length': arm.pose.bones['MCH_Upper_' + tag].length + arm.pose.bones['MCH_Lower_' + tag].length}
    return {'arm': arm, 'mesh': mesh, 'carrier': carrier, 'rest': rest, 'legs': legs}


def clear_animation(ctx):
    objs = [ctx['arm'], ctx['carrier']] + [l[k] for l in ctx['legs'].values() for k in ('foot', 'pole')]
    for o in objs:
        o.animation_data_clear()
    ctx['carrier'].location = (0, 0, 0)
    ctx['carrier'].rotation_mode = 'QUATERNION'
    ctx['carrier'].rotation_quaternion = (1, 0, 0, 0)


def spine_pose(ctx, frame, angles, offset):
    """body_pose generalised with yaw: armature-space XYZ Euler conjugated into rest."""
    arm, rest = ctx['arm'], ctx['rest']
    for name, values in angles.items():
        pb = arm.pose.bones[name]
        pb.rotation_mode = 'QUATERNION'
        r = rest[name].to_quaternion()
        q = Euler(tuple(math.radians(a) for a in values), 'XYZ').to_quaternion()
        pb.rotation_quaternion = r.inverted() @ q @ r
        pb.keyframe_insert('rotation_quaternion', frame=frame, group=name)
    pb = arm.pose.bones['body']
    pb.location = rest['body'].to_quaternion().inverted() @ Vector(offset)
    pb.keyframe_insert('location', frame=frame, group='body')


def hoof_pose(ctx, tag, yaw_deg, dz, pitch, extra=(0, 0)):
    """Like build_clip.hoof_key plus a yaw about the object origin (carrier space)."""
    leg, mesh = ctx['legs'][tag], ctx['mesh']
    qp = Quaternion((1, 0, 0), math.radians(pitch))
    qy = Quaternion((0, 0, 1), math.radians(yaw_deg))
    pivot = Vector((leg['ankle'].x, leg['ankle'].y, leg['sole_z']))
    low = min((pivot + qp @ (mesh.data.vertices[i].co - pivot)).z for i in leg['hoof_ids'])
    loc = qy @ (pivot + qp @ (leg['ankle'] - pivot))
    loc += Vector((extra[0], extra[1], dz + leg['sole_z'] - low))
    return loc, qy @ qp @ leg['rest_rotation']


def hoof_key(ctx, tag, frame, yaw_deg, dz, pitch, extra=(0, 0)):
    foot = ctx['legs'][tag]['foot']
    loc, rot = hoof_pose(ctx, tag, yaw_deg, dz, pitch, extra)
    foot.location = loc
    foot.rotation_quaternion = rot
    foot.keyframe_insert('location', frame=frame)
    foot.keyframe_insert('rotation_quaternion', frame=frame)
    foot['sole_clearance'] = float(dz)
    foot.keyframe_insert('["sole_clearance"]', frame=frame)


def fcurves(o):
    ad = o.animation_data
    if not ad or not ad.action:
        return []
    return [fc for layer in ad.action.layers for strip in layer.strips
            for bag in strip.channelbags for fc in bag.fcurves]


def set_interp(o, kind='BEZIER', paths=None):
    for fc in fcurves(o):
        if paths and fc.data_path not in paths:
            continue
        for k in fc.keyframe_points:
            k.interpolation = kind
            if kind == 'BEZIER':
                k.handle_left_type = 'AUTO_CLAMPED'
                k.handle_right_type = 'AUTO_CLAMPED'


def remove_curves(o, data_path, index=None):
    ad = o.animation_data
    for layer in ad.action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in list(bag.fcurves):
                    if fc.data_path == data_path and (index is None or fc.array_index == index):
                        bag.fcurves.remove(fc)


def goto(scene, f):
    fl = math.floor(f)
    scene.frame_set(int(fl), subframe=f - fl)
    bpy.context.view_layer.update()
