"""Кадры проверки Forest_RootSwarm@Hit с игровой камеры (орто, наклон 48°).

Запуск: blender -b --factory-startup --python render_hit_review.py
Потом: python make_hit_sheet.py — лист review/hit_r01.png.
Рендер идёт из выгруженного FBX, а не из сцены: проверяется то, что уйдёт в Unity.
"""
import bpy, math, json, os
from pathlib import Path
from mathutils import Vector

ROOT = Path(r'C:/Users/d.grab/Desktop/the-game')
LIVE = ROOT / 'razlom/Assets/Resources/Characters/Forest_RootSwarm'
FBX = Path(os.environ.get('ROOTSWARM_HIT_OUT', str(LIVE / 'Forest_RootSwarm@Hit.fbx')))
TILES = Path(os.environ.get('ROOTSWARM_HIT_TILES', str(ROOT / 'artifacts/rootswarm-hit-review')))
TILES.mkdir(parents=True, exist_ok=True)
FRAMES = [0, 2, 4, 6, 8, 10]

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = 30
bpy.ops.import_scene.fbx(filepath=str(FBX), use_anim=True)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = next(o for o in bpy.data.objects if o.type == 'MESH')
offset = int(arm.animation_data.action.frame_range[0])  # импорт FBX сдвигает такт на кадр

texture = bpy.data.images.load(str(LIVE / 'Forest_RootSwarm_BaseColor.JPEG'))
mat = bpy.data.materials.new('REVIEW_RootSwarm')
mat.use_nodes = True
nodes = mat.node_tree.nodes
bsdf = nodes['Principled BSDF']
image = nodes.new('ShaderNodeTexImage'); image.image = texture
mat.node_tree.links.new(image.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = .85
body.data.materials.clear(); body.data.materials.append(mat)

bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
ground = bpy.context.active_object
gmat = bpy.data.materials.new('REVIEW_Ground'); gmat.use_nodes = True
gmat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.20, .27, .12, 1)
gmat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 1
ground.data.materials.append(gmat)
# Метка точки покоя: крестик под ногами, чтобы читался отъезд таза.
for size, rot in [((.36, .012, .002), 0), ((.012, .36, .002), 0)]:
    bpy.ops.mesh.primitive_cube_add(location=(0, 0, .001))
    mark = bpy.context.active_object; mark.scale = size
    mm = bpy.data.materials.get('REVIEW_Mark') or bpy.data.materials.new('REVIEW_Mark')
    mm.use_nodes = True; mm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.55, .60, .40, 1)
    mark.data.materials.append(mm)

sun_data = bpy.data.lights.new('REVIEW_Sun', 'SUN'); sun_data.energy = 4.0; sun_data.angle = math.radians(8)
sun = bpy.data.objects.new('REVIEW_Sun', sun_data); scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(40), math.radians(-20), math.radians(-35))
scene.world = bpy.data.worlds.new('REVIEW_World'); scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (.55, .62, .70, 1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.1

# Орто-камера игры: наклон 48°, моб смотрит к игроку в нижней части экрана,
# чуть вполоборота — видны и отдача назад, и разлёт рук.
cam_data = bpy.data.cameras.new('REVIEW_Cam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 1.45
cam = bpy.data.objects.new('REVIEW_Cam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
target = Vector((0, -.06, .22)); pitch = math.radians(48); yaw = math.radians(28)
direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
cam.location = target + direction * 6
cam.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()

scene.render.engine = 'CYCLES'; scene.cycles.samples = 48; scene.cycles.use_denoising = True
scene.render.resolution_x = scene.render.resolution_y = 520
scene.render.image_settings.file_format = 'PNG'; scene.render.image_settings.color_mode = 'RGBA'
scene.view_settings.view_transform = 'Standard'

for frame in FRAMES:
    scene.frame_set(frame + offset)
    scene.render.film_transparent = False; ground.hide_render = False
    scene.render.filepath = str(TILES / f'hit_{frame:02d}.png'); bpy.ops.render.render(write_still=True)
# Силуэт покоя для призрака на листе.
scene.frame_set(FRAMES[0] + offset)
scene.render.film_transparent = True; ground.hide_render = True
for ob in bpy.data.objects:
    if ob.name.startswith('Cube'): ob.hide_render = True
scene.render.filepath = str(TILES / 'ghost.png'); bpy.ops.render.render(write_still=True)
(TILES / 'tiles.json').write_text(json.dumps({'frames': FRAMES, 'fps': 30}), encoding='utf8')
print('ROOTSWARM_HIT_REVIEW_TILES', TILES)
