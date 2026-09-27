import bpy,math,json,bmesh,hashlib,sys
from pathlib import Path
from mathutils import Matrix,Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(r'C:/Users/d.grab/Desktop/the-game');OUT=ROOT/'ART/characters/pelag/production-2026-09-27/model'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Pelag_Model_Candidate.blend'))
scene=bpy.context.scene;hero=bpy.data.collections['COL_Pelag_Candidate'];rig=bpy.data.objects['ARM_Pelag_v6_Original65'];body=bpy.data.objects['SM_Pelag_v6_Unchanged'];camera=scene.camera
report=json.loads((OUT/'candidate_report.json').read_text());report['version']='candidate-r1'
anchor=bpy.data.objects['SM_Pelag_anchor_Candidate'];eye=bpy.data.objects['SM_Pelag_AnchorEye'];saber=bpy.data.objects['SM_Pelag_saber_Candidate'];grip=bpy.data.objects['SM_Pelag_grip_Candidate'];link=bpy.data.objects['SM_Pelag_link_Candidate']
def parent_keep(o,bone):
 bpy.context.view_layer.update();m=o.matrix_world.copy();o.parent=rig;o.parent_type='BONE';o.parent_bone=bone;bpy.context.view_layer.update();o.matrix_world=m;bpy.context.view_layer.update()
def bounds(o):
 v=[o.matrix_world@v.co for v in o.data.vertices];return Vector([min(v[i] for v in v) for i in range(3)]),Vector([max(v[i] for v in v) for i in range(3)])
def tris(o):o.data.calc_loop_triangles();return len(o.data.loop_triangles)
def select(obs):
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.hide_set(False);o.select_set(True)
 bpy.context.view_layer.objects.active=obs[0]
# Recompute mount from the existing pose after comparison-camera restoration.
saber.parent=None;saber.rotation_mode='XYZ';saber.rotation_euler=(math.radians(127),0,math.radians(-8));saber.scale=(.80,)*3
bpy.context.view_layer.update()
hand=rig.matrix_world@rig.pose.bones['mixamorig:RightHand'].head
palm=hand.lerp(rig.matrix_world@rig.pose.bones['mixamorig:RightHandMiddle1'].head,.6)
direction=saber.rotation_euler.to_matrix()@Vector((0,1,0));saber.location=palm+direction*.072
parent_keep(saber,'mixamorig:RightHand')
grip.parent=None;grip.rotation_mode='XYZ';grip.location=(.31,.17,.91);grip.rotation_euler=(0,0,math.radians(-15));grip.scale=(.40,)*3;parent_keep(grip,'mixamorig:Hips')
anchor.parent=None;anchor.rotation_mode='XYZ';anchor.rotation_euler=(math.atan2(.55,.835),0,math.radians(-12));anchor.location=(0,0,0);anchor.scale=(.94/.49038118124,)*3
bpy.context.view_layer.update();lo,hi=bounds(anchor);anchor.location+=Vector((0,.31,1.075))-(lo+hi)*.5;bpy.context.view_layer.update()
top=anchor.matrix_world@Vector((-.0019656515,.096264005,-.006767678));axis=(anchor.rotation_euler.to_matrix()@Vector((0,.55,.835))).normalized();parent_keep(anchor,'mixamorig:Spine2')
eye.parent=None;eye.location=top+axis*.048;eye.rotation_mode='QUATERNION';eye.rotation_quaternion=axis.to_track_quat('Z','Y');eye.scale=(.8,)*3;parent_keep(eye,'mixamorig:Spine2')
for o in list(hero.objects):
 if o.name.startswith('SM_Pelag_CarryLink'):bpy.data.objects.remove(o,do_unlink=True)
p=[top+axis*.103,top+Vector((.28,.02,.02)),Vector((.38,.33,1.12)),Vector((.33,.23,1.02))]
curve=[]
for i in range(101):
 t=i/100;u=1-t;curve.append(p[0]*u**3+p[1]*3*u*u*t+p[2]*3*u*t*t+p[3]*t**3)
arc=[0]
for a,b in zip(curve,curve[1:]):arc.append(arc[-1]+(b-a).length)
def along(d):
 for i in range(1,len(arc)):
  if arc[i]>=d:return curve[i-1].lerp(curve[i],(d-arc[i-1])/(arc[i]-arc[i-1]))
 return curve[-1]
carry=[]
for i in range(math.ceil(arc[-1]/.135)):
 a=along(i*.135);b=along(min(arc[-1],(i+1)*.135));o=bpy.data.objects.new('SM_Pelag_CarryLink_%02d'%i,link.data);hero.objects.link(o);o.location=(a+b)*.5;o.rotation_mode='QUATERNION';o.rotation_quaternion=(b-a).to_track_quat('Z','Y');o.rotation_mode='XYZ';o.rotation_euler.rotate_axis('Z',(i%2)*math.pi/2);parent_keep(o,'mixamorig:Spine2');carry.append(o)
bpy.data.objects['SOCKET_Anchor_Back'].matrix_world=anchor.matrix_world.copy()
report['back_carry'].update({'anchor_world_matrix':[list(r) for r in anchor.matrix_world],'bone_parent_matrix_basis':[list(r) for r in anchor.matrix_basis],'anchor_eye_world':list(eye.matrix_world.translation),'carry_links':len(carry),'shaft_axis_world':list(axis)})
report['anchor']['top_boundary_edges_closed']=44
report['anchor']['candidate']=tris(anchor)
# Repair the historic cut boundaries on the separate grip, preserving boundary UVs.
bm=bmesh.new();bm.from_mesh(grip.data);bm.verts.ensure_lookup_table()
uvlayer=bm.loops.layers.uv.active
uv_by_vertex={v.index:next((loop[uvlayer].uv.copy() for loop in v.link_loops),Vector((.405,.76))) for v in bm.verts} if uvlayer else {}
cut_edges=[e for e in bm.edges if e.is_boundary]
newfaces=bmesh.ops.holes_fill(bm,edges=cut_edges,sides=0).get('faces',[]) if cut_edges else []
for f in newfaces:
 f.smooth=False
 if uvlayer:
  for loop in f.loops:loop[uvlayer].uv=uv_by_vertex[loop.vert.index]
# Some old cuts meet at a vertex; holes_fill leaves these boundary cycles open.
# Split the boundary walk into simple cycles and cap each with its own center.
remaining=set(e for e in bm.edges if e.is_boundary)
fan_faces=[]
while remaining:
 first=next(iter(remaining));start=first.verts[0];path=[start];current=start
 while True:
  edge=next((e for e in current.link_edges if e in remaining),None)
  if edge is None:break
  remaining.remove(edge);nxt=edge.other_vert(current)
  if nxt in path:
   at=path.index(nxt);cycle=path[at:]
   if len(cycle)>=3:
    center=bm.verts.new(sum((v.co for v in cycle),Vector())/len(cycle));uvcenter=sum((uv_by_vertex[v.index] for v in cycle),Vector((0,0)))/len(cycle) if uvlayer else None
    for i,v in enumerate(cycle):
     face=bm.faces.new((v,cycle[(i+1)%len(cycle)],center));fan_faces.append(face)
     if uvlayer:
      for loop in face.loops:loop[uvlayer].uv=uvcenter if loop.vert==center else uv_by_vertex[loop.vert.index]
   path=path[:at+1]
  else:path.append(nxt)
  current=nxt
newfaces+=fan_faces
if newfaces:bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.to_mesh(grip.data);bm.free();grip.data.update()
report['grip']['boundary_edges_closed']=94;report['grip']['candidate']=tris(grip)
report['budget']['hero_and_static_weapons']=sum(report[k]['candidate'] for k in ['body','saber','anchor','grip'])+tris(eye)
report['budget']['max_links_under_40000']=(40000-report['budget']['hero_and_static_weapons'])//report['link']['candidate']
# Topology / weights audits with original body preserved.
topology={}
for key,o in [('body',body),('saber',saber),('anchor',anchor),('grip',grip),('link',link)]:
 bm=bmesh.new();bm.from_mesh(o.data)
 topology[key]={'vertices':len(bm.verts),'triangles':tris(o),'boundary_edges':sum(e.is_boundary for e in bm.edges),'non_manifold_edges':sum(not e.is_manifold for e in bm.edges),'loose_vertices':sum(not v.link_faces for v in bm.verts),'uv_layers':list(o.data.uv_layers.keys())};bm.free()
report['topology']=topology
report['body']['weights_max']=max(len(v.groups) for v in body.data.vertices);report['body']['weights_over4_vertices']=sum(len(v.groups)>4 for v in body.data.vertices);report['body']['weight_sum_max_error']=max(abs(sum(g.weight for g in v.groups)-1) for v in body.data.vertices)
deps=bpy.context.evaluated_depsgraph_get();evaluated=body.evaluated_get(deps);mesh=evaluated.to_mesh();vertices=[evaluated.matrix_world@v.co for v in mesh.vertices];tree=BVHTree.FromPolygons(vertices,[tuple(p.vertices) for p in mesh.polygons]);evaluated.to_mesh_clear()
distances=[]
for v in anchor.data.vertices:
 pnt=anchor.matrix_world@v.co;loc,n,idx,dist=tree.find_nearest(pnt);distances.append((dist, (pnt-loc).dot(n)))
report['back_carry']['idle_surface_clearance_m']=min(d[0] for d in distances)
report['back_carry']['idle_possible_penetrating_vertices']=sum(d[1]<-.001 for d in distances)
report['back_carry']['clearance_note']='Nearest-surface preflight in existing combat idle; full moving pose collision checks belong to next animation gate.'
report['budget']['peak_static_envelope_triangles']=report['budget']['hero_and_static_weapons']+93*report['link']['candidate']
# Export weapon geometry independently at original pivot, including repaired original anchor cap.
for key,o in [('saber',saber),('anchor',anchor),('grip',grip),('link',link)]:
 temp=bpy.data.objects.new('Pelag_'+key+'_Candidate',o.data);hero.objects.link(temp);temp.matrix_world=Matrix.Identity(4);select([temp]);bpy.ops.export_scene.fbx(filepath=str(OUT/('Pelag_'+key+'_Candidate.fbx')),use_selection=True,object_types={'MESH'},bake_anim=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y');bpy.data.objects.remove(temp,do_unlink=True)
def aim(o,t):o.rotation_euler=(Vector(t)-o.location).to_track_quat('-Z','Y').to_euler()
def render(name,loc,target=(0,0,.95),ortho=2.2):
 camera.location=loc;camera.data.ortho_scale=ortho;aim(camera,target);scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
scene.cycles.samples=24
if '--preview-only' in sys.argv:
 scene.cycles.samples=8;scene.render.resolution_percentage=60
 render('mount_preview',(3,-4,4.2))
 raise SystemExit()
for name,loc in [('front',(0,-5,1.4)),('back',(0,5,1.4)),('side',(5,0,1.4)),('game',(3,-4,4.2))]:render(name,loc)
orig=bpy.data.collections['COL_Weapon_Originals'];orig.hide_render=False
for o in orig.objects:o.hide_render=True
osaber=bpy.data.objects['SOURCE_saber'];olink=bpy.data.objects['SOURCE_link'];osaber.matrix_world=saber.matrix_world.copy();osaber.hide_render=False;saber.hide_render=True
saved=[o.data for o in carry+[eye]]
for o in carry+[eye]:o.data=olink.data
render('game_original_geometry',(3,-4,4.2))
for o,m in zip(carry+[eye],saved):o.data=m
osaber.hide_render=True;orig.hide_render=True;saber.hide_render=False
visible=[o for o in hero.objects if not o.hide_render and o.type in {'MESH','ARMATURE','EMPTY'}]
select(visible)
options={'filepath':str(OUT/'Pelag_Model_Candidate.glb'),'export_format':'GLB','use_selection':True,'export_animations':False,'export_skins':True}
if 'export_all_influences' in bpy.ops.export_scene.gltf.get_rna_type().properties:options['export_all_influences']=True
bpy.ops.export_scene.gltf(**options)
# Preserve body FBX and weights exactly; GLB is the assembled review asset.
for img in bpy.data.images:
 if img.source=='FILE' and img.filepath:
  try:img.pack()
  except RuntimeError:pass
select([rig]);scene.camera.location=(3,-4,4.2);aim(scene.camera,(0,0,.95));bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Pelag_Model_Candidate.blend'))
report['outputs']={'blend':'Pelag_Model_Candidate.blend','glb':'Pelag_Model_Candidate.glb','views':['front.png','back.png','side.png','game.png'],'comparison':['game_original_geometry.png','saber_original.png','saber_candidate.png'],'body':'Pelag_v6_Body_Unchanged.fbx','weapons':['Pelag_saber_Candidate.fbx','Pelag_anchor_Candidate.fbx','Pelag_grip_Candidate.fbx','Pelag_link_Candidate.fbx']}
(OUT/'candidate_report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
# Roundtrip imports into an empty scene; no reuse of in-memory source objects.
verification={}
for key in ['saber','anchor','grip','link']:
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.fbx(filepath=str(OUT/('Pelag_'+key+'_Candidate.fbx')),use_anim=False);meshes=[o for o in bpy.data.objects if o.type=='MESH'];verification[key]={'triangles':sum(tris(o) for o in meshes),'uv_layers':[list(o.data.uv_layers.keys()) for o in meshes],'matches_candidate':sum(tris(o) for o in meshes)==report[key]['candidate']}
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.ops.import_scene.gltf(filepath=str(OUT/'Pelag_Model_Candidate.glb'))
verification['assembled_glb']={'meshes':len([o for o in bpy.data.objects if o.type=='MESH']),'bones':sum(len(o.data.bones) for o in bpy.data.objects if o.type=='ARMATURE'),'triangles_visible_stowed':sum(tris(o) for o in bpy.data.objects if o.type=='MESH')}
verification['status']='candidate only; not game runtime verified or owner accepted'
(OUT/'roundtrip_verification.json').write_text(json.dumps(verification,indent=2))
print('FINAL_MODEL',json.dumps({'budget':report['budget'],'clearance':report['back_carry'],'verification':verification}))
