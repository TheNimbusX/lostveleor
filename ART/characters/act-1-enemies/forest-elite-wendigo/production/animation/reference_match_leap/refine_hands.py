import bpy,json
from pathlib import Path
HERE=Path(__file__).resolve().parent
source=json.loads((HERE/'fit_source.json').read_text())
mesh=bpy.context.scene.objects['SM_ForestWendigo_LOD0'];changed=[]
for v,entries in zip(mesh.data.vertices,source['weights']):
 old=dict(entries)
 for side in ['R','L']:
  lower=side+'_arm_lower';hand=side+'_hand';total=old.get(lower,0)+old.get(hand,0)
  if total<.95 or v.co.z>1.22:continue
  t=max(0.0,min(1.0,(1.22-v.co.z)/.15));t=t*t*(3-2*t)
  target=old.get(hand,0)+(total-old.get(hand,0))*t
  if target-old.get(hand,0)<1e-6:continue
  mesh.vertex_groups[hand].add([v.index],target,'REPLACE')
  mesh.vertex_groups[lower].add([v.index],total-target,'REPLACE')
  old[hand]=target;old[lower]=total-target
  source['weights'][v.index]=[[n,w] for n,w in old.items() if w>1e-7];changed.append(v.index)
(HERE/'fit_source.json').write_text(json.dumps(source))
(HERE/'hand_skin_audit.json').write_text(json.dumps({'changed_vertices':len(changed),'why':'Claw tips inherited forearm weights, prevented a clean planted palm. Changes isolated to Leap source.','triangle_count':sum(len(p.vertices)-2 for p in mesh.data.polygons)},indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(HERE/'Leap_Reference_Work.blend'))
