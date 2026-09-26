"""Isolated collar/wrist skin repair for the approved Wendigo mesh.

Run on ForestWendigo_Rig_WeightRevision_v2.blend. No geometry or rig edits.
"""
import json
from pathlib import Path
import bpy

OUT = Path(__file__).resolve().parent
M = bpy.data.objects['SM_ForestWendigo_LOD0']
R = bpy.data.objects['ARM_ForestWendigo']
verts = M.data.vertices
id_to_name = {g.index:g.name for g in M.vertex_groups}
weight = [{id_to_name[g.group]:g.weight for g in v.groups if g.weight>.00001} for v in verts]
original = [dict(w) for w in weight]

def smoothstep(lo, hi, x):
    t = min(1.0,max(0.0,(x-lo)/(hi-lo)))
    return t*t*(3.0-2.0*t)

def normalize(w):
    w={k:v for k,v in w.items() if v>.0001}
    s=sum(w.values())
    if s<.001: raise RuntimeError('Lost skin weight')
    return {k:v/s for k,v in w.items()}

def fix_cross(i):
    w=weight[i]
    side='R' if verts[i].co.x>=0 else 'L'
    arm={f'{side}_arm_upper',f'{side}_arm_lower',f'{side}_hand'}
    leg={f'{side}_leg_upper',f'{side}_leg_lower',f'{side}_foot',f'{side}_toe'}
    aw=sum(w.get(n,0) for n in arm)
    lw=sum(w.get(n,0) for n in leg)
    if aw>.52 and lw>0:
        for n in leg:w.pop(n,None)
    elif lw>.52 and aw>0:
        for n in arm:w.pop(n,None)
    weight[i]=normalize(w)

for i in range(len(verts)):
    fix_cross(i)

collar=set()
def fix_collar(i):
    co=verts[i].co
    if not (1.78<co.z<2.4 and abs(co.x)<.9 and abs(co.y)<.9):
        return False
    w=weight[i]
    side='R' if co.x>=0 else 'L'
    keys=(f'{side}_clavicle',f'{side}_arm_upper')
    arm=sum(w.get(k,0) for k in keys)
    if arm<.001:return False
    radius=max(abs(co.x),abs(co.y)*.50)
    outward=smoothstep(.14,.9,radius)
    upper=smoothstep(1.84,2.20,co.z)
    allowable=1-upper*(1-outward)
    # Above the collar, isolated shoulder weights must fade into the torso.
    allowable=max(0.0,min(1.0,allowable))
    if arm<=allowable:return True
    removed=arm-allowable
    scale=allowable/arm
    for k in keys:
        if k in w:w[k]*=scale
    neck_fraction=.24+.55*smoothstep(2.0,2.3,co.z)
    w['neck']=w.get('neck',0)+removed*neck_fraction
    w['spine_02']=w.get('spine_02',0)+removed*(1-neck_fraction)
    weight[i]=normalize(w)
    return True

for i in range(len(verts)):
    if fix_collar(i):collar.add(i)

neighbors=[set() for _ in verts]
for e in M.data.edges:
    a,b=e.vertices
    if (verts[a].co-verts[b].co).length<.075:
        neighbors[a].add(b);neighbors[b].add(a)

# A short geodesic relaxation replaces the almost discontinuous auto-weight
# band, without blurring the rest of the sculpted claws/antlers.
region={i for i in range(len(verts)) if 1.8<verts[i].co.z<2.33
        and abs(verts[i].co.x)<.9 and abs(verts[i].co.y)<.9}
for iteration in range(7):
    before=[dict(w) for w in weight]
    for i in region:
        ns=[j for j in neighbors[i] if j in region]
        if len(ns)<2:continue
        avg={}
        for j in ns:
            for k,v in before[j].items():avg[k]=avg.get(k,0)+v/len(ns)
        blend=.42
        target={k:(1-blend)*before[i].get(k,0)+blend*avg.get(k,0)
                for k in (set(before[i])|set(avg))}
        weight[i]=normalize(target)
    for i in region:fix_collar(i)

touched={i for i in range(len(verts)) if weight[i]!=original[i]}
for i in touched:
    for g in M.vertex_groups:g.remove([i])
    ordered=sorted(weight[i].items(),key=lambda kv:kv[1],reverse=True)[:4]
    total=sum(v for _,v in ordered)
    for name,value in ordered:M.vertex_groups[name].add([i],value/total,'REPLACE')

M.data.calc_loop_triangles()
assert len(M.data.loop_triangles)==24636
deform={b.name for b in R.data.bones if b.use_deform}
qa={'unweighted':0,'over_four':0,'unnormalized':0,'nondeform':0}
for v in verts:
    ws=[g.weight for g in v.groups if g.weight>.0001]
    if not ws:qa['unweighted']+=1
    if len(ws)>4:qa['over_four']+=1
    if abs(sum(ws)-1)>.005:qa['unnormalized']+=1
    if any(id_to_name[g.group] not in deform for g in v.groups if g.weight>.0001):qa['nondeform']+=1
assert not any(qa.values()),qa

dst=OUT/'ForestWendigo_Rig_WeightRevision_v4.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(dst),compress=True)
report={'source':'ForestWendigo_Rig_WeightRevision_v2.blend','blend':str(dst),
        'tris':len(M.data.loop_triangles),'bones':len(R.data.bones),
        'touched_vertices':len(touched),'collar_region':len(region),'qa':qa}
(OUT/'weight_revision_v4_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('WEIGHT_V4',json.dumps(report),flush=True)
