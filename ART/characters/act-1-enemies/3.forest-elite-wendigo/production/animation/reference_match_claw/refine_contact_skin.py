"""Locally soften the inherited hard head/neck skin boundary in this work file."""
import bpy,json
from pathlib import Path
HERE=Path(__file__).resolve().parent
m=bpy.context.scene.objects['SM_ForestWendigo_LOD0']
source=json.loads((HERE/'fit_source.json').read_text())
audit=json.loads((HERE/'deformation_audit.json').read_text())
seed=set()
for row in audit:
 for ia,ib,ratio,co in row['worst']:
  if co[2]>2.1 and ratio>5:seed.update([ia,ib])
adj=[set() for _ in m.data.vertices]
for e in m.data.edges:
 a,b=e.vertices;adj[a].add(b);adj[b].add(a)
region=set(seed)
for _ in range(3):region|={j for i in region for j in adj[i]}
weights=[dict(gs) for gs in source['weights']]
for iteration in range(10):
 updates={}
 for i in region:
  old=weights[i];out={k:v*.3 for k,v in old.items()}
  for j in adj[i]:
   for k,v in weights[j].items():out[k]=out.get(k,0)+.7*v/len(adj[i])
  updates[i]=out
 for i,w in updates.items():weights[i]=w
for i in region:
 w=weights[i];total=sum(w.values())
 for g in list(m.data.vertices[i].groups):m.vertex_groups[g.group].remove([i])
 for n,value in w.items():
  if value/total>.0001:m.vertex_groups[n].add([i],value/total,'REPLACE')
 source['weights'][i]=[[n,v/total] for n,v in w.items() if v/total>.0001]
(HERE/'fit_source.json').write_text(json.dumps(source))
(HERE/'skin_refinement.json').write_text(json.dumps({'vertices':sorted(region),'count':len(region),'reason':'continuous head/neck transition; no geometry changes'},indent=2))
