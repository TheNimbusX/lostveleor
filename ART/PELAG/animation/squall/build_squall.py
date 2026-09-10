"""Авторинг Шквала через Blender MCP: исходники остаются отдельными actions."""
import bpy
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path('C:/Users/d.grab/Desktop/the-game')
FOLDER = ROOT / 'ART/PELAG/animation/squall'
OUT = ROOT / 'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo'
scene = bpy.context.scene
scene.render.fps = 30

def smooth(t):
    t = max(0.0, min(1.0, t))
    return t*t*(3-2*t)

sources = {}
for key in ['Horizontal', 'Backhand', 'Downward', 'Sprint']:
    rig = bpy.data.objects['Squall_Source_' + key]
    action = bpy.data.actions['AN_Squall_Source_' + key]
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots[0]
    sources[key] = rig

def sample(key, frame):
    scene.frame_set(int(frame), subframe=frame-int(frame))
    rig = sources[key]
    return ({b.name: b.matrix_basis.to_quaternion().copy() for b in rig.pose.bones},
            rig.pose.bones['mixamorig:Hips'].matrix.translation.copy())

def blend(a, b, weight):
    return ({n: a[0][n].slerp(b[0][n], weight) for n in a[0]}, a[1].lerp(b[1], weight))

def remap(frame, keys):
    for (a, av), (b, bv) in zip(keys, keys[1:]):
        if frame <= b:
            # Без остановки на контакте: плавность задаётся уже исходным mocap.
            return av + (bv-av) * (frame-a)/(b-a)
    return keys[-1][1]

definitions = {
    'Start': ('Horizontal', [(1,9),(3,15),(6,18),(9,22)], 9, 3),
    'A': ('Horizontal', [(1,12),(3,15.5),(6,18),(9,22)], 9, 3),
    'B': ('Backhand', [(1,18),(3,19.2),(6,21.4),(9,25)], 9, 11),
    'Finish': ('Downward', [(1,9),(3,12.5),(6,16),(9,21),(15,36)], 15, 3),
    'RecoverA': ('Horizontal', [(1,18),(4,23),(9,41)], 9, None),
    'RecoverB': ('Backhand', [(1,21.4),(4,29),(9,55)], 9, None),
}

# Сначала считываем все исходные позы, чтобы новые ключи не влияли на sampling.
poses = {}
leg_names = [n for n in sources['Sprint'].pose.bones.keys()
             if any(part in n for part in ['UpLeg','Leg','Foot','ToeBase'])]
for name, (key, mapping, end, sprint_start) in definitions.items():
    frames = []
    for frame in range(1, end+1):
        pose = sample(key, remap(frame, mapping))
        if sprint_start is not None:
            sprint = sample('Sprint', min(17, sprint_start + (frame-1)*0.8))
            weight = 0.88 * (1-smooth((frame-2)/4))
            for bone in leg_names:
                pose[0][bone] = pose[0][bone].slerp(sprint[0][bone], weight)
            pose[0]['mixamorig:Hips'] = pose[0]['mixamorig:Hips'].slerp(sprint[0]['mixamorig:Hips'], weight*0.30)
            pose = (pose[0], pose[1].lerp(sprint[1], weight*0.45))
        frames.append(pose)
    poses[name] = frames

rig = bpy.data.objects.get('Squall_Export_Rig')
if rig is None:
    rig = sources['Horizontal'].copy()
    rig.data = sources['Horizontal'].data.copy()
    scene.collection.objects.link(rig)
    rig.name = 'Squall_Export_Rig'
rig.animation_data_clear()
rig.animation_data_create()
rig.hide_set(False)
rig.scale = (0.01,0.01,0.01)
hip_rest = rig.data.bones['mixamorig:Hips'].head_local.copy()

def reset():
    for b in rig.pose.bones:
        b.rotation_mode = 'QUATERNION'
        b.rotation_quaternion = (1,0,0,0)
        b.location = (0,0,0)
        b.scale = (1,1,1)
    bpy.context.view_layer.update()

def export(name, animated):
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),
        use_selection=True, object_types={'ARMATURE'}, add_leaf_bones=False,
        bake_anim=animated, bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=animated, bake_anim_simplify_factor=0,
        axis_forward='-Z', axis_up='Y')

reset()
scene.frame_start = scene.frame_end = 1
export('Pelag_AN_SquallBind', False)
report = {}
for name, frames in poses.items():
    rig.animation_data.action = None
    for track in list(rig.animation_data.nla_tracks):
        rig.animation_data.nla_tracks.remove(track)
    reset()
    action_name = 'AN_Pelag_Squall_' + name
    previous = bpy.data.actions.get(action_name)
    if previous is not None:
        bpy.data.actions.remove(previous)
    action = bpy.data.actions.new(action_name)
    rig.animation_data.action = action
    for frame, (rotations, hips) in enumerate(frames, 1):
        for b in rig.pose.bones:
            b.rotation_quaternion = rotations[b.name]
        bpy.context.view_layer.update()
        # Перемещение по земле задаёт Sim. От mocap сохраняем высоту таза,
        # присед и поворот; это исключает двойной рывок и возврат к корню.
        hip = rig.pose.bones['mixamorig:Hips']
        matrix = hip.matrix.copy()
        matrix.translation = Vector((hip_rest.x, max(hip_rest.y-0.19, min(hip_rest.y+0.08, hips.y)), hip_rest.z))
        hip.matrix = matrix
        for b in rig.pose.bones:
            b.keyframe_insert('rotation_quaternion', frame=frame, group=b.name)
        hip.keyframe_insert('location', frame=frame, group=hip.name)
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for curve in bag.fcurves:
                    for point in curve.keyframe_points:
                        point.interpolation = 'BEZIER'
                        point.handle_left_type = point.handle_right_type = 'AUTO_CLAMPED'
    slot = rig.animation_data.action_slot
    track = rig.animation_data.nla_tracks.new()
    strip = track.strips.new(action.name, 1, action)
    strip.action_slot = slot
    rig.animation_data.action = None
    scene.frame_start, scene.frame_end = 1, len(frames)
    export('Pelag_AN_Squall'+name, True)
    report[name] = {'frames':len(frames), 'contact_frame':6 if name not in ['RecoverA','RecoverB'] else 1}
    action.use_fake_user = True

bpy.ops.wm.save_as_mainfile(filepath=str(FOLDER/'Pelag_Squall_Work.blend'))
result = report
