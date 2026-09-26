import bpy,json
from pathlib import Path
HERE=Path(__file__).resolve().parent;bpy.ops.wm.open_mainfile(filepath=str(HERE/'Stonehoof_Death_r02.blend'))
bpy.context.scene.frame_set(48);a=bpy.data.objects['ARM_ForestStonehoof'];m=bpy.data.objects['SM_ForestStonehoof_LOD0'];ev=m.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();groups={}
for v in m.data.vertices:
    g=max(v.groups,key=lambda g:g.weight);name=m.vertex_groups[g.group].name
    if name not in groups or me.vertices[v.index].co.z<groups[name][0]:groups[name]=[me.vertices[v.index].co.z,v.index,list(me.vertices[v.index].co)]
print(json.dumps({'groups':groups,'root_local':list(a.pose.bones['body'].location)},indent=2));ev.to_mesh_clear()
