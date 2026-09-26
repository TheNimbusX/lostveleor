import bpy,json
from pathlib import Path
HERE=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(HERE.parent/'windup_start'/'Stonehoof_WindupStart_r01.blend'))
bpy.context.scene.frame_set(0);a=bpy.data.objects['ARM_ForestStonehoof']
print('RIG',json.dumps({b.name:{'head':list(b.head),'tail':list(b.tail)} for b in a.pose.bones if b.bone.use_deform}))
