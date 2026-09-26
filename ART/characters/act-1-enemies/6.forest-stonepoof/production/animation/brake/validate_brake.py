import bpy,json,math,hashlib
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;bpy.ops.wm.open_mainfile(filepath=str(HERE/'Stonehoof_Brake_r01.blend'))
scene=bpy.context.scene;arm=bpy.data.objects['ARM_ForestStonehoof'];mesh=bpy.data.objects['SM_ForestStonehoof_LOD0'];build=json.loads((HERE/'build.json').read_text());contacts=build['contacts'];soles={};samples=[];prev={};max_turn=(0,None,None)
for tag in contacts:
 idx=mesh.vertex_groups['leg_'+tag+'_bot2'].index;ids=[v.index for v in mesh.data.vertices if any(g.group==idx and g.weight>.99 for g in v.groups)];z=min(mesh.data.vertices[i].co.z for i in ids);soles[tag]=[i for i in ids if mesh.data.vertices[i].co.z<z+.008]
times=sorted(set([i/8 for i in range(145)]+list(contacts.values())))
for f in times:
 scene.frame_set(math.floor(f),subframe=f-math.floor(f));bpy.context.view_layer.update();ev=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get());m=ev.to_mesh();row={'frame':f,'mesh_min_z':min((ev.matrix_world@v.co).z for v in m.vertices),'legs':{},'length_error':0}
 for tag in contacts:
  b=arm.pose.bones['leg_'+tag+'_bot2'];target=bpy.data.objects['CTRL_Hoof_'+tag];pts=[ev.matrix_world@m.vertices[i].co for i in soles[tag]]
  row['legs'][tag]={'target_error':((arm.matrix_world@b.head)-target.matrix_world.translation).length,'sole':list(sum(pts,Vector())/len(pts)),'min_z':min(p.z for p in pts),'clearance':target['sole_clearance']}
 for b in arm.pose.bones:
  if not b.bone.use_deform:continue
  row['length_error']=max(row['length_error'],abs((b.tail-b.head).length/b.bone.length-1));q=b.matrix.to_quaternion().normalized()
  if b.name in prev:
   angle=math.degrees(2*math.acos(min(1,abs(q.dot(prev[b.name])))))
   if angle>max_turn[0]:max_turn=(angle,f,b.name)
  prev[b.name]=q
 samples.append(row);ev.to_mesh_clear()
drift={};vertical={}
for tag in contacts:
 part=[s for s in samples if s['frame']>=14];origin=Vector(part[0]['legs'][tag]['sole']);drift[tag]=max((Vector(s['legs'][tag]['sole'])-origin).length for s in part)
 part=[s for s in samples if s['frame']>=contacts[tag]];vertical[tag]=[min(s['legs'][tag]['min_z'] for s in part),max(s['legs'][tag]['min_z'] for s in part)]
report={'samples':len(samples),'maximum_hoof_target_error_m':{tag:max((s['legs'][tag]['target_error'],s['frame']) for s in samples) for tag in contacts},'post_stop_support_drift_m':drift,'skid_sole_z_ranges_m':vertical,'minimum_mesh_z_m':min((s['mesh_min_z'],s['frame']) for s in samples),'bone_length_error':max(s['length_error'] for s in samples),'max_rotation_step_eighth_frame':max_turn,'joins':build['joins'],'accepted_sources_unchanged':all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in build['source_sha256'].items()),'owner_approved':False,'detail':samples}
(HERE/'validation.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in report.items() if k!='detail'}))
