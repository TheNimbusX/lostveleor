import bpy
import json
import math
from pathlib import Path

repo = Path(r'C:/Users/d.grab/Desktop/the-game')
out = repo / 'ART/CAMP/unified-2026-09-13'
out.mkdir(parents=True, exist_ok=True)
source = repo / 'razlom/Assets/Resources/Environment/Camp/fonar.fbx'
# Фоновый процесс работает с копией модели и не открывает авторский blend-файл.
bpy.ops.wm.read_factory_settings(use_empty=True)
collection = bpy.data.collections.new('COL_CampLantern_Optimized')
bpy.context.scene.collection.children.link(collection)
bpy.ops.import_scene.fbx(filepath=str(source), use_anim=False)
objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
report = {'source': str(source), 'target_triangles': 20000, 'texture': 'original atlas preserved', 'objects': []}
for obj in objects:
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    for old_collection in list(obj.users_collection):
        old_collection.objects.unlink(obj)
    collection.objects.link(obj)
    obj.name = 'SM_CampLantern_LOD0'
    obj.data.calc_loop_triangles()
    before = len(obj.data.loop_triangles)
    bounds_before = [list(v) for v in obj.bound_box]
    modifier = obj.modifiers.new('Preserve silhouette below 20k', 'DECIMATE')
    modifier.decimate_type = 'COLLAPSE'
    modifier.ratio = min(1.0, 18000 / before)
    modifier.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.calc_loop_triangles()
    after = len(obj.data.loop_triangles)
    assert after <= 20000, after
    assert all(math.isfinite(c) for v in obj.data.vertices for c in v.co)
    assert len(obj.data.uv_layers) > 0
    report['objects'].append({'name': obj.name, 'before_triangles': before, 'after_triangles': after, 'materials': len(obj.data.materials), 'uv_layers': len(obj.data.uv_layers), 'bounds_before': bounds_before, 'bounds_after': [list(v) for v in obj.bound_box]})
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
for obj in objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
bpy.ops.export_scene.fbx(filepath=str(out / 'CampLantern_LOD0.fbx'), use_selection=True, object_types={'MESH'}, use_mesh_modifiers=True, bake_anim=False, add_leaf_bones=False, axis_forward='-Z', axis_up='Y', path_mode='STRIP')
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'CampLantern_Optimized.blend'))
(out / 'lantern-optimization.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
