import bpy
from pathlib import Path
from mathutils import Vector
p=Path(__file__).parent;out=p.parents[1]/'review'/'animation_death';bpy.ops.wm.open_mainfile(filepath=str(p/'Stonehoof_Death_r01.blend'));s=bpy.context.scene;cam=s.camera
for view in ('side','game'):
 target=Vector((0,-.03,.6));offset=Vector((8,-1,2.6)) if view=='side' else Vector((6,-7,7));cam.location=target+offset;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=3.4
 for f in range(61):
  s.frame_set(f);s.render.filepath=str(out/(view+'_frames')/f'{f:04d}.png');bpy.ops.render.render(write_still=True)
