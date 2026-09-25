"""Constrained local weight relaxation on the Wendigo's extreme reference poses.

Run on v4 candidate. Only over-stretched body seams are smoothed; topology and
bone transforms remain untouched. This is a diagnostic candidate, not export.
"""
import ast
import json
import math
from pathlib import Path
import bpy
from mathutils import Quaternion, Vector

OUT=Path(__file__).resolve().parent
M=bpy.data.objects['SM_ForestWendigo_LOD0']
R=bpy.data.objects['ARM_ForestWendigo']
rig=R
scene=bpy.context.scene
ids={g.index:g.name for g in M.vertex_groups}
weights=[{ids[g.group]:g.weight for g in v.groups if g.weight>.0001} for v in M.data.vertices]
deform={b.name for b in R.data.bones if b.use_deform}
verts=M.data.vertices
edges=[(e.vertices[0],e.vertices[1]) for e in M.data.edges]
rest=[v.co.copy() for v in verts]
length=[(rest[a]-rest[b]).length for a,b in edges]
active=[i for i,(a,b) in enumerate(edges) if length[i]>=.005 and
        1.42<rest[a].z<2.42 and 1.42<rest[b].z<2.42 and
        max(abs(rest[a].x),abs(rest[b].x))<.92 and
        max(abs(rest[a].y),abs(rest[b].y))<.92]

source=(OUT.parent/'stress_current_rig.py').read_text(encoding='utf-8')
tree=ast.parse(source)
names={'reset','move','rotate','ik','pose_quad_crouch','pose_claw_arc','pose_fold_dead'}
nodes=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in names]
code=compile(ast.Module(body=nodes,type_ignores=[]),'<pose_defs>','exec')
exec(code,globals())
poses=[('quad_crouch',pose_quad_crouch),('claw_arc',pose_claw_arc),('fold_dead',pose_fold_dead)]

def norm(w):
    w={k:v for k,v in w.items() if v>.00005 and k in deform}
    top=sorted(w.items(),key=lambda x:x[1],reverse=True)[:4]
    total=sum(v for _,v in top)
    return {k:v/total for k,v in top}

def write(ids_changed):
    for i in ids_changed:
        for g in M.vertex_groups:g.remove([i])
        for name,val in weights[i].items():M.vertex_groups[name].add([i],val,'REPLACE')
    M.data.update()
    bpy.context.view_layer.update()

def assess():
    max_ratio={}
    high={}
    for name,pose in poses:
        pose()
        bpy.context.view_layer.update()
        evaluated=M.evaluated_get(bpy.context.evaluated_depsgraph_get())
        deformed=evaluated.to_mesh()
        points=[v.co.copy() for v in deformed.vertices]
        local={}
        for i in active:
            a,b=edges[i]
            ratio=(points[a]-points[b]).length/length[i]
            if ratio>2.5:local[i]=ratio
            high[i]=max(high.get(i,0),ratio)
        max_ratio[name]=round(max(high.values()),3)
        evaluated.to_mesh_clear()
    reset()
    return max_ratio,{i:val for i,val in high.items() if val>2.5}

history=[]
for iteration in range(11):
    maximum,hot=assess()
    history.append({'iteration':iteration,'maximum':maximum,'hot_edges':len(hot)})
    print('OPT_ITER',iteration,maximum,len(hot),flush=True)
    if not hot:break
    accum={}
    counts={}
    for idx,ratio in hot.items():
        a,b=edges[idx]
        # Fragile 5 mm edges need a wider neighborhood than a single pair;
        # each round diffuses only the amount needed to tame current stress.
        strength=min(.70,max(.15,(ratio-2.5)/6.0))
        for i,j in ((a,b),(b,a)):
            d=accum.setdefault(i,{})
            counts[i]=counts.get(i,0)+strength
            for key,val in weights[j].items():d[key]=d.get(key,0)+strength*val
    before={i:dict(weights[i]) for i in accum}
    for i,other in accum.items():
        count=counts[i]
        neighbor={k:v/count for k,v in other.items()}
        blend=min(.55,.24+.04*iteration)
        combined={k:(1-blend)*before[i].get(k,0)+blend*neighbor.get(k,0)
                  for k in set(before[i])|set(neighbor)}
        weights[i]=norm(combined)
    write(accum.keys())

maximum,hot=assess()
history.append({'iteration':'final','maximum':maximum,'hot_edges':len(hot)})
reset();bpy.context.view_layer.update()
tris=len(M.data.loop_triangles)
qa={'triangles':tris,'bones':len(R.data.bones),'over_four':0,'unnormalized':0,
    'unweighted':0,'wrong_groups':0}
for v in verts:
    ww=[(ids[g.group],g.weight) for g in v.groups if g.weight>.0001]
    qa['over_four']+=len(ww)>4
    qa['unnormalized']+=abs(sum(value for _,value in ww)-1)>.005
    qa['unweighted']+=not ww
    qa['wrong_groups']+=any(name not in deform for name,_ in ww)
assert tris<=25000 and not any(qa[k] for k in ('over_four','unnormalized','unweighted','wrong_groups')),qa
target=OUT/'ForestWendigo_Rig_WeightRevision_v5.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(target),compress=True)
(OUT/'weight_revision_v5_report.json').write_text(json.dumps({'source':'v4','output':str(target),
    'history':history,'qa':qa},indent=2),encoding='utf-8')
print('OPT_DONE',json.dumps(history[-1]),flush=True)
