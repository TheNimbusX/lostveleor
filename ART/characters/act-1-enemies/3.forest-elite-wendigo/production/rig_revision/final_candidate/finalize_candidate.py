"""Remove the final four upper-arm/leg cross weights from isolated v6."""
import json
from pathlib import Path
import bpy

OUT=Path(__file__).resolve().parent
M=bpy.data.objects['SM_ForestWendigo_LOD0']
target_ids=(8532,9919,9960,10139)
for i in target_ids:
    v=M.data.vertices[i]
    ws={M.vertex_groups[g.group].name:g.weight for g in v.groups if g.weight>.0001}
    assert ws.get('L_leg_upper',0)>.1 and ws.get('L_arm_upper',0)>.5,(i,ws)
    ws.pop('L_leg_upper')
    s=sum(ws.values())
    for group in M.vertex_groups:group.remove([i])
    for name,val in ws.items():M.vertex_groups[name].add([i],val/s,'REPLACE')
M.data.calc_loop_triangles()
assert len(M.data.loop_triangles)==24636
dst=OUT/'ForestWendigo_Rig_SkinCandidate.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(dst),compress=True)
report={'source':'ForestWendigo_Rig_WeightRevision_v6.blend',
        'output':str(dst),'four_residual_arm_leg_weights_removed':list(target_ids),
        'triangles':len(M.data.loop_triangles)}
(OUT/'final_skin_candidate_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('FINAL_SKIN_CANDIDATE',json.dumps(report),flush=True)
