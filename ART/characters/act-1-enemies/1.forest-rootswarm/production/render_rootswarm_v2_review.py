"""Кадры и замеры клипов Корнеполза v2 — из выгруженных FBX, то есть из того, что уйдёт в Unity.

Запуск: blender -b --factory-startup --python render_rootswarm_v2_review.py
Переменные:
  ROOTSWARM_V2_FBX_DIR — откуда брать Forest_RootSwarm@*.fbx (по умолчанию Resources/…/Forest_RootSwarm);
  ROOTSWARM_V2_TILES   — куда класть кадры и qc.json (по умолчанию artifacts/rootswarm-v2-review);
  ROOTSWARM_V2_CLIPS   — какие клипы (через запятую);
  ROOTSWARM_V2_VIEWS   — game,side,ref (игровая орто-камера 48°, орто сбоку, ракурс рефов Higgsfield);
  ROOTSWARM_V2_STEP    — шаг по кадрам (1 — все);
  ROOTSWARM_V2_NORENDER=1 — только замеры.

У Run и Death импортёр снимает проезд центра масс по XZ (IsRootTravelClipFile): в игре
центр масс стоит на месте. Кадры этих клипов снимаются так же — арматура сдвигается,
чтобы приближённый центр масс Humanoid не уезжал от кадра 0.

У ударов в кадрах 18→22 тело везёт Sim (выпад 0,5 м за 4 тика, RootSwarmLungeDistance):
на съёмке арматура едет вперёд так же, чтобы укус читался, как в бою.
"""
import bpy, math, json, os
from pathlib import Path
from mathutils import Vector

ROOT = Path(r'C:/Users/d.grab/Desktop/the-game')
LIVE = ROOT / 'razlom/Assets/Resources/Characters/Forest_RootSwarm'
FBX_DIR = Path(os.environ.get('ROOTSWARM_V2_FBX_DIR', str(LIVE)))
TILES = Path(os.environ.get('ROOTSWARM_V2_TILES', str(ROOT / 'artifacts/rootswarm-v2-review')))
CLIPS = [c for c in os.environ.get('ROOTSWARM_V2_CLIPS', 'Idle,Run,AttackA,AttackB,Hit,Death').split(',') if c]
VIEWS = [v for v in os.environ.get('ROOTSWARM_V2_VIEWS', 'game,side,ref').split(',') if v]
STEP = int(os.environ.get('ROOTSWARM_V2_STEP', '1'))
RENDER = os.environ.get('ROOTSWARM_V2_NORENDER', '') != '1'
PINNED = {'Run', 'Death'}
LUNGE = {'AttackA', 'AttackB'}
LUNGE_UNITS = .5 / (1.05 / 0.661713)   # 0,5 м игры в единицах исходника
LUNGE_FRAMES = (18, 22)
M = 'mixamorig:'
TILES.mkdir(parents=True, exist_ok=True)

# Приближённые доли массы сегментов Humanoid: (кость, следующая кость, доля).
SEGMENTS = [('Hips', 'Spine', .15), ('Spine', 'Spine1', .10), ('Spine1', 'Spine2', .10), ('Spine2', 'Neck', .12),
            ('Neck', 'Head', .03), ('Head', 'HeadTop_End', .05)]
for s in ('Left', 'Right'):
    SEGMENTS += [(s + 'Arm', s + 'ForeArm', .03), (s + 'ForeArm', s + 'Hand', .02), (s + 'Hand', s + 'HandMiddle1', .01),
                 (s + 'UpLeg', s + 'Leg', .10), (s + 'Leg', s + 'Foot', .045), (s + 'Foot', s + 'ToeBase', .015)]


def setup_scene():
    scene = bpy.context.scene
    scene.render.fps = 30
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = scene.render.resolution_y = 480
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'Standard'
    world = bpy.data.worlds.new('REVIEW_World')
    world.use_nodes = True
    scene.world = world
    sun_data = bpy.data.lights.new('REVIEW_Sun', 'SUN')
    sun_data.energy = 2.4
    sun_data.angle = math.radians(6)
    sun = bpy.data.objects.new('REVIEW_Sun', sun_data)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(40), math.radians(-15), math.radians(-35))
    ground_mat = bpy.data.materials.new('REVIEW_Ground')
    ground_mat.use_nodes = True
    bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
    ground = bpy.context.active_object
    ground.data.materials.append(ground_mat)
    cam_data = bpy.data.cameras.new('REVIEW_Cam')
    cam = bpy.data.objects.new('REVIEW_Cam', cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return scene, world, ground_mat, cam


def set_view(view, world, ground_mat, cam):
    bg = world.node_tree.nodes['Background']
    bsdf = ground_mat.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Roughness'].default_value = .95
    if view in ('game', 'side'):
        # game — игровая камера: орто, наклон 48°, моб вполоборота (азимут −60°).
        # side — орто сбоку на уровне тела: видны оттяг, бросок и шаг.
        bg.inputs[0].default_value = (.32, .38, .30, 1)
        bg.inputs[1].default_value = .6
        bsdf.inputs['Base Color'].default_value = (.07, .11, .035, 1)
        cam.data.type = 'ORTHO'
        cam.data.ortho_scale = 1.5
        # Кадр чуть впереди моба: туда уезжает выпад.
        target = Vector((0, -.17, .20))
        pitch = math.radians(48 if view == 'game' else 8)
        yaw = math.radians(-60 if view == 'game' else -90)
        direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
        cam.location = target + direction * 6
    else:
        # Ракурс рефов Higgsfield (start_swarm.png): перспектива 50 мм, азимут −40°, подъём 12°.
        bg.inputs[0].default_value = (.62, .66, .60, 1)
        bg.inputs[1].default_value = .9
        bsdf.inputs['Base Color'].default_value = (.47, .50, .42, 1)
        cam.data.type = 'PERSP'
        cam.data.lens = 50
        target = Vector((0, -.05, .30))
        az, el = math.radians(float(os.environ.get('ROOTSWARM_V2_REF_AZ', '-50'))), math.radians(12)
        direction = Vector((math.sin(az), -math.cos(az), math.tan(el))).normalized()
        cam.location = target + direction * 2.2
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()


def load_clip(name):
    for ob in list(bpy.data.objects):
        if ob.name.startswith('REVIEW_') or ob.type in {'CAMERA', 'LIGHT'} or ob.name.startswith('Plane'):
            continue
        bpy.data.objects.remove(ob, do_unlink=True)
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    bpy.ops.import_scene.fbx(filepath=str(FBX_DIR / f'Forest_RootSwarm@{name}.fbx'), use_anim=True)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh = next(o for o in bpy.data.objects if o.type == 'MESH' and not o.name.startswith('Plane'))
    texture = bpy.data.images.get('Forest_RootSwarm_BaseColor.JPEG') or \
        bpy.data.images.load(str(LIVE / 'Forest_RootSwarm_BaseColor.JPEG'))
    mat = bpy.data.materials.get('REVIEW_RootSwarm')
    if mat is None:
        mat = bpy.data.materials.new('REVIEW_RootSwarm')
        mat.use_nodes = True
        nodes = mat.node_tree.nodes
        image = nodes.new('ShaderNodeTexImage')
        image.image = texture
        mat.node_tree.links.new(image.outputs['Color'], nodes['Principled BSDF'].inputs['Base Color'])
        nodes['Principled BSDF'].inputs['Roughness'].default_value = .85
    mesh.data.materials.clear()
    mesh.data.materials.append(mat)
    action = arm.animation_data.action
    # Импорт FBX сдвигает такт на кадр: кадр Unity 0 = кадр Blender start.
    # Сдвиги съёмки (центр масс, выпад) — через пустышку-родителя: у самой арматуры
    # из FBX есть кривые положения объекта, и они перебивали бы её location на каждом кадре.
    pivot = bpy.data.objects.get('REVIEW_Pivot')
    if pivot is None:
        pivot = bpy.data.objects.new('REVIEW_Pivot', None)
        bpy.context.scene.collection.objects.link(pivot)
    pivot.location = (0, 0, 0)
    arm.parent = pivot
    arm.matrix_parent_inverse.identity()
    return arm, mesh, int(round(action.frame_range[0])), int(round(action.frame_range[1]))


def measure(arm, mesh, scene, offset, frame):
    scene.frame_set(frame + offset)
    bpy.context.view_layer.update()
    inv = arm.matrix_world.inverted()
    bones = arm.pose.bones

    def head(n):
        return bones[M + n].head.copy()

    com, mass = Vector(), 0.0
    for a, b, w in SEGMENTS:
        com += (head(a) + head(b)) * .5 * w
        mass += w
    com /= mass
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    to_arm = inv @ mesh.matrix_world
    pts = [to_arm @ v.co for v in me.vertices]
    ev.to_mesh_clear()
    min_y = min(p.y for p in pts)
    return {'com': com, 'min_y': min_y, 'pts': pts,
            'toes': {s: head(s + 'ToeBase') for s in ('Left', 'Right')},
            'tips': {s: sum((head(f'{s}Hand{f}3') for f in ('Index', 'Middle', 'Ring')), Vector()) / 3
                     for s in ('Left', 'Right')},
            'hips': head('Hips')}


scene, world, ground_mat, cam = setup_scene()
qc = {}
beak_index = None
for clip in CLIPS:
    arm, mesh, start, end = load_clip(clip)
    pivot = bpy.data.objects['REVIEW_Pivot']
    frames = list(range(0, end - start + 1))
    data = [measure(arm, mesh, scene, start, f) for f in frames]
    if beak_index is None:
        # Клюв: самая выдвинутая вперёд вершина верхней половины меша в позе привязки.
        to_arm = arm.matrix_world.inverted() @ mesh.matrix_world
        rest = [to_arm @ v.co for v in mesh.data.vertices]
        beak_index = max((i for i, p in enumerate(rest) if p.y > .30), key=lambda i: rest[i].z)
    com0 = data[0]['com']
    beak = [d['pts'][beak_index] for d in data]
    info = {'frames': [frames[0], frames[-1]],
            'min_y': [round(d['min_y'], 4) for d in data],
            'beak_z': [round(p.z, 4) for p in beak], 'beak_y': [round(p.y, 4) for p in beak],
            'com_xz_drift': round(max(math.hypot(d['com'].x - com0.x, d['com'].z - com0.z) for d in data), 4),
            'hips_y': [round(d['hips'].y, 4) for d in data],
            'toes': {s: [[round(d['toes'][s].x, 4), round(d['toes'][s].y, 4), round(d['toes'][s].z, 4)] for d in data]
                     for s in ('Left', 'Right')},
            'tips': {s: [[round(d['tips'][s].x, 4), round(d['tips'][s].y, 4), round(d['tips'][s].z, 4)] for d in data]
                     for s in ('Left', 'Right')}}
    last = data[-1]
    info['seam'] = round(max((a - b).length for a, b in zip(data[0]['pts'][::50], last['pts'][::50])), 5)
    speeds = [(beak[i + 1].z - beak[i - 1].z) * 15 for i in range(1, len(beak) - 1)]
    if speeds:
        info['beak_peak_speed_frame'] = 1 + max(range(len(speeds)), key=lambda i: speeds[i])
        info['beak_max_forward_frame'] = max(range(len(beak)), key=lambda i: beak[i].z)
    qc[clip] = info
    if RENDER:
        for view in VIEWS:
            set_view(view, world, ground_mat, cam)
            folder = TILES / clip
            folder.mkdir(parents=True, exist_ok=True)
            for f in frames[::STEP] + ([frames[-1]] if frames[-1] % STEP else []):
                scene.frame_set(f + start)
                # Арматура повёрнута на 90° вокруг X: её Z (вперёд) — мировой −Y.
                if clip in PINNED:
                    d = data[f]['com'] - com0
                    pivot.location = (-d.x, d.z, 0)
                elif clip in LUNGE:
                    a, b = LUNGE_FRAMES
                    pivot.location = (0, -LUNGE_UNITS * min(1.0, max(0.0, (f - a) / (b - a))), 0)
                else:
                    pivot.location = (0, 0, 0)
                scene.render.filepath = str(folder / f'{view}_{f:03d}.png')
                bpy.ops.render.render(write_still=True)
            pivot.location = (0, 0, 0)
    print('ROOTSWARM_V2_REVIEW', clip, json.dumps({k: v for k, v in info.items() if k not in ('toes', 'tips')}))

(TILES / 'qc.json').write_text(json.dumps(qc, indent=1), encoding='utf8')
print('ROOTSWARM_V2_REVIEW_DONE', TILES)
