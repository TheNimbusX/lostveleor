"""Корнеполз: реакция на попадание, Forest_RootSwarm@Hit.fbx.

Запуск: blender -b --factory-startup --python build_rootswarm_hit.py

Скелет и меш берутся из Forest_RootSwarm@Idle.fbx — того же файла, из которого
игра грузит тело, — поэтому имена костей Mixamo, bind pose и единицы совпадают
с остальными клипами, и Humanoid-аватар строится так же. Первый и последний
кадры — кадр 1 стойки: вход и выход из реакции не дёргают позу.

Движение: резкая отдача назад (кадры 0–2), присед-сжатие (3–5), возврат с
малым перелётом (6–10). Ступни держит аналитический IK: стоят на месте и не
проваливаются, проезд таза остаётся в позе (правило ConfigureMobClip — у Hit
проезд не снимается).
"""
import bpy, math, json, os
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(r'C:/Users/d.grab/Desktop/the-game')
LIVE = ROOT / 'razlom/Assets/Resources/Characters/Forest_RootSwarm'
PROD = ROOT / 'ART/characters/act-1-enemies/1.forest-rootswarm/production'
SOURCE = LIVE / 'Forest_RootSwarm@Idle.fbx'
# ROOTSWARM_HIT_OUT — пробная выгрузка мимо Assets, чтобы открытый редактор не импортировал черновики.
OUT_FBX = Path(os.environ.get('ROOTSWARM_HIT_OUT', str(LIVE / 'Forest_RootSwarm@Hit.fbx')))
PROD = Path(os.environ.get('ROOTSWARM_HIT_PROD', str(PROD)))
PROD.mkdir(parents=True, exist_ok=True)
FRAMES = 11  # кадры 0…10: 10 интервалов = 0,333 с при 30 fps

# Кривые по кадрам 0…10. Отдача короткая и резкая, присед догоняет её,
# голова отстаёт на кадр, руки разлетаются вместе с отдачей.
RECOIL = [0, .60, 1.0, .90, .55, .22, 0, -.08, -.06, -.02, 0]
SQUASH = [0, .15, .45, .85, 1.0, .75, .38, .08, -.10, -.04, 0]
HEAD = [0, .25, .70, 1.0, .70, .20, -.18, -.22, -.08, 0, 0]
ARMS = [0, .55, 1.0, .85, .45, .12, -.06, -.04, 0, 0, 0]

# Пространство арматуры Mixamo после импорта: +Y вверх, +Z вперёд, +X влево.
UP, FORWARD, LEFT = Vector((0, 1, 0)), Vector((0, 0, 1)), Vector((1, 0, 0))
M = 'mixamorig:'

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30
bpy.ops.import_scene.fbx(filepath=str(SOURCE), use_anim=True)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = next(o for o in bpy.data.objects if o.type == 'MESH')
idle = arm.animation_data.action
scene.frame_set(1)
bpy.context.view_layer.update()
base = {pb.name: pb.matrix_basis.copy() for pb in arm.pose.bones}
channels = set()
for layer in idle.layers:
    for strip in layer.strips:
        for bag in strip.channelbags:
            for fc in bag.fcurves:
                channels.add(fc.data_path.split('"')[1])

bones = sorted(arm.data.bones, key=lambda b: len(b.parent_recursive))
rest_rel = {b.name: (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local.copy()
            for b in bones}


def pose(basis):
    """Позы костей в пространстве арматуры — как pb.matrix, но без depsgraph."""
    result = {}
    for b in bones:
        parent = result[b.parent.name] if b.parent else Matrix.Identity(4)
        result[b.name] = parent @ rest_rel[b.name] @ basis[b.name]
    return result


def rotate(basis, name, axis, degrees):
    rotate_matrix(basis, name, Matrix.Rotation(math.radians(degrees), 3, axis))


def rotate_matrix(basis, name, rotation):
    """Поворот кости вокруг её головы, ось задана в пространстве арматуры."""
    p = pose(basis)[name]
    q = p @ basis[name].inverted()
    head = p.translation
    around = Matrix.Translation(head) @ rotation.to_4x4() @ Matrix.Translation(-head)
    basis[name] = q.inverted() @ around @ q @ basis[name]


def translate(basis, name, offset):
    p = pose(basis)[name]
    q = p @ basis[name].inverted()
    basis[name] = q.inverted() @ Matrix.Translation(offset) @ q @ basis[name]


rest_pose = pose(base)
for pb in arm.pose.bones:
    error = (pb.matrix.translation - rest_pose[pb.name].translation).length
    if error > 1e-5:
        raise RuntimeError(f'Своя сборка поз разошлась с Blender: {pb.name} {error}')


def plant_leg(basis, side):
    """Двухзвенный IK: голеностоп и ступня возвращаются в позу стойки."""
    up, low, foot = M + side + 'UpLeg', M + side + 'Leg', M + side + 'Foot'
    p = pose(basis)
    hip, knee, ankle = p[up].translation, p[low].translation, p[foot].translation
    target = rest_pose[foot].translation
    upper, lower = (knee - hip).length, (ankle - knee).length
    reach = target - hip
    dist = min(max(reach.length, abs(upper - lower) + 1e-5), upper + lower - 1e-5)
    along = reach.normalized()
    # Сгиб колена — в той же плоскости, что и в стойке.
    bend = rest_pose[low].translation - rest_pose[up].translation
    bend = (bend - along * bend.dot(along)).normalized()
    u = (upper * upper - lower * lower + dist * dist) / (2 * dist)
    new_knee = hip + along * u + bend * math.sqrt(max(0.0, upper * upper - u * u))
    rotate_matrix(basis, up, (knee - hip).rotation_difference(new_knee - hip).to_matrix())
    p = pose(basis)
    knee, ankle = p[low].translation, p[foot].translation
    rotate_matrix(basis, low, (ankle - knee).rotation_difference(target - knee).to_matrix())
    p = pose(basis)
    rotate_matrix(basis, foot, rest_pose[foot].to_3x3().normalized() @ p[foot].to_3x3().normalized().inverted())


def hit_pose(i):
    b = {name: m.copy() for name, m in base.items()}
    r, s, h, a = RECOIL[i], SQUASH[i], HEAD[i], ARMS[i]
    # Таз: назад и вниз, лёгкий наклон назад. 0,04 единицы = ~6 см в игре (масштаб 1,59).
    translate(b, M + 'Hips', -FORWARD * .040 * r - UP * .034 * s)
    rotate(b, M + 'Hips', LEFT, -6 * r)
    # Корпус: отброс назад, в присед — сжатие вперёд; чуть закрученный.
    rotate(b, M + 'Spine', LEFT, -10 * r + 5 * s)
    rotate(b, M + 'Spine1', LEFT, -8 * r + 4 * s)
    rotate(b, M + 'Spine1', UP, 5 * r)
    rotate(b, M + 'Spine2', LEFT, -7 * r + 3 * s)
    # Голова отстаёт: запрокидывается на кадр позже корпуса и кивает в присед.
    rotate(b, M + 'Neck', LEFT, -6 * h + 2 * s)
    rotate(b, M + 'Head', LEFT, -15 * h + 6 * s)
    # Руки разлетаются в стороны вместе с отдачей.
    rotate(b, M + 'LeftShoulder', FORWARD, 8 * a)
    rotate(b, M + 'RightShoulder', FORWARD, -8 * a)
    rotate(b, M + 'LeftArm', FORWARD, 20 * a)
    rotate(b, M + 'RightArm', FORWARD, -20 * a)
    rotate(b, M + 'LeftForeArm', LEFT, -12 * a)
    rotate(b, M + 'RightForeArm', LEFT, -12 * a)
    plant_leg(b, 'Left')
    plant_leg(b, 'Right')
    return b


arm.animation_data.action = None
bpy.data.actions.remove(idle)
action = bpy.data.actions.new('Hit')
arm.animation_data.action = action
for pb in arm.pose.bones:
    pb.rotation_mode = 'QUATERNION'
previous = {}
report = {'frames': FRAMES, 'fps': 30, 'feet_drift_m': 0.0, 'hips_back_max': 0.0, 'hips_down_max': 0.0}
for i in range(FRAMES):
    # Такт Mixamo начинается с нуля: Unity получает кадры 0…10, как у соседних клипов.
    frame = i
    b = hit_pose(i)
    p = pose(b)
    for side in ['Left', 'Right']:
        for part in ['Foot', 'ToeBase', 'Toe_End']:
            name = M + side + part
            report['feet_drift_m'] = max(report['feet_drift_m'], (p[name].translation - rest_pose[name].translation).length)
    delta = p[M + 'Hips'].translation - rest_pose[M + 'Hips'].translation
    report['hips_back_max'] = max(report['hips_back_max'], -delta.dot(FORWARD))
    report['hips_down_max'] = max(report['hips_down_max'], -delta.dot(UP))
    for pb in arm.pose.bones:
        if pb.name not in channels: continue
        pb.matrix_basis = b[pb.name]
        q = pb.rotation_quaternion.copy()
        # Непрерывность знака: q и −q одна поза, но интерполяция между ними крутит кость.
        if pb.name in previous and previous[pb.name].dot(q) < 0: q.negate(); pb.rotation_quaternion = q
        previous[pb.name] = q
        pb.keyframe_insert('location', frame=frame, group=pb.name)
        pb.keyframe_insert('rotation_quaternion', frame=frame, group=pb.name)
        pb.keyframe_insert('scale', frame=frame, group=pb.name)
action.use_fake_user = True
scene.frame_start, scene.frame_end = 0, FRAMES - 1
scene.frame_set(0)

end_error = max((a - b).length for a, b in zip(
    [m.translation for m in pose(hit_pose(0)).values()], [m.translation for m in pose(hit_pose(FRAMES - 1)).values()]))
report['start_end_error_m'] = end_error
if report['feet_drift_m'] > 1e-4: raise RuntimeError('Ступни поехали: ' + str(report['feet_drift_m']))
if end_error > 1e-6: raise RuntimeError('Реакция не возвращается в стойку')

bpy.ops.object.select_all(action='DESELECT')
arm.select_set(True); body.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=str(OUT_FBX), use_selection=True, object_types={'ARMATURE', 'MESH'},
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
    use_mesh_modifiers=False, mesh_smooth_type='FACE', add_leaf_bones=False, armature_nodetype='NULL',
    use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_bones=True,
    bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
    bake_anim_step=1, bake_anim_simplify_factor=0, path_mode='STRIP', embed_textures=False)
bpy.ops.wm.save_as_mainfile(filepath=str(PROD / 'Forest_RootSwarm_Hit.blend'))
(PROD / 'hit_build.json').write_text(json.dumps(report, indent=2), encoding='utf8')
print('ROOTSWARM_HIT_READY', json.dumps(report))
