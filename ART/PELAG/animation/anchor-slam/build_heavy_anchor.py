"""Кованый боевой якорь: чистый силуэт, фаски и проушина. Pivot — центр проушины."""
import bpy, math, bmesh
from mathutils import Vector
from pathlib import Path
ROOT=Path('C:/Users/d.grab/Desktop/the-game')
OUT=ROOT/'razlom/Assets/Resources/Weapons/Pelag/AnchorChain'
scene=bpy.data.scenes.get('Pelag_Anchor_Foundry') or bpy.data.scenes.new('Pelag_Anchor_Foundry')
bpy.context.window.scene=scene
collection=bpy.data.collections.new('COL_Heavy_Anchor_Authoring');scene.collection.children.link(collection)
objects=[]
colors=[(.115,.155,.19,1),(.31,.40,.44,1),(.53,.60,.60,1),(.30,.20,.095,1),(.52,.35,.15,1)]
mats=[]
for i,color in enumerate(colors):
    mat=bpy.data.materials.new('HeavyAnchor_'+str(i));mat.diffuse_color=color;mat.use_nodes=True
    bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=color;bs.inputs['Metallic'].default_value=.35;bs.inputs['Roughness'].default_value=.58;mats.append(mat)

def finish(obj,mat=0,bevel=.012):
    for c in list(obj.users_collection):c.objects.unlink(obj)
    collection.objects.link(obj);obj.data.materials.clear()
    for m in mats:obj.data.materials.append(m)
    for p in obj.data.polygons:p.material_index=mat
    if bevel:
        b=obj.modifiers.new('Forged edge bevel','BEVEL');b.width=bevel;b.segments=1;b.affect='EDGES';b.material=1 if mat==0 else 4
        b=obj.modifiers.new('Face weighted normals','WEIGHTED_NORMAL');b.keep_sharp=True;b.weight=35
    objects.append(obj);return obj

def prism(name,profile,depth,mat=0,bevel=.012):
    # Профиль x,z выдавливается по толщине y.
    n=len(profile);verts=[(x,y,z) for y in (-depth/2,depth/2) for x,z in profile]
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj)
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
    return finish(obj,mat,bevel)

prism('SM_Anchor_Shank',[(-.054,-.07),(.054,-.07),(.058,-.59),(.10,-.72),(0,-.81),(-.10,-.72),(-.058,-.59)],.15)
prism('SM_Anchor_Crown',[(-.12,-.57),(.12,-.57),(.17,-.71),(.11,-.82),(0,-.87),(-.11,-.82),(-.17,-.71)],.20,0,.022)
# Лапы образуют несущую дугу, а наконечник расширяется к внешнему режущему краю.
arm=[(.07,-.69),(.16,-.68),(.24,-.61),(.29,-.51),(.31,-.43),(.39,-.40),(.42,-.51),(.39,-.65),(.31,-.76),(.19,-.81),(.09,-.78)]
fluke=[(.25,-.50),(.30,-.39),(.32,-.24),(.49,-.47),(.39,-.48),(.34,-.60)]
for side in (-1,1):
    prism('SM_Anchor_Arm_'+str(side),[(x*side,z) for x,z in arm],.18,0,.02)
    prism('SM_Anchor_Fluke_'+str(side),[(x*side,z) for x,z in fluke],.19,0,.016)
prism('SM_Anchor_Stock',[(-.235,-.19),(.235,-.19),(.25,-.235),(.21,-.27),(-.21,-.27),(-.25,-.235)],.17,0,.012)
for z in (-.20,-.49):
    prism('SM_Anchor_Brass_Collar',[(-.076,z+.034),(.076,z+.034),(.076,z-.034),(-.076,z-.034)],.185,3,.006)

def link_mesh(name,length,width,tube,path_count=16,radial=6):
    # Овальное звено с прямыми длинными сторонами и полукруглыми торцами.
    verts=[];faces=[];radius=width*.5-tube;straight=(length-width)*.5
    for i in range(path_count):
        a=2*math.pi*i/path_count;x=math.cos(a)*radius;z=math.sin(a)*radius+(straight if math.sin(a)>=0 else -straight)
        outward=Vector((math.cos(a),0,math.sin(a)))
        for j in range(radial):
            t=2*math.pi*j/radial;p=Vector((x,0,z))+outward*(math.cos(t)*tube)+Vector((0,math.sin(t)*tube,0));verts.append(tuple(p))
    for i in range(path_count):
        for j in range(radial):faces.append((i*radial+j,((i+1)%path_count)*radial+j,((i+1)%path_count)*radial+(j+1)%radial,i*radial+(j+1)%radial))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update();obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj)
    return finish(obj,0,0)

ring=link_mesh('SM_Anchor_Shackle',.21,.17,.026,16,6);ring.location.z=-.02
# Небольшие заклёпки читаются объёмом, без текстурного шума.
for z in (-.20,-.49):
    for y in (-.097,.097):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=8,ring_count=4,radius=.022,location=(0,y,z))
        stud=bpy.context.object;stud.name='SM_Anchor_Collar_Rivet';stud.scale=(1,.35,1);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);finish(stud,4,0)

def baked_export(parts,name):
    bpy.ops.object.select_all(action='DESELECT');dupes=[]
    for obj in parts:
        copy=obj.copy();copy.data=obj.data.copy();scene.collection.objects.link(copy);copy.select_set(True);dupes.append(copy)
        bpy.context.view_layer.objects.active=copy
        for mod in list(copy.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.context.view_layer.objects.active=dupes[0];bpy.ops.object.join();combined=bpy.context.object;combined.name=name
    scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    color=combined.data.color_attributes.new(name='Col',type='FLOAT_COLOR',domain='CORNER')
    for p in combined.data.polygons:
        c=combined.data.materials[p.material_index].diffuse_color
        for l in p.loop_indices:color.data[l].color=c
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},
        use_mesh_modifiers=True,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='AUTO')
    tris=sum(len(p.vertices)-2 for p in combined.data.polygons)
    combined.hide_set(True);combined.hide_render=True
    return tris

anchor_parts=objects.copy();tris=baked_export(anchor_parts,'Pelag_AnchorHead_Heavy')
objects.clear();chain=link_mesh('SM_Heavy_Chain_Link',.174,.107,.015,12,6)
chain.location=(1.2,0,0);chain.location=(0,0,0)
chain_tris=baked_export([chain],'Pelag_ChainLink_Heavy');chain.hide_render=True;chain.hide_set(True)
scene.world=bpy.data.worlds.new('Anchor studio');scene.world.color=(.22,.22,.22)
bpy.ops.object.camera_add(location=(1.3,-2.6,1.0));camera=bpy.context.object;camera.name='Anchor scale review';scene.camera=camera
camera.rotation_euler=(Vector((0,0,-.39))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=1.35
bpy.ops.object.light_add(type='AREA',location=(1,-2,3));bpy.context.object.data.energy=400;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=4
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.render.resolution_x=960;scene.render.resolution_y=960;scene.render.resolution_percentage=100
scene.render.filepath=str(ROOT/'artifacts/anchor-heavy-model.png');bpy.ops.render.render(write_still=True)
bpy.data.libraries.write(str(ROOT/'ART/PELAG/animation/anchor-slam/Pelag_HeavyAnchor_Work.blend'),{scene},fake_user=True,compress=True)
result={'anchorTriangles':tris,'linkTriangles':chain_tris,'ringPivot':[0,0,0],'render':scene.render.filepath}
