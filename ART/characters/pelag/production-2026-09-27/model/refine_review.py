import bpy,math,json,bmesh,hashlib
from pathlib import Path
from mathutils import Vector,Matrix
from mathutils.bvhtree import BVHTree
ROOT=Path(r'C:/Users/d.grab/Desktop/the-game');OUT=ROOT/'ART/characters/pelag/production-2026-09-27/model'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Pelag_Model_Candidate.blend'))
scene=bpy.context.scene;hero=bpy.data.collections['COL_Pelag_Candidate'];originals=bpy.data.collections['COL_Weapon_Originals']
rig=bpy.data.objects['ARM_Pelag_v6_Original65'];body=bpy.data.objects['SM_Pelag_v6_Unchanged']
report=json.loads((OUT/'candidate_report.json').read_text())
before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_AN_CombatIdle.fbx'),use_anim=True)
src=next(o for o in set(bpy.data.objects)-before if o.type=='ARMATURE')
scene.frame_set(1);bpy.context.view_layer.update()
# Existing combat idle frame, not a newly authored animation. Preserve target joint lengths.
for pb in rig.pose.bones:pb.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
for pb in rig.pose.bones:
 sp=src.pose.bones.get(pb.name)
 if not sp:continue
 m=sp.matrix.to_quaternion().to_matrix().to_4x4();m.translation=pb.head.copy();pb.matrix=m;bpy.context.view_layer.update()
for o in list(set(bpy.data.objects)-before):bpy.data.objects.remove(o,do_unlink=True)
report['review_pose']={'source':'razlom/Assets/Resources/Characters/Pelag_v5/Mixamo/Pelag_AN_CombatIdle.fbx','frame':1,'method':'existing pose world rotations, target original joint lengths; no animation keys authored'}
anchor=bpy.data.objects['SM_Pelag_anchor_Candidate'];saber=bpy.data.objects['SM_Pelag_saber_Candidate'];grip=bpy.data.objects['SM_Pelag_grip_Candidate'];link=bpy.data.objects['SM_Pelag_link_Candidate']
def bounds(o):
 pts=[o.matrix_world@v.co for v in o.data.vertices];return Vector([min(v[i] for v in pts) for i in range(3)]),Vector([max(v[i] for v in pts) for i in range(3)])
def select(obs):
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 bpy.context.view_layer.objects.active=obs[0]
def tris(o):o.data.calc_loop_triangles();return len(o.data.loop_triangles)
def parent_bone_keep(o,bone):
 bpy.context.view_layer.update();w=o.matrix_world.copy();o.parent=rig;o.parent_type='BONE';o.parent_bone=bone;bpy.context.view_layer.update();o.matrix_world=w;bpy.context.view_layer.update()
anchor.parent=None;anchor.rotation_euler=(math.atan2(.55,.835),0,math.radians(-12));anchor.location=(0,0,0);anchor.scale=(.94/.49038118124,)*3
bpy.context.view_layer.update();lo,hi=bounds(anchor);anchor.location+=Vector((0,.34,1.065))-(lo+hi)*.5
bpy.context.view_layer.update()
# Actual cut shaft center, measured from its 44 boundary vertices (not mesh origin).
top_local=Vector((-.0019656515,.096264005,-.006767678))
top=anchor.matrix_world@top_local
shaft_axis=(anchor.rotation_euler.to_matrix()@Vector((0,.55,.835))).normalized()
for o in list(hero.objects):
 if o.name.startswith('SM_Pelag_CarryLink'):bpy.data.objects.remove(o,do_unlink=True)
# Anchor shaft has an open cut from the historic split; close only its top boundary.
bm=bmesh.new();bm.from_mesh(anchor.data)
before_open=sum(e.is_boundary for e in bm.edges)
top_edges=[e for e in bm.edges if e.is_boundary and all(v.co.z>-.015 for v in e.verts)]
filled=bmesh.ops.holes_fill(bm,edges=top_edges,sides=0).get('faces',[]) if top_edges else []
uv=bm.loops.layers.uv.active
for f in filled:
 if uv:
  for loop in f.loops:loop[uv].uv=(.405,.76)
bm.to_mesh(anchor.data);bm.free();anchor.data.update()
report['anchor']['top_boundary_edges_closed']=len(top_edges);report['anchor']['candidate']=tris(anchor)
parent_bone_keep(anchor,'mixamorig:Spine2')
socket=bpy.data.objects['SOCKET_Anchor_Back'];socket.matrix_world=anchor.matrix_world.copy()
eye=bpy.data.objects.get('SM_Pelag_AnchorEye')
if eye:bpy.data.objects.remove(eye,do_unlink=True)
eye=bpy.data.objects.new('SM_Pelag_AnchorEye',link.data);hero.objects.link(eye);eye.location=top+shaft_axis*.048;eye.rotation_mode='QUATERNION';eye.rotation_quaternion=shaft_axis.to_track_quat('Z','Y');eye.scale=(.8,)*3;parent_bone_keep(eye,'mixamorig:Spine2')
hand=rig.matrix_world@rig.pose.bones['mixamorig:RightHand'].head
palm=hand.lerp(rig.matrix_world@rig.pose.bones['mixamorig:RightHandMiddle1'].head,.6)
saber.rotation_euler=(math.radians(127),0,math.radians(-8));saber.scale=(.80,)*3
bpy.context.view_layer.update();direction=saber.rotation_euler.to_matrix()@Vector((0,1,0));saber.location=palm+direction*.072
bpy.context.view_layer.update();parent_bone_keep(saber,'mixamorig:RightHand')
grip.location=(.31,.17,.91);grip.rotation_euler=(0,0,math.radians(-15));grip.scale=(.40,)*3;parent_bone_keep(grip,'mixamorig:Hips')
points=[top+shaft_axis*.103,Vector((.29,.49,1.38)),Vector((.36,.37,1.15)),Vector((.33,.23,1.02))]
carry=[]
dense=[]
for i in range(101):
 t=i/100;u=1-t;dense.append(points[0]*(u**3)+points[1]*(3*u*u*t)+points[2]*(3*u*t*t)+points[3]*(t**3))
arc=[0]
for a,b in zip(dense,dense[1:]):arc.append(arc[-1]+(b-a).length)
def along(d):
 for i in range(1,len(arc)):
  if arc[i]>=d:return dense[i-1].lerp(dense[i],(d-arc[i-1])/max(.000001,arc[i]-arc[i-1]))
 return dense[-1]
for i in range(math.ceil(arc[-1]/.135)):
 a=along(i*.135);b=along(min(arc[-1],(i+1)*.135))
 o=bpy.data.objects.new('SM_Pelag_CarryLink_%02d'%len(carry),link.data);hero.objects.link(o);o.location=(a+b)*.5;o.rotation_mode='QUATERNION';o.rotation_quaternion=(b-a).to_track_quat('Z','Y');o.rotation_mode='XYZ';o.rotation_euler.rotate_axis('Z',(i%2)*math.pi/2);parent_bone_keep(o,'mixamorig:Spine2');carry.append(o)
report['back_carry']={'bone':'mixamorig:Spine2','anchor_world_matrix':[list(r) for r in anchor.matrix_world],'bone_parent_matrix_basis':[list(r) for r in anchor.matrix_basis],'anchor_width_m':.94,'anchor_eye_world':list(top+shaft_axis*.048),'carry_links':len(carry),'review_only':True,'runtime_handoff_implementation':'not installed until owner model approval'}
# Save original as byte-identical resource candidate; do not re-export or truncate rig weights.
source=ROOT/'razlom/Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx'
(OUT/'Pelag_v6_Body_Unchanged.fbx').write_bytes(source.read_bytes())
report['body']['candidate_sha256']=hashlib.sha256((OUT/'Pelag_v6_Body_Unchanged.fbx').read_bytes()).hexdigest()
report['body']['byte_identical_fbx']=report['body']['candidate_sha256']==report['lineage']['body']['sha256']
# Compare surface approximation at original vertices and seam coverage using original UV.
comparisons={}
for key in ['saber','link']:
 o=bpy.data.objects['SM_Pelag_'+key+'_Candidate'];original=bpy.data.objects['SOURCE_'+key]
 bm=bmesh.new();bm.from_mesh(o.data);tree=BVHTree.FromBMesh(bm)
 ds=[tree.find_nearest(v.co)[3] for v in original.data.vertices];ds.sort();bm.free()
 comparisons[key]={'max_distance_m':max(ds),'p95_distance_m':ds[int(len(ds)*.95)],'mean_distance_m':sum(ds)/len(ds),'original_triangles':tris(original),'candidate_triangles':tris(o),'metric':'original vertices to nearest candidate surface in unscaled source units'}
report['geometry_error']=comparisons
report['budget']['hero_and_static_weapons']=sum(report[k]['candidate'] for k in ['body','saber','anchor','grip'])+tris(eye)
report['budget']['anchor_eye_triangles']=tris(eye)
report['budget']['max_links_under_40000']=(40000-report['budget']['hero_and_static_weapons'])//report['link']['candidate']
report['budget']['static_code_upper_bound_links']={'slam':51,'slam_return':56,'boarding':93}
report['budget']['peak_static_envelope_triangles']=report['budget']['hero_and_static_weapons']+93*report['link']['candidate']
report['budget']['warning']='Static code envelope, not measured runtime. Assumes exactly one active weapon ownership and carry chain hidden during deployment. Runtime overdraw/drawcalls/transition overlaps need measurement after approved integration.'
camera=scene.camera
def aim(o,t):o.rotation_euler=(Vector(t)-o.location).to_track_quat('-Z','Y').to_euler()
def render(name,loc,target=(0,0,.95),ortho=2.2):
 camera.location=loc;camera.data.ortho_scale=ortho;aim(camera,target);scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
for name,loc in [('front',(0,-5,1.4)),('back',(0,5,1.4)),('side',(5,0,1.4)),('game',(3,-4,4.2))]:render(name,loc)
# Identical pose/camera before-after comparison; only weapon geometry differs.
originals.hide_render=False
original_saber=bpy.data.objects['SOURCE_saber'];original_link=bpy.data.objects['SOURCE_link']
for o in originals.objects:o.hide_render=True
original_saber.hide_render=False;original_saber.matrix_world=saber.matrix_world.copy();saber.hide_render=True
links=carry+[eye];saved_data=[o.data for o in links]
for o in links:o.data=original_link.data
render('game_original_geometry',(3,-4,4.2))
for o,m in zip(links,saved_data):o.data=m
saber.hide_render=False;original_saber.hide_render=True;originals.hide_render=True
# Weapon closeups on neutral ground, both originals/candidates rendered identically.
visible=[o for o in hero.objects if not o.hide_render]
for o in visible:o.hide_render=True
originals.hide_render=False
saber_parent=saber.parent;saber_bone=saber.parent_bone;saber_matrix=saber.matrix_world.copy()
saber.parent=None;saber.matrix_world=Matrix.Identity(4);saber.rotation_euler=(math.pi/2,0,0);saber.location=(0,0,.36)
bpy.context.view_layer.update()
original_saber.matrix_world=saber.matrix_world.copy()
saber.hide_render=False
render('saber_candidate',(0,-4,1.0),target=(0,0,.82),ortho=1.58)
saber.hide_render=True;original_saber.hide_render=False
render('saber_original',(0,-4,1.0),target=(0,0,.82),ortho=1.58)
original_saber.hide_render=True;originals.hide_render=True
saber.parent=saber_parent;saber.parent_bone=saber_bone;bpy.context.view_layer.update();saber.matrix_world=saber_matrix;bpy.context.view_layer.update()
for o in visible:o.hide_render=False
render('game',(3,-4,4.2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Pelag_Model_Candidate.blend'))
(OUT/'candidate_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('REFINED',json.dumps(report['budget']))
