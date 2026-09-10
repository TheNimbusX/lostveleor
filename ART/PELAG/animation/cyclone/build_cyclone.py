"""Циклон: монтаж Mixamo, зеркалирование на руку цепи, замыкание и экспорт через Blender MCP."""
import math
from pathlib import Path
import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = Path('C:/Users/d.grab/Desktop/the-game')
FOLDER = ROOT / 'ART/PELAG/animation/cyclone'
OUT = FOLDER / 'export'
OUT.mkdir(exist_ok=True)
scene = bpy.data.scenes['Pelag Cyclone Authoring']
bpy.context.window.scene = scene
scene.render.fps = 30
sources = {key: bpy.data.objects['Cyclone_Source_' + key] for key in ['Horizontal', 'Attack', 'Slash']}
for key, source in sources.items():
    source.animation_data.action = bpy.data.actions['AN_Cyclone_Source_' + key]
    source.animation_data.action_slot = source.animation_data.action.slots[0]

def smooth(t):
    t = max(0., min(1., t))
    return t*t*(3.-2.*t)

def other(name):
    return name.replace('Left', '#').replace('Right', 'Left').replace('#', 'Right')

reflection = Matrix.Diagonal(Vector((-1., 1., 1., 1.)))

def sample(key, frame):
    scene.frame_set(int(frame), subframe=frame-int(frame))
    source = sources[key]
    matrices = {}
    for bone in source.data.bones:
        partner = source.data.bones[other(bone.name)]
        delta = source.pose.bones[partner.name].matrix @ partner.matrix_local.inverted()
        matrices[bone.name] = reflection @ delta @ reflection @ bone.matrix_local
    rotations = {}
    for bone in source.data.bones:
        options = {}
        if bone.parent:
            options = dict(parent_matrix=matrices[bone.parent.name], parent_matrix_local=bone.parent.matrix_local)
        basis = bone.convert_local_to_pose(matrices[bone.name], bone.matrix_local, invert=True, **options)
        rotations[bone.name] = basis.to_quaternion()
    return rotations, matrices['mixamorig:Hips'].translation.copy()

def blend(a, b, weight):
    return ({n: a[0][n].slerp(b[0][n], weight) for n in a[0]}, a[1].lerp(b[1], weight))

loop_start = sample('Horizontal', 9)
guard = sample('Attack', 1)
poses = {'Loop': [], 'Start': [], 'End': []}
for f in range(31):
    if f <= 21:
        p = sample('Horizontal', 9 + 19*f/21)
    else:
        p = blend(sample('Horizontal', 28), loop_start, smooth((f-21)/9))
    poses['Loop'].append(p)
for f in range(7):
    p = sample('Slash', 8 + 13*f/6)
    poses['Start'].append(blend(p, loop_start, smooth((f-2)/4)))
for f in range(10):
    p = sample('Attack', 21 + 10*f/9)
    poses['End'].append(blend(loop_start, p, smooth(f/6)))

rig = bpy.data.objects.get('Cyclone_Export_Rig')
if rig is None:
    rig = sources['Horizontal'].copy()
    rig.data = sources['Horizontal'].data.copy()
    scene.collection.objects.link(rig)
    rig.name = 'Cyclone_Export_Rig'
rig.parent = None
rig.scale = (.01, .01, .01)
rig.animation_data_clear()
rig.animation_data_create()
rig.hide_set(False)
hip_rest = rig.data.bones['mixamorig:Hips'].head_local.copy()

def reset():
    for b in rig.pose.bones:
        b.rotation_mode = 'QUATERNION'
        b.rotation_quaternion = (1, 0, 0, 0)
        b.location = (0, 0, 0)
        b.scale = (1, 1, 1)
    bpy.context.view_layer.update()

def turn_bone(bone, delta):
    matrix = bone.matrix.copy()
    location = matrix.translation.copy()
    matrix = delta.to_matrix().to_4x4() @ matrix
    matrix.translation = location
    bone.matrix = matrix
    bpy.context.view_layer.update()

def solve_limb(upper_name, lower_name, end_name, target, pole):
    upper, lower, end = [rig.pose.bones['mixamorig:' + n] for n in (upper_name, lower_name, end_name)]
    start = upper.head.copy()
    l1, l2 = (lower.head-start).length, (end.head-lower.head).length
    direction = (target-start).normalized()
    distance = min((target-start).length, (l1+l2)*.985)
    distance = max(abs(l1-l2)+.001, distance)
    along = (l1*l1 + distance*distance - l2*l2)/(2*distance)
    height = math.sqrt(max(0., l1*l1-along*along))
    perpendicular = pole-start
    perpendicular -= direction*perpendicular.dot(direction)
    if perpendicular.length < .0001: perpendicular = Vector((0,0,1))
    elbow = start + direction*along + perpendicular.normalized()*height
    turn_bone(upper, (lower.head-start).rotation_difference(elbow-start))
    turn_bone(lower, (end.head-lower.head).rotation_difference(start+direction*distance-lower.head))

def export(name, animated):
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')), use_selection=True,
        object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=animated,
        bake_anim_use_all_actions=False, bake_anim_use_nla_strips=animated,
        bake_anim_simplify_factor=0, axis_forward='-Z', axis_up='Y')

reset()
scene.frame_start = scene.frame_end = 1
export('Pelag_AN_CycloneBind', False)
# Ориентацию подошв берём из устойчивой исходной стойки до решения IK.
for b in rig.pose.bones: b.rotation_quaternion = guard[0][b.name]
bpy.context.view_layer.update()
foot_rotations = {side:rig.pose.bones['mixamorig:'+side+'Foot'].matrix.to_quaternion().copy() for side in ['Left','Right']}
report = {}
for phase, frames in poses.items():
    rig.animation_data.action = None
    for track in list(rig.animation_data.nla_tracks): rig.animation_data.nla_tracks.remove(track)
    reset()
    name = 'AN_Pelag_Cyclone_' + phase
    previous = bpy.data.actions.get(name)
    if previous is not None: bpy.data.actions.remove(previous)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    rig.animation_data.action = action
    endpoints = []
    for index, (rotations, hips) in enumerate(frames):
        for b in rig.pose.bones:
            rotation = rotations[b.name]
            correction = 1. if phase != 'End' else 1.-smooth(index/9)
            if any(part in b.name for part in ['Hips', 'Spine', 'Neck', 'Head']):
                rotation = rotation.slerp(guard[0][b.name].slerp(rotation, .65), correction)
            if any(part in b.name for part in ['RightShoulder', 'RightArm', 'RightForeArm', 'RightHand']):
                rotation = rotation.slerp(guard[0][b.name].slerp(rotation, .15), correction)
            b.rotation_quaternion = rotation
            b.location = (0,0,0)
        bpy.context.view_layer.update()
        hip = rig.pose.bones['mixamorig:Hips']
        matrix = hip.matrix.copy()
        matrix.translation = Vector((hip_rest.x, max(hip_rest.y-.18, min(hip_rest.y+.05, hips.y)), hip_rest.z))
        hip.matrix = matrix
        bpy.context.view_layer.update()
        # Кисть проходит непрерывный круг; исходный одиночный взмах иначе разворачивается назад.
        angle = 2*math.pi*index/30 if phase == 'Loop' else 0
        hand = Vector((.32 + .17*math.cos(angle), 1.15 + .06*math.sin(angle), .04 + .25*math.sin(angle)))
        weight = 1. if phase == 'Loop' else smooth(index/6) if phase == 'Start' else 1.-smooth(index/7)
        target = rig.pose.bones['mixamorig:LeftHand'].head.lerp(hand, weight)
        solve_limb('LeftArm', 'LeftForeArm', 'LeftHand', target, Vector((1., .9, -.35)))
        # Перенос веса остаётся в корпусе, а опора не скользит после удаления root motion.
        for side, x, z in [('Left', .19, .08), ('Right', -.21, -.04)]:
            solve_limb(side+'UpLeg', side+'Leg', side+'Foot', Vector((x, .12, z)), Vector((x, .45, 1.)))
            foot = rig.pose.bones['mixamorig:'+side+'Foot']
            matrix = foot_rotations[side].to_matrix().to_4x4()
            matrix.translation = foot.head.copy()
            foot.matrix = matrix
            bpy.context.view_layer.update()
        for b in rig.pose.bones:
            b.keyframe_insert('rotation_quaternion', frame=index+1, group=b.name)
        hip.keyframe_insert('location', frame=index+1, group=hip.name)
        endpoints.append({b.name:list(b.rotation_quaternion) for b in rig.pose.bones})
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    for point in curve.keyframe_points:
                        point.interpolation = 'BEZIER'
                        point.handle_left_type = point.handle_right_type = 'AUTO_CLAMPED'
    slot = rig.animation_data.action_slot
    track = rig.animation_data.nla_tracks.new()
    strip = track.strips.new(name, 1, action)
    strip.action_slot = slot
    rig.animation_data.action = None
    scene.frame_start, scene.frame_end = 1, len(frames)
    export('Pelag_AN_Cyclone'+phase, True)
    report[phase] = {'frames': len(frames), 'seam_degrees':max(Quaternion(endpoints[0][b]).rotation_difference(Quaternion(endpoints[-1][b])).angle*180/math.pi for b in endpoints[0]) if phase=='Loop' else None}
bpy.ops.wm.save_as_mainfile(filepath=str(FOLDER/'Pelag_Cyclone_Work.blend'))
result = report
