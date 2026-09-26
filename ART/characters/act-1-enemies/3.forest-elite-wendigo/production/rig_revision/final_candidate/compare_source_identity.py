"""Verify isolated skin candidate retains source geometry/bind/material/UV."""
import json
from pathlib import Path
import bpy

OUT=Path(__file__).resolve().parent
source=OUT.parents[1]/'rig/ForestWendigo_Rig.blend'
with bpy.data.libraries.load(str(source),link=False) as (available,loaded):
    loaded.objects=['SM_ForestWendigo_LOD0','ARM_ForestWendigo']
old_mesh=next(o for o in loaded.objects if o.type=='MESH')
old_rig=next(o for o in loaded.objects if o.type=='ARMATURE')
new_mesh=bpy.data.objects['SM_ForestWendigo_LOD0']
new_rig=bpy.data.objects['ARM_ForestWendigo']
a,b=old_mesh.data,new_mesh.data
a.calc_loop_triangles();b.calc_loop_triangles()
assert len(a.vertices)==len(b.vertices)
assert len(a.polygons)==len(b.polygons)
assert len(a.loop_triangles)==len(b.loop_triangles)
max_vertex=max((x.co-y.co).length for x,y in zip(a.vertices,b.vertices))
same_faces=all(tuple(x.vertices)==tuple(y.vertices) for x,y in zip(a.polygons,b.polygons))
same_uv_layers=[u.name for u in a.uv_layers]==[u.name for u in b.uv_layers]
max_uv=max((u.uv-v.uv).length for la,lb in zip(a.uv_layers,b.uv_layers)
           for u,v in zip(la.data,lb.data))
def mat_signature(material):
    return (bool(material.use_nodes),
            tuple(sorted((n.bl_idname,n.label) for n in material.node_tree.nodes)) if material.use_nodes else (),
            tuple(sorted(n.image.filepath for n in material.node_tree.nodes if n.type=='TEX_IMAGE' and n.image))
            if material.use_nodes else ())
same_material=[mat_signature(m) for m in a.materials]==[mat_signature(m) for m in b.materials]
same_bone_names={x.name for x in old_rig.data.bones}=={x.name for x in new_rig.data.bones}
max_bind=max(max(abs(x-y) for row_a,row_b in zip(old_rig.data.bones[name].matrix_local,
             new_rig.data.bones[name].matrix_local) for x,y in zip(row_a,row_b))
             for name in (x.name for x in old_rig.data.bones))
max_object=max(abs(x-y) for row_a,row_b in zip(old_mesh.matrix_world,new_mesh.matrix_world)
               for x,y in zip(row_a,row_b))
result={'source':str(source),'candidate':bpy.data.filepath,
        'vertices':len(a.vertices),'triangles':len(a.loop_triangles),
        'max_position_delta_m':max_vertex,'same_faces':same_faces,
        'same_uv_layers':same_uv_layers,'max_uv_delta':max_uv,
        'same_material':same_material,
        'source_material_names':[m.name for m in a.materials],
        'candidate_material_names':[m.name for m in b.materials],
        'same_bone_names':same_bone_names,
        'max_bind_matrix_delta':max_bind,'max_object_matrix_delta':max_object}
(OUT/'source_identity_report.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('SOURCE_IDENTITY',json.dumps(result),flush=True)
assert max_vertex==0 and same_faces and same_uv_layers and max_uv==0
assert same_material and same_bone_names and max_bind<1e-7 and max_object<1e-7
