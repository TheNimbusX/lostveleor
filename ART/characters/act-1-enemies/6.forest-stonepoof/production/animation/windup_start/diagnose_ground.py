import bpy,json
from pathlib import Path
p=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(p/'Stonehoof_WindupStart_r01.blend'))
s=bpy.context.scene;s.frame_set(19,subframe=.5)
o=bpy.data.objects['SM_ForestStonehoof_LOD0']; ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get()); m=ev.to_mesh()
ids=sorted(range(len(m.vertices)),key=lambda i:(ev.matrix_world@m.vertices[i].co).z)[:5]
print(json.dumps([{'id':i,'co':list(m.vertices[i].co),'rest':list(o.data.vertices[i].co),'weights':[(o.vertex_groups[g.group].name,g.weight) for g in o.data.vertices[i].groups]} for i in ids]))
