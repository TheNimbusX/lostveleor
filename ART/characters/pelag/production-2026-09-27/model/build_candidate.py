"""Isolated, reproducible Pelag model review. Does not write Unity or original assets."""
import bpy, math, json, shutil, hashlib, bmesh
from pathlib import Path
from mathutils import Matrix, Vector
ROOT=Path(r'C:/Users/d.grab/Desktop/the-game')
OUT=ROOT/'ART/characters/pelag/production-2026-09-27/model'
AUDIT=json.loads((OUT/'source_audit.json').read_text())
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'
scene.render.engine='CYCLES';scene.cycles.samples=32
scene.render.resolution_x=1000;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
scene.world=bpy.data.worlds.new('Neutral daylight');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.42,.45,.48,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.45
hero=bpy.data.collections.new('COL_Pelag_Candidate');scene.collection.children.link(hero)
originals=bpy.data.collections.new('COL_Weapon_Originals');scene.collection.children.link(originals)
originals.hide_render=True
studio=bpy.data.collections.new('COL_Review_Studio');scene.collection.children.link(studio)
def move(o,col):
 for c in list(o.users_collection):c.objects.unlink(o)
 col.objects.link(o)
def imported(key):
 before=set(bpy.data.objects)
 bpy.ops.import_scene.fbx(filepath=str(ROOT/AUDIT[key]['path']),use_anim=False)
 obs=list(set(bpy.data.objects)-before)
 for o in obs:move(o,hero)
 return obs
def mat(name,path,metal=0,rough=.7):
 m=bpy.data.materials.new(name);m.use_nodes=True
 bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=rough;bs.inputs['Metallic'].default_value=metal
 tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(ROOT/path),check_existing=True)
 m.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
 return m
bodymat=mat('MAT_Pelag_Original','razlom/Assets/Resources/Characters/Pelag_v6/Pelag_v6_BaseColor.jpg')
sabermat=mat('MAT_Pelag_Saber_Original','razlom/Assets/Resources/Weapons/Pelag/FantasySaber/Pelag_FantasySaber_BaseColor.jpg',.25,.45)
anchormat=mat('MAT_Pelag_Anchor_Original','razlom/Assets/Resources/Weapons/Pelag/AnchorChain/Pelag_AnchorChain_BaseColor.jpg',.15,.53)
def tris(o):o.data.calc_loop_triangles();return len(o.data.loop_triangles)
def assign(o,m):o.data.materials.clear();o.data.materials.append(m)
def bounds(o):
 coords=[o.matrix_world@v.co for v in o.data.vertices]
 return Vector(tuple(min(v[i] for v in coords) for i in range(3))),Vector(tuple(max(v[i] for v in coords) for i in range(3)))
def apply_matrix(o,m):
 o.data.transform(m@o.matrix_world);o.parent=None;o.matrix_world=Matrix.Identity(4)
def select(obs):
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs:o.select_set(True)
 bpy.context.view_layer.objects.active=obs[0]
obs=imported('body');rig=next(o for o in obs if o.type=='ARMATURE');body=next(o for o in obs if o.type=='MESH')
for o in obs:
 if o.parent is None:o.matrix_world=Matrix.Scale(100,4)@o.matrix_world
bpy.context.view_layer.update();assign(body,bodymat)
rig.name='ARM_Pelag_v6_Original65';body.name='SM_Pelag_v6_Unchanged'
source_weights=[[(g.group,g.weight) for g in v.groups] for v in body.data.vertices]
source_bones=[(b.name, b.parent.name if b.parent else None) for b in rig.data.bones]
stats={'body':{'original':tris(body),'candidate':tris(body),'vertices':len(body.data.vertices),'rig_bones':len(rig.data.bones),'geometry_uv_weights':'unchanged','display_root_unit_correction':100}}
weapon_objects={};source_objects={}
for key,ratio,material in [('saber',.172,sabermat),('anchor',1,anchormat),('grip',1,anchormat),('link',.209,anchormat)]:
 objects=imported(key);o=next(x for x in objects if x.type=='MESH');assign(o,material)
 apply_matrix(o,Matrix.Identity(4));bpy.context.view_layer.update()
 original=o.copy();original.data=o.data.copy();originals.objects.link(original);original.name='SOURCE_'+key;source_objects[key]=original
 n=tris(o)
 if ratio<1:
  select([o]);mod=o.modifiers.new('Preserve silhouette reduction','DECIMATE');mod.ratio=ratio;mod.use_collapse_triangulate=True
  bpy.ops.object.modifier_apply(modifier=mod.name)
 o.name='SM_Pelag_'+key+'_Candidate';weapon_objects[key]=o
 stats[key]={'original':n,'candidate':tris(o),'ratio':ratio,'uv_layers':list(o.data.uv_layers.keys()),'materials':len(o.data.materials)}
 # Independent centered export retains original authoring pivot and metres.
 select([o]);bpy.ops.export_scene.fbx(filepath=str(OUT/('Pelag_'+key+'_Candidate.fbx')),use_selection=True,object_types={'MESH'},bake_anim=False,add_leaf_bones=False,axis_forward='-Z',axis_up='Y')
 # Separate lower-detail review LOD, not active in hero assembly.
 if key=='saber':
  lod=o.copy();lod.data=o.data.copy();originals.objects.link(lod);lod.name='SM_Pelag_Saber_LOD1'
  select([lod]);dec=lod.modifiers.new('LOD1 reduction','DECIMATE');dec.ratio=.5;dec.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=dec.name)
  stats[key]['lod1_triangles']=tris(lod)
  select([lod]);bpy.ops.export_scene.fbx(filepath=str(OUT/'Pelag_saber_LOD1.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,axis_forward='-Z',axis_up='Y')

# Back carry: actual original head, long X axis rotated upright, broad painted face backwards.
anchor=weapon_objects['anchor'];anchor.rotation_euler=(0,math.pi/2,0);anchor.scale=(.94/.49038118124,)*3
bpy.context.view_layer.update();lo,hi=bounds(anchor);anchor.location+=Vector((-.015,.285,1.12))-(lo+hi)*.5
# Saber is placed beside right hand with its existing curved blade and grip pivot.
saber=weapon_objects['saber'];saber.rotation_euler=(math.pi/2,0,math.radians(-12));saber.scale=(.80,)*3
bpy.context.view_layer.update();saber.location=(-.48,-.065,.92)
# Existing rolled-chain grip stays at belt, with short paid section visibly connected to head.
grip=weapon_objects['grip'];grip.scale=(.5,)*3;grip.location=(.24,.18,.95)
link=weapon_objects['link'];link.hide_render=True;link.hide_set(True)
# Carry chain laid behind shoulder; all links share one reduced mesh and original painted material.
points=[Vector((.22,.24,1.01)),Vector((.25,.30,1.18)),Vector((.20,.34,1.35)),Vector((.08,.35,1.47)),Vector((-.05,.33,1.42))]
carry=[]
for i in range(len(points)-1):
 a,b=points[i],points[i+1];n=max(1,round((b-a).length/.135))
 for j in range(n):
  p=a.lerp(b,(j+.5)/n);o=bpy.data.objects.new('SM_Pelag_CarryLink_%02d'%len(carry),link.data);hero.objects.link(o);o.location=p
  o.rotation_mode='QUATERNION';o.rotation_quaternion=(b-a).to_track_quat('Z','Y')
  if len(carry)%2:o.rotation_quaternion=o.rotation_quaternion@Vector((0,0,1)).to_track_quat('Z','Y') # rotation below uses local Z
  o.rotation_mode='XYZ';o.rotation_euler.rotate_axis('Z',(len(carry)%2)*math.pi/2);carry.append(o)
socket=bpy.data.objects.new('SOCKET_Anchor_Back',None);hero.objects.link(socket);socket.matrix_world=anchor.matrix_world.copy()
socket.parent=rig;socket.parent_type='BONE';socket.parent_bone='mixamorig:Spine2'
# Keep review mounting transforms explicit and do not alter original bone transforms.
bpy.context.view_layer.update();socket.matrix_world=anchor.matrix_world.copy()
stats['back_carry']={'bone':'mixamorig:Spine2','anchor_world_matrix':[list(r) for r in anchor.matrix_world],'socket_parent_inverse':[list(r) for r in socket.matrix_parent_inverse],'anchor_max_dimension_m':.94,'carry_links':len(carry),'review_only':True}
stats['budget']={'hero_and_static_weapons':sum(stats[k]['candidate'] for k in ['body','saber','anchor','grip']), 'link_tris':stats['link']['candidate'],'max_links_under_40000':int((40000-sum(stats[k]['candidate'] for k in ['body','saber','anchor','grip']))/stats['link']['candidate']), 'runtime_pool_capacity_main_feed_bridge':[96,24,16], 'warning':'Pool capacity is not a measured visible link count. Runtime budget must be checked before replacement.'}
stats['lineage']={k:{'path':v['path'],'sha256':v['sha256']} for k,v in AUDIT.items()}
stats['status']='MODEL CANDIDATE — not owner accepted, not installed in Unity'

def aim(o,target):o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
for name,loc,power,size in [('Key',(-3,-4,6),650,4),('Fill',(4,-1,3),350,3),('Rim',(0,4,5),500,3)]:
 d=bpy.data.lights.new(name,'AREA');o=bpy.data.objects.new(name,d);studio.objects.link(o);o.location=loc;d.energy=power;d.shape='DISK';d.size=size;aim(o,(0,0,1))
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.015));floor=bpy.context.object;floor.name='Review_Ground';move(floor,studio)
m=bpy.data.materials.new('MAT_Neutral_Ground');m.diffuse_color=(.16,.18,.19,1);m.use_nodes=True;m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.16,.18,.19,1);m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9;assign(floor,m)
camera=bpy.data.objects.new('CAM_Review',bpy.data.cameras.new('CAM_Review'));studio.objects.link(camera);scene.camera=camera;camera.data.type='ORTHO';camera.data.ortho_scale=2.45
def render(name,loc,target=(0,0,.96),ortho=2.45):
 camera.location=loc;camera.data.ortho_scale=ortho;aim(camera,target);scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
# Refine_review renders the final posed assembly and comparisons.
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Pelag_Model_Candidate.blend'))
(OUT/'candidate_report.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
print('CANDIDATE',json.dumps(stats['budget']))
