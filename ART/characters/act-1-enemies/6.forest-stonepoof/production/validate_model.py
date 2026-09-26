"""Независимая повторная проверка сохранённого кандидата и исходных циклов."""
import bpy,bmesh,json,hashlib
from pathlib import Path
from mathutils import Matrix
ROOT=Path(__file__).resolve().parent.parent;OUT=ROOT/'production'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Stonehoof_ModelCandidate_r03.blend'))
obj=bpy.data.objects['SM_ForestStonehoof_LOD0'];arm=bpy.data.objects['ARM_ForestStonehoof']
mesh=obj.data;mesh.calc_loop_triangles();bm=bmesh.new();bm.from_mesh(mesh)
counts=[sum(g.weight>1e-7 for g in v.groups) for v in mesh.vertices]
weight_error=max(abs(sum(g.weight for g in v.groups)-1) for v in mesh.vertices)
result={'triangles':len(mesh.loop_triangles),'boundary_edges':sum(e.is_boundary for e in bm.edges),'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'unweighted_vertices':sum(c==0 for c in counts),'maximum_weights':max(counts),'normalization_error':weight_error,'mesh_scale':list(obj.scale),'armature_scale':list(arm.scale),'materials':len(mesh.materials),'texture_packed':all(i.packed_file is not None for i in bpy.data.images if i.name.endswith('.jpg'))}
bm.free()
assert result['triangles']<=25000
assert result['boundary_edges']==result['nonmanifold_edges']==result['unweighted_vertices']==0
assert result['maximum_weights']<=4 and weight_error<1e-5
assert result['mesh_scale']==result['armature_scale']==[1,1,1]
assert result['materials']==1 and result['texture_packed']
sources=json.loads((OUT/'source_audit.json').read_text())['sources']
result['source_hashes_match']=all(hashlib.sha256((ROOT/value['path']).read_bytes()).hexdigest()==value['sha256'] for value in sources.values())
assert result['source_hashes_match']
result['loop_endpoints']={}
for kind in ['idle','run']:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(OUT/'sources'/(kind+'.fbx')))
    arm=next(o for o in bpy.data.objects if o.type=='ARMATURE')
    clip=arm.animation_data.action;start,end=clip.frame_range
    poses=[]
    for frame in [int(start),int(end)]:
        bpy.context.scene.frame_set(frame)
        poses.append({b.name:b.matrix.copy() for b in arm.pose.bones})
    error=max(max(abs(poses[0][n][i][j]-poses[1][n][i][j]) for i in range(4) for j in range(4)) for n in poses[0])
    result['loop_endpoints'][kind]={'first':start,'last':end,'maximum_matrix_delta_source_units':error}
result['passed']=True
(OUT/'model_validation.json').write_text(json.dumps(result,indent=2),encoding='utf8')
print('STONEHOOF_VALIDATION '+json.dumps(result))
