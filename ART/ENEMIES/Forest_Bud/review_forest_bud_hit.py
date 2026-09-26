"""Кадры проверки клипа Hit с игровой камеры (орто, наклон 48°).

Запуск: blender -b --factory-startup --python review_forest_bud_hit.py
Потом: python make_hit_contact_sheet.py — лист review/hit_r01.png.
"""
import bpy, math, json, os
from pathlib import Path
from mathutils import Vector

R = Path(r'C:/Users/d.grab/Desktop/the-game')
BLEND = Path(os.environ.get('FOREST_BUD_BLEND', str(R / 'ART/ENEMIES/Forest_Bud/ForestBudRanged_Production.blend')))
TILES = Path(os.environ.get('FOREST_BUD_HIT_TILES', str(R / 'artifacts/forest-bud-production/review-hit')))
TILES.mkdir(parents=True, exist_ok=True)
FRAMES = [1, 3, 4, 6, 8, 11]

bpy.ops.wm.open_mainfile(filepath=str(BLEND))
scene = bpy.context.scene
rig = bpy.data.objects['ARM_ForestBudRanged']
for tr in rig.animation_data.nla_tracks: tr.mute = True
action = bpy.data.actions['Hit']
rig.animation_data.action = action; rig.animation_data.action_slot = action.slots[0]
for o in bpy.data.objects:
    if o.type in ['LIGHT', 'CAMERA'] or o.name == 'STUDIO_Floor': o.hide_render = True

bpy.ops.mesh.primitive_plane_add(size=8, location=(0, 0, 0))
ground = bpy.context.active_object
gmat = bpy.data.materials.new('REVIEW_Ground'); gmat.use_nodes = True
gmat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.20, .27, .12, 1)
gmat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 1
ground.data.materials.append(gmat)
marks = []
for size in [(.6, .015, .002), (.015, .6, .002)]:
    bpy.ops.mesh.primitive_cube_add(location=(0, 0, .001))
    mark = bpy.context.active_object; mark.scale = size; marks.append(mark)
    mm = bpy.data.materials.get('REVIEW_Mark') or bpy.data.materials.new('REVIEW_Mark')
    mm.use_nodes = True; mm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.55, .60, .40, 1)
    mark.data.materials.append(mm)

sun_data = bpy.data.lights.new('REVIEW_Sun', 'SUN'); sun_data.energy = 4.0; sun_data.angle = math.radians(8)
sun = bpy.data.objects.new('REVIEW_Sun', sun_data); scene.collection.objects.link(sun)
sun.rotation_euler = (math.radians(40), math.radians(-20), math.radians(-35))
scene.world = bpy.data.worlds.new('REVIEW_World'); scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value = (.55, .62, .70, 1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.1

# Та же постановка, что у листа Корнеполза: моб смотрит к игроку чуть вполоборота.
cam_data = bpy.data.cameras.new('REVIEW_Cam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = 2.3
cam = bpy.data.objects.new('REVIEW_Cam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
target = Vector((0, -.05, .55)); pitch = math.radians(48); yaw = math.radians(28)
direction = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
cam.location = target + direction * 8
cam.rotation_euler = (-direction).to_track_quat('-Z', 'Y').to_euler()

scene.render.engine = 'CYCLES'; scene.cycles.samples = 48; scene.cycles.use_denoising = True
scene.render.resolution_x = scene.render.resolution_y = 520; scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'; scene.render.image_settings.color_mode = 'RGBA'
scene.view_settings.view_transform = 'Standard'
for frame in FRAMES:
    scene.frame_set(frame)
    scene.render.film_transparent = False; ground.hide_render = False
    scene.render.filepath = str(TILES / f'hit_{frame:02d}.png'); bpy.ops.render.render(write_still=True)
scene.frame_set(FRAMES[0])
scene.render.film_transparent = True; ground.hide_render = True
for mark in marks: mark.hide_render = True
scene.render.filepath = str(TILES / 'ghost.png'); bpy.ops.render.render(write_still=True)
(TILES / 'tiles.json').write_text(json.dumps({'frames': FRAMES, 'fps': 30}), encoding='utf8')
print('FOREST_BUD_HIT_REVIEW_TILES', TILES)
