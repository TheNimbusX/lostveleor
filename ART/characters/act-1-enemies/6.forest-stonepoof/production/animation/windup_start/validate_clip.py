import bpy,json,math
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(HERE/'Stonehoof_WindupStart_r01.blend'))
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0']
build=json.loads((HERE/'rig_build.json').read_text());data=[]
for i in range(145):
    f=i/4;scene.frame_set(int(f),subframe=f-int(f));bpy.context.view_layer.update()
    ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());m=ev.to_mesh()
    record={'frame':f,'legs':{},'max_length_error':0}
    record['mesh_low_z']=min((ev.matrix_world@v.co).z for v in m.vertices)
    for tag,info in build['legs'].items():
        name='leg_'+tag+'_bot2';p=arm.pose.bones[name];target=bpy.data.objects['CTRL_Hoof_'+tag]
        sole=[ev.matrix_world@m.vertices[j].co for j in info['sole_ids']]
        record['legs'][tag]={'target_error':((arm.matrix_world@p.head)-target.matrix_world.translation).length,'sole_center':list(sum(sole,Vector())/len(sole)),'sole_min_z':min(v.z for v in sole)}
    for p in arm.pose.bones:
        if p.bone.use_deform:record['max_length_error']=max(record['max_length_error'],abs((p.tail-p.head).length/p.bone.length-1))
    data.append(record);ev.to_mesh_clear()
maximum_error={tag:max((s['legs'][tag]['target_error'],s['frame']) for s in data) for tag in build['legs']}
drift={}
for tag in ('front_right','hind_left','hind_right'):
    first=Vector(data[0]['legs'][tag]['sole_center']);drift[tag]=max((Vector(s['legs'][tag]['sole_center'])-first).length for s in data if s['frame']<=30)
report={'samples':len(data),'maximum_hoof_target_error_m_and_frame':maximum_error,'stationary_support_drift_m':drift,'minimum_mesh_z_m':min((s['mesh_low_z'],s['frame']) for s in data),'maximum_bone_length_relative_error':max(s['max_length_error'] for s in data),'owner_approved':False,'samples_detail':data}
(HERE/'validation.json').write_text(json.dumps(report,indent=2))
print(json.dumps({k:v for k,v in report.items() if k!='samples_detail'}))
