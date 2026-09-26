"""Read-only probe of the stance rig: carrier rotation mode, hip heights, leg reach."""
import bpy
from pathlib import Path
HERE=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(HERE.parent/'windup_start'/'Stonehoof_WindupStart_r01.blend'))
s=bpy.context.scene;s.frame_set(0);bpy.context.view_layer.update()
arm=bpy.data.objects['ARM_ForestStonehoof'];car=bpy.data.objects['CTRL_PreviewMotion_Only']
print('CARRIER',car.rotation_mode,tuple(car.rotation_euler),tuple(car.location),'arm parent',arm.parent and arm.parent.name)
for t in ('front_left','front_right','hind_left','hind_right'):
    u=arm.pose.bones['MCH_Upper_'+t];l=arm.pose.bones['MCH_Lower_'+t];f=bpy.data.objects['CTRL_Hoof_'+t]
    print('LEG',t,'hip',tuple(round(x,3) for x in u.head),'len',round(u.length+l.length,3),'dist',round((l.tail-u.head).length,3),'foot',tuple(round(x,3) for x in f.location),f.rotation_mode,'parent',f.parent.name,[c.type for c in l.constraints])
print('ACTIONS',[a.name for a in bpy.data.actions])
