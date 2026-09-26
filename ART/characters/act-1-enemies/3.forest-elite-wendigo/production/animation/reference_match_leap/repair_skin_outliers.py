"""Удаляет ошибочные изолированные веса на зашитых участках исходного меша."""
import bpy,json,numpy as np
from pathlib import Path
HERE=Path(__file__).resolve().parent
m=bpy.context.scene.objects['SM_ForestWendigo_LOD0'];data=json.loads((HERE/'fit_source.json').read_text())
names=[b['name'] for b in data['bones']];xyz=np.array([v.co[:] for v in m.data.vertices]);edges=np.array([list(e.vertices) for e in m.data.edges]);W=np.zeros((len(xyz),len(names)))
for i,gs in enumerate(data['weights']):
 for n,w in gs:
  if n in names:W[i,names.index(n)]=w
original=W.copy();adj=[set() for _ in xyz]
for a,b in edges:adj[a].add(int(b));adj[b].add(int(a))
neighborhood=[]
for i in range(len(xyz)):
 near=set(adj[i])
 for n in list(near):near.update(adj[n])
 near.discard(i);near=[n for n in near if np.linalg.norm(xyz[n]-xyz[i])<.065]
 neighborhood.append(near)
repairs=set()
for _ in range(4):
 updated=W.copy()
 for i,near in enumerate(neighborhood):
  if len(near)<3:continue
  median=np.median(W[near],axis=0)
  if median.sum()<.2:continue
  median/=median.sum()
  if np.abs(median-W[i]).sum()>.65:updated[i]=median;repairs.add(i)
 W=updated
short=np.linalg.norm(xyz[edges[:,0]]-xyz[edges[:,1]],axis=1)<.02
bad=short & (np.abs(W[edges[:,0]]-W[edges[:,1]]).sum(axis=1)>.4)
smooth=set(edges[bad].ravel().tolist())
for n in list(smooth):smooth.update(adj[n])
for _ in range(8):
 updated=W.copy()
 for i in smooth:
  near=list(adj[i])
  if near:updated[i]=W[i]*.6+W[near].mean(axis=0)*.4
 W=updated
changed=np.where(np.abs(W-original).sum(axis=1)>1e-5)[0]
for i in changed:
 for g in list(m.data.vertices[int(i)].groups):m.vertex_groups[g.group].remove([int(i)])
 entries=[]
 for b,w in enumerate(W[i]):
  if w>1e-6:m.vertex_groups[names[b]].add([int(i)],float(w),'REPLACE');entries.append([names[b],float(w)])
 data['weights'][int(i)]=entries
(HERE/'fit_source.json').write_text(json.dumps(data))
(HERE/'skin_outlier_audit.json').write_text(json.dumps({'isolated_outliers':len(repairs),'smoothed_vertices':len(changed),'geometry_unchanged':True},indent=2))
bpy.context.scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Leap_Reference_Work.blend'))
