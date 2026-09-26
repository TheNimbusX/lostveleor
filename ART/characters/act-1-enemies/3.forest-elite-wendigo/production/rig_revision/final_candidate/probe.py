import json
from pathlib import Path
import bpy
from mathutils import Vector

out = Path(__file__).resolve().parent
mesh = bpy.data.objects['SM_ForestWendigo_LOD0']
verts = mesh.data.vertices
edges = [(e.vertices[0], e.vertices[1]) for e in mesh.data.edges]
names = {g.index:g.name for g in mesh.vertex_groups}

targets = [Vector((.069,-.397,2.202)), Vector((.24,-.255,2.2)),
           Vector((-.437,.248,2.199)), Vector((.567,-.542,1.199))]
near=[]
for target in targets:
    candidates=[]
    for a,b in edges:
        mid=(verts[a].co+verts[b].co)*.5
        dist=(mid-target).length
        if dist<.02:
            candidates.append((dist,a,b))
    candidates.sort()
    rows=[]
    for dist,a,b in candidates[:12]:
        def info(i):
            v=verts[i]
            return {'id':i,'co':list(map(lambda x:round(x,4),v.co)),
                    'weights':{names[g.group]:round(g.weight,3) for g in v.groups if g.weight>.001}}
        rows.append({'dist':round(dist,4),'edge_m':round((verts[a].co-verts[b].co).length,4),
                     'a':info(a),'b':info(b)})
    near.append({'target':list(target),'edges':rows})

(out/'probe.json').write_text(json.dumps(near,indent=2),encoding='utf-8')
counts={}
for v in verts:
    w={names[g.group]:g.weight for g in v.groups if g.weight>.001}
    armR=sum(w.get(n,0) for n in ('R_arm_upper','R_arm_lower','R_hand'))
    armL=sum(w.get(n,0) for n in ('L_arm_upper','L_arm_lower','L_hand'))
    if armR>.5 and w.get('R_leg_upper',0)>.01:
        counts.setdefault('R_arm_with_R_leg',[]).append((v.index,round(w['R_leg_upper'],3),list(v.co)))
    if armL>.5 and w.get('L_leg_upper',0)>.01:
        counts.setdefault('L_arm_with_L_leg',[]).append((v.index,round(w['L_leg_upper'],3),list(v.co)))
    if abs(v.co.x)<.3 and 2.1<v.co.z<2.3 and sum(w.get(n,0) for n in ('L_clavicle','R_clavicle','L_arm_upper','R_arm_upper'))>.3:
        counts.setdefault('central_collar_arm',[]).append((v.index,round(sum(w.get(n,0) for n in ('L_clavicle','R_clavicle','L_arm_upper','R_arm_upper')),3),list(v.co)))
(out/'counts.json').write_text(json.dumps({k:{'count':len(x),'max':max(x,key=lambda item:item[1]),'sample':x[:10]} for k,x in counts.items()},indent=2),encoding='utf-8')
print('PROBE',str(out/'probe.json'))
