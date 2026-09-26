"""Compare actual skin proportions in a frame attached to the rigid body root."""
import bpy,json,math
import numpy as np
from pathlib import Path
HERE=Path(__file__).resolve().parent
def measure(file):
 bpy.ops.wm.open_mainfile(filepath=str(file));s=bpy.context.scene;a=bpy.data.objects['ARM_ForestStonehoof'];m=bpy.data.objects['SM_ForestStonehoof_LOD0']
 torso_groups={'body','body_top0','body_top1','body_bot','pelvis'}
 torso=[v.index for v in m.data.vertices if sum(g.weight for g in v.groups if m.vertex_groups[g.group].name in torso_groups)>.80]
 edges=[tuple(e.vertices) for e in m.data.edges if all(v in set(torso) for v in e.vertices)]
 rows=[];base=None;previous=None
 for f in range(61):
  s.frame_set(f);bpy.context.view_layer.update();ev=m.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
  transform=a.pose.bones['body'].matrix@a.data.bones['body'].matrix_local.inverted();inv=transform.inverted()
  points=np.array([list(inv@mesh.vertices[i].co) for i in torso]);lengths=np.array([(mesh.vertices[x].co-mesh.vertices[y].co).length for x,y in edges]);ev.to_mesh_clear()
  bbox=points.max(axis=0)-points.min(axis=0)
  if base is None:base=(bbox,lengths)
  ratios=lengths/np.maximum(base[1],1e-8)
  rows.append({'frame':f,'torso_extent_ratio':(bbox/base[0]).tolist(),'torso_edges_p01_p99':np.quantile(ratios,[.01,.99]).tolist(),'maximum_edge_compression':float(1-ratios.min())})
 scales=[fc.data_path for layer in a.animation_data.action.layers for strip in layer.strips for bag in strip.channelbags for fc in bag.fcurves if fc.data_path.endswith('.scale')]
 return {'torso_vertices':len(torso),'torso_edges':len(edges),'extent_ratio_min':np.array([r['torso_extent_ratio'] for r in rows]).min(axis=0).tolist(),'extent_ratio_max':np.array([r['torso_extent_ratio'] for r in rows]).max(axis=0).tolist(),'scale_channels':scales,'detail':rows}
result={'rejected_r01':measure(HERE.parent/'death'/'Stonehoof_Death_r01.blend'),'candidate_r02':measure(HERE/'Stonehoof_Death_r02.blend')}
(HERE/'body_mass_validation.json').write_text(json.dumps(result,indent=2));print(json.dumps({k:{n:v for n,v in r.items() if n!='detail'} for k,r in result.items()}))

assert result["candidate_r02"]["extent_ratio_min"][1]>.98
assert result["candidate_r02"]["extent_ratio_max"][1]<1.02
assert not result["candidate_r02"]["scale_channels"]
