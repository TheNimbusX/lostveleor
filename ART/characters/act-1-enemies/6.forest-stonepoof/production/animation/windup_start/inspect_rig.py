import bpy,json
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;PROD=HERE.parents[1]
bpy.ops.wm.open_mainfile(filepath=str(PROD/'Stonehoof_ModelCandidate_r03.blend'))
arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0']
report={'arm_world':[list(row) for row in arm.matrix_world],'mesh_world':[list(row) for row in mesh.matrix_world],'bones':[],'foot_vertices':{}}
for b in arm.data.bones:
    report['bones'].append({'name':b.name,'parent':b.parent.name if b.parent else None,'head':list(b.head_local),'tail':list(b.tail_local),'length':b.length,'matrix':[list(row) for row in b.matrix_local],'connected':b.use_connect})
for vg in mesh.vertex_groups:
    if 'bot' not in vg.name:continue
    verts=[v for v in mesh.data.vertices if any(g.group==vg.index and g.weight>.45 for g in v.groups)]
    if not verts:continue
    lo=Vector(tuple(min(v.co[i] for v in verts) for i in range(3)))
    hi=Vector(tuple(max(v.co[i] for v in verts) for i in range(3)))
    bottom=sorted(verts,key=lambda v:v.co.z)[:12]
    report['foot_vertices'][vg.name]={'count':len(verts),'min':list(lo),'max':list(hi),'bottom_ids':[v.index for v in bottom],'bottom_centroid':list(sum((v.co for v in bottom),Vector())/len(bottom))}
(HERE/'rig_audit.json').write_text(json.dumps(report,indent=2))
print(json.dumps({'bones':[{k:b[k] for k in ('name','parent','head','tail','length')} for b in report['bones']],'feet':report['foot_vertices']}))
