import bpy,json,math
from mathutils import Quaternion
from pathlib import Path
p=Path(r'C:/Users/d.grab/Desktop/the-game/ART/characters/act-1-enemies/forest-stonepoof/production/animation')
bpy.ops.wm.open_mainfile(filepath=str(p/'charge_loop/Stonehoof_ChargeLoop_r01.blend'));s=bpy.context.scene;s.frame_set(0);a=bpy.data.objects['ARM_ForestStonehoof'];m=bpy.data.objects['SM_ForestStonehoof_LOD0'];a.animation_data_clear()
for angle in [0,10,20,25,30,35]:
 b=a.pose.bones['head0'];q=a.data.bones['head0'].matrix_local.to_quaternion();b.rotation_quaternion=q.inverted()@Quaternion((1,0,0),math.radians(angle))@q;bpy.context.view_layer.update();ev=m.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();idx=min(range(len(me.vertices)),key=lambda i:me.vertices[i].co.y);print(json.dumps({'head_angle':angle,'leading_vertex':idx,'evaluated':list(me.vertices[idx].co),'rest':list(m.data.vertices[idx].co)}));ev.to_mesh_clear()
