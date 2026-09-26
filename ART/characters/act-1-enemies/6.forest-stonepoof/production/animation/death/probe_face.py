import bpy,math,json
from pathlib import Path
from mathutils import Quaternion
p=Path(__file__).parent;bpy.ops.wm.open_mainfile(filepath=str(p/'Stonehoof_Death_r01.blend'));s=bpy.context.scene;s.frame_set(48);a=bpy.data.objects['ARM_ForestStonehoof'];m=bpy.data.objects['SM_ForestStonehoof_LOD0'];out=p/'face_probes';out.mkdir(exist_ok=True)
for i,(neck,head) in enumerate(((-6,0),(-20,40),(10,-20),(20,-30),(25,-25))):
 for name,angle in (('neck0',neck),('head0',head)):
  b=a.pose.bones[name];r=b.bone.matrix_local.to_quaternion();b.rotation_quaternion=r.inverted()@Quaternion((1,0,0),math.radians(angle))@r;b.keyframe_insert('rotation_quaternion',frame=48)
 bpy.context.view_layer.update();s.render.filepath=str(out/f'{i}.png');bpy.ops.render.render(write_still=True)
