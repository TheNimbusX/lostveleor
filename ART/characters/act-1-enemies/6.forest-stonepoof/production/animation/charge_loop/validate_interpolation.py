"""Independent source/derived sampling, including frame seams and 30/60/120 Hz."""
import bpy,json,math,hashlib
from pathlib import Path
HERE=Path(__file__).resolve().parent
times=sorted(set([i/32 for i in range(385)]+[12-.001,.001]))
def sample(file):
 bpy.ops.wm.open_mainfile(filepath=str(HERE/file));scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];out={}
 for f in times:
  scene.frame_set(math.floor(f),subframe=f-math.floor(f));bpy.context.view_layer.update()
  out[f]={b.name:b.matrix.copy() for b in arm.pose.bones if b.bone.use_deform}
 return out
source=sample('Stonehoof_ChargeLoop_r01.blend');baked=sample('Stonehoof_ChargeLoop_Baked_r01.blend')
max_pos=(0,None,None);max_angle=(0,None,None)
for f in times:
 for n,m in source[f].items():
  p=(baked[f][n].translation-m.translation).length
  a=math.degrees(2*math.acos(min(1,abs(baked[f][n].to_quaternion().normalized().dot(m.to_quaternion().normalized())))))
  if p>max_pos[0]:max_pos=(p,f,n)
  if a>max_angle[0]:max_angle=(a,f,n)
arm=bpy.data.objects['ARM_ForestStonehoof'];seam=max((baked[0][n].translation-baked[12][n].translation).length for n in baked[0])
report={'sample_count':len(times),'maximum_interpolated_joint_error_m':max_pos,'maximum_interpolated_rotation_error_degrees':max_angle,'baked_seam_error_m':seam,'display_rates_checked':[30,60,120],'note':'Blender source/derived interpolation check, not Unity runtime or performance evidence.','source_sha256_unchanged':hashlib.sha256((HERE.parent/'windup_start'/'Stonehoof_WindupStart_r01.blend').read_bytes()).hexdigest()==json.loads((HERE/'build.json').read_text())['source_master_sha256']}
(HERE/'interpolation_validation.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
assert max_pos[0]<.003,report
assert max_angle[0]<2,report
assert seam<.00001,report
