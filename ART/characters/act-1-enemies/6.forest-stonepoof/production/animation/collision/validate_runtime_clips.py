"""Check the trimmed runtime actions independently of the full review action."""
import bpy,json,math
from pathlib import Path
HERE=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(HERE/'Stonehoof_Collision_r01.blend'))
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof']
names=[b.name for b in arm.pose.bones if b.bone.use_deform]
expected={}
for action,start,end in [('AN_Stonehoof_WallBrace',8,12),('AN_Stonehoof_WallImpact',12,48)]:
    rows=[]
    for i in range((end-start)*8+1):
        f=start+i/8;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
        rows.append((f-start,{n:arm.pose.bones[n].matrix.copy() for n in names}))
    expected[action]=rows
bpy.ops.wm.open_mainfile(filepath=str(HERE/'Stonehoof_Collision_Baked_r01.blend'))
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof']
for track in arm.animation_data.nla_tracks:track.mute=True
results={}
for name,rows in expected.items():
    action=bpy.data.actions[name];arm.animation_data.action=action
    positions=[];angles=[]
    for f,pose in rows:
        scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
        for n,m in pose.items():
            actual=arm.pose.bones[n].matrix
            positions.append((actual.translation-m.translation).length)
            dot=abs(actual.to_quaternion().normalized().dot(m.to_quaternion().normalized()))
            angles.append(math.degrees(2*math.acos(min(1,dot))))
    results[name]={'frames':list(action.frame_range),'samples':len(rows),'max_joint_error_m':max(positions),'max_rotation_error_deg':max(angles)}
    assert max(positions)<.001,results[name]
results['no_root_carrier']=arm.parent is None and arm.matrix_world.translation.length<1e-6
results['note']='Blender actions only. Arbitrary incoming gait phases and Unity collision timing remain integration checks.'
assert results['no_root_carrier']
(HERE/'runtime_validation.json').write_text(json.dumps(results,indent=2))
print(json.dumps(results))
