import bpy,json,math
from mathutils import Quaternion
from pathlib import Path
p=Path(r'C:/Users/d.grab/Desktop/the-game/ART/characters/act-1-enemies/forest-stonepoof/production/animation')
bpy.ops.wm.open_mainfile(filepath=str(p/'charge_loop/Stonehoof_ChargeLoop_r01.blend'));s=bpy.context.scene;s.frame_set(0);a=bpy.data.objects['ARM_ForestStonehoof'];m=bpy.data.objects['SM_ForestStonehoof_LOD0'];a.animation_data_clear()
for tag in ['front_left','front_right']:
 o=bpy.data.objects['CTRL_Hoof_'+tag];o.animation_data_clear();o.location.y+=.23
for neck,head in [(10,10),(16,12),(20,15),(25,15),(30,15),(35,10)]:
 for name,angle in [('neck0',neck),('head0',head)]:
  b=a.pose.bones[name];q=a.data.bones[name].matrix_local.to_quaternion();b.rotation_quaternion=q.inverted()@Quaternion((1,0,0),math.radians(angle))@q
 bpy.context.view_layer.update();ev=m.evaluated_get(bpy.context.evaluated_depsgraph_get());me=ev.to_mesh();idx=min(range(len(me.vertices)),key=lambda i:me.vertices[i].co.y);top=min([v.index for v in m.data.vertices if v.co.z>.8 and v.co.y<-.4],key=lambda i:me.vertices[i].co.y);print(json.dumps({'neck':neck,'head':head,'leading_vertex':idx,'evaluated':list(me.vertices[idx].co),'rest':list(m.data.vertices[idx].co),'forehead':list(me.vertices[top].co),'forehead_rest':list(m.data.vertices[top].co)}));ev.to_mesh_clear()
