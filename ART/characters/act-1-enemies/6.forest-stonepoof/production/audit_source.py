"""Проверка исходников камнекопыта в отдельном Blender, без изменения оригиналов."""
import bpy, bmesh, json, hashlib, zipfile
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'production'
SOURCES = OUT / 'sources'
SOURCES.mkdir(parents=True, exist_ok=True)
files = {'static': ROOT / 'wooden+boar+3d+model.fbx'}
for key, name in [('rig', 'woodenboar#0000 (1).zip'), ('idle', 'woodenboar#0000_idle.zip'), ('run', 'woodenboar#0000_run.zip')]:
    with zipfile.ZipFile(ROOT / name) as archive:
        member = next(n for n in archive.namelist() if n.lower().endswith('.fbx'))
        target = SOURCES / (key + '.fbx')
        target.write_bytes(archive.read(member))
        files[key] = target

report = {'stage':'model_review_not_owner_approved', 'sources':{}, 'budget_triangles':25000}
for key, path in files.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    bpy.context.view_layer.update()
    row = {'path':str(path.relative_to(ROOT)), 'sha256':hashlib.sha256(path.read_bytes()).hexdigest(), 'meshes':[], 'armatures':[], 'actions':[]}
    for obj in bpy.data.objects:
        if obj.type == 'MESH':
            mesh = obj.data
            mesh.calc_loop_triangles()
            bm = bmesh.new(); bm.from_mesh(mesh)
            pts = [obj.matrix_world @ v.co for v in mesh.vertices]
            minimum = [min(v[i] for v in pts) for i in range(3)]
            maximum = [max(v[i] for v in pts) for i in range(3)]
            influences = [len([g for g in v.groups if g.weight > 1e-6]) for v in mesh.vertices]
            sums = [sum(g.weight for g in v.groups) for v in mesh.vertices]
            row['meshes'].append({'name':obj.name, 'vertices':len(mesh.vertices), 'triangles':len(mesh.loop_triangles), 'boundary_edges':sum(e.is_boundary for e in bm.edges), 'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges), 'loose_vertices':sum(not v.link_edges for v in bm.verts), 'bounds_min':minimum, 'bounds_max':maximum, 'dimensions':[maximum[i]-minimum[i] for i in range(3)], 'scale':list(obj.scale), 'uv_layers':[u.name for u in mesh.uv_layers], 'materials':[m.name if m else None for m in mesh.materials], 'groups':[g.name for g in obj.vertex_groups], 'max_influences':max(influences,default=0), 'unweighted':sum(s < 1e-6 for s in sums), 'weight_error':max([abs(s-1) for s in sums],default=0)})
            bm.free()
        elif obj.type == 'ARMATURE':
            row['armatures'].append({'name':obj.name, 'scale':list(obj.scale), 'bones':[{'name':b.name,'parent':b.parent.name if b.parent else None,'head':list(b.head_local),'tail':list(b.tail_local)} for b in obj.data.bones]})
    for action in bpy.data.actions:
        row['actions'].append({'name':action.name,'frame_range':list(action.frame_range),'slots':[s.identifier for s in action.slots]})
    row['fps'] = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
    report['sources'][key] = row
    if key == 'rig':
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'Stonehoof_SourceRig.blend'))
(OUT / 'source_audit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
print('STONEHOOF_AUDIT ' + json.dumps({k:{'triangles':sum(m['triangles'] for m in v['meshes']),'dimensions':[m['dimensions'] for m in v['meshes']],'bones':sum(len(a['bones']) for a in v['armatures']),'actions':v['actions']} for k,v in report['sources'].items()}))
