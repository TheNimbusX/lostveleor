import bpy,json,math
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(HERE/'Stonehoof_ChargeLoop_r01.blend'))
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0'];carrier=bpy.data.objects['CTRL_PreviewMotion_Only']
build=json.loads((HERE/'build.json').read_text());contacts=build['contacts'];soles={};samples=[];prev={};max_turn=(0,None,None)
for tag in contacts:
 idx=mesh.vertex_groups['leg_'+tag+'_bot2'].index
 ids=[v.index for v in mesh.data.vertices if any(g.group==idx and g.weight>.99 for g in v.groups)]
 z=min(mesh.data.vertices[i].co.z for i in ids);soles[tag]=[i for i in ids if mesh.data.vertices[i].co.z<z+.008]
times=sorted(set([i/8 for i in range(97)]+[float(f) for c in contacts.values() for f in c]))
seams={}
for f in times:
 scene.frame_set(math.floor(f),subframe=f-math.floor(f));carrier.location=(0,-.4*f,0);bpy.context.view_layer.update()
 ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());m=ev.to_mesh()
 row={'frame':f,'mesh_min_z':min((ev.matrix_world@v.co).z for v in m.vertices),'legs':{},'length_error':0}
 for tag in contacts:
    b=arm.pose.bones['leg_'+tag+'_bot2'];target=bpy.data.objects['CTRL_Hoof_'+tag]
    pts=[ev.matrix_world@m.vertices[i].co for i in soles[tag]]
    row['legs'][tag]={'target_error':((arm.matrix_world@b.head)-target.matrix_world.translation).length,'sole':list(sum(pts,Vector())/len(pts)),'min_z':min(p.z for p in pts)}
 for b in arm.pose.bones:
    if not b.bone.use_deform:continue
    row['length_error']=max(row['length_error'],abs((b.tail-b.head).length/b.bone.length-1))
    q=b.matrix.to_quaternion().normalized()
    if b.name in prev:
        angle=math.degrees(2*math.acos(min(1,abs(q.dot(prev[b.name])))))
        if angle>max_turn[0]:max_turn=(angle,f,b.name)
    prev[b.name]=q
 if f in (0,12):seams[f]={b.name:b.matrix.copy() for b in arm.pose.bones if b.bone.use_deform}
 samples.append(row);ev.to_mesh_clear()
drift={}
for tag,(start,end) in contacts.items():
 part=[s for s in samples if start-1e-6<=s['frame']<=end+1e-6];origin=Vector(part[0]['legs'][tag]['sole'])
 drift[tag]=max((Vector(s['legs'][tag]['sole'])-origin).length for s in part)
report={'samples':len(samples),'maximum_hoof_target_error_m':{tag:max((s['legs'][tag]['target_error'],s['frame']) for s in samples) for tag in contacts},'world_plant_drift_m_at_12m_s':drift,'minimum_mesh_z_m':min((s['mesh_min_z'],s['frame']) for s in samples),'bone_length_error':max(s['length_error'] for s in samples),'max_rotation_step_eighth_frame':max_turn,'loop_seam_position_m':max((seams[0][n].translation-seams[12][n].translation).length for n in seams[0]),'loop_seam_matrix_error':max(abs(seams[0][n][i][j]-seams[12][n][i][j]) for n in seams[0] for i in range(4) for j in range(4)),'launch_join_position_m':max(x['position_m'] for x in build['launch_join'].values()),'owner_approved':False,'detail':samples}
(HERE/'validation.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='detail'}))
