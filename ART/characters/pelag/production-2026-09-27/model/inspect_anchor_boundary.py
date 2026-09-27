import bpy,bmesh,json
from pathlib import Path
from mathutils import Vector
OUT=Path(r'C:/Users/d.grab/Desktop/the-game/ART/characters/pelag/production-2026-09-27/model')
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Pelag_Model_Candidate.blend'))
o=bpy.data.objects['SOURCE_anchor'];bm=bmesh.new();bm.from_mesh(o.data)
remaining=set(e for e in bm.edges if e.is_boundary);comps=[]
while remaining:
 edges={remaining.pop()};front=list(edges)
 while front:
  e=front.pop()
  for v in e.verts:
   for n in v.link_edges:
    if n in remaining:remaining.remove(n);edges.add(n);front.append(n)
 vs=set(v for e in edges for v in e.verts);mid=sum((v.co for v in vs),Vector())/len(vs)
 comps.append({'edges':len(edges),'center':list(mid),'bounds':[[min(v.co[i] for v in vs) for i in range(3)],[max(v.co[i] for v in vs) for i in range(3)]]})
print('BOUNDARIES',json.dumps(comps));(OUT/'anchor_boundaries.json').write_text(json.dumps(comps,indent=2))
