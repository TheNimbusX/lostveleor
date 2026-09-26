import json
from pathlib import Path
import bpy

out=Path(__file__).resolve().parent
M=bpy.data.objects['SM_ForestWendigo_LOD0']
R=bpy.data.objects['ARM_ForestWendigo']
M.data.calc_loop_triangles()
names={g.index:g.name for g in M.vertex_groups}
counter={'R_arm_has_leg':0,'L_arm_has_leg':0,'central_collar_arm_over_30pct':0,
         'bad_normalized':0,'unweighted':0,'over_four':0}
left_leaks=[]
for v in M.data.vertices:
    w={names[g.group]:g.weight for g in v.groups if g.weight>.0001}
    if sum(w.get(k,0) for k in ('R_arm_upper','R_arm_lower','R_hand'))>.5 and w.get('R_leg_upper',0)>.01:
        counter['R_arm_has_leg']+=1
    if sum(w.get(k,0) for k in ('L_arm_upper','L_arm_lower','L_hand'))>.5 and w.get('L_leg_upper',0)>.01:
        counter['L_arm_has_leg']+=1
        left_leaks.append({'id':v.index,'co':[round(x,3) for x in v.co],'weights':w})
    if abs(v.co.x)<.3 and 2.1<v.co.z<2.3 and sum(w.get(k,0) for k in ('L_clavicle','R_clavicle','L_arm_upper','R_arm_upper'))>.3:
        counter['central_collar_arm_over_30pct']+=1
    counter['bad_normalized']+=abs(sum(w.values())-1)>.005
    counter['unweighted']+=not w
    counter['over_four']+=len(w)>4
report={'file':bpy.data.filepath,'triangles':len(M.data.loop_triangles),
        'vertices':len(M.data.vertices),'deform_bones':sum(b.use_deform for b in R.data.bones),
        'controls':sum(not b.use_deform for b in R.data.bones),
        'material_slots':[s.material.name if s.material else None for s in M.material_slots],
        'uv_layers':[layer.name for layer in M.data.uv_layers],
        'counts':counter}
report['left_leaks']=left_leaks
(out/'candidate_audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('CANDIDATE_AUDIT',json.dumps(report),flush=True)
