"""Broaden the collar/upper torso skin gradient after targeted v6 repair."""
import json
from pathlib import Path
import bpy

OUT=Path(__file__).resolve().parent
M=bpy.data.objects['SM_ForestWendigo_LOD0']
R=bpy.data.objects['ARM_ForestWendigo']
verts=M.data.vertices
names={g.index:g.name for g in M.vertex_groups}
weights=[{names[g.group]:g.weight for g in v.groups if g.weight>.0001} for v in verts]
neighbors=[set() for _ in verts]
for e in M.data.edges:
    a,b=e.vertices
    if (verts[a].co-verts[b].co).length<.065:
        neighbors[a].add(b);neighbors[b].add(a)
region={i for i,v in enumerate(verts) if 1.57<v.co.z<2.39 and
        abs(v.co.x)<.86 and abs(v.co.y)<.86}
changed=set()

def step(lo,hi,x):
    u=max(0.,min(1.,(x-lo)/(hi-lo)))
    return u*u*(3-2*u)

for _ in range(12):
    before=[dict(w) for w in weights]
    for i in region:
        ns=[j for j in neighbors[i] if j in region]
        if len(ns)<3:continue
        v=verts[i]
        fade=step(1.57,1.72,v.co.z)*(1-step(2.28,2.39,v.co.z))
        mix=.34*fade
        if mix<.001:continue
        avg={}
        for j in ns:
            for name,val in before[j].items():avg[name]=avg.get(name,0)+val/len(ns)
        merged={k:(1-mix)*before[i].get(k,0)+mix*avg.get(k,0)
                for k in set(before[i])|set(avg)}
        top=sorted(merged.items(),key=lambda pair:pair[1],reverse=True)[:4]
        s=sum(val for _,val in top)
        weights[i]={k:val/s for k,val in top}
        changed.add(i)

for i in changed:
    for g in M.vertex_groups:g.remove([i])
    for name,val in weights[i].items():M.vertex_groups[name].add([i],val,'REPLACE')

target=OUT/'ForestWendigo_Rig_WeightRevision_v7.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(target),compress=True)
report={'source':'v6','output':str(target),'changed':len(changed),'triangles':len(M.data.loop_triangles)}
(OUT/'weight_revision_v7_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('SMOOTH_V7',json.dumps(report),flush=True)
