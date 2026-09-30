import bpy, math, json
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parent
scene=bpy.context.scene
assert 'demo-polish-2026-09-30' in bpy.data.filepath, 'Wrong Blender worker scene'
for o in scene.objects: o.hide_render=True
name='COL_PelagAnchor_Demo'
old=bpy.data.collections.get(name)
if old:
    for o in list(old.objects): bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(old)
col=bpy.data.collections.new(name); scene.collection.children.link(col)
def adopt(o):
    for c in list(o.users_collection): c.objects.unlink(o)
    col.objects.link(o); o.hide_render=False
    return o
def mat(name, low, high):
    m=bpy.data.materials.new(name); m.use_nodes=True
    nt=m.node_tree; nt.nodes.clear()
    out=nt.nodes.new('ShaderNodeOutputMaterial'); p=nt.nodes.new('ShaderNodeBsdfPrincipled')
    p.inputs['Metallic'].default_value=.58; p.inputs['Roughness'].default_value=.58
    geo=nt.nodes.new('ShaderNodeNewGeometry')
    dot=nt.nodes.new('ShaderNodeVectorMath'); dot.operation='DOT_PRODUCT'; dot.inputs[1].default_value=(-.25,-.6,.65)
    nt.links.new(geo.outputs['Normal'],dot.inputs[0])
    remap=nt.nodes.new('ShaderNodeMapRange')
    remap.inputs['From Min'].default_value=-1; remap.inputs['From Max'].default_value=1
    remap.inputs['To Min'].default_value=.05; remap.inputs['To Max'].default_value=.95
    nt.links.new(dot.outputs['Value'],remap.inputs['Value'])
    mix=nt.nodes.new('ShaderNodeMixRGB'); mix.inputs[1].default_value=(*low,1); mix.inputs[2].default_value=(*high,1)
    nt.links.new(remap.outputs[0],mix.inputs[0])
    noise=nt.nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=16; noise.inputs['Detail'].default_value=2
    nt.links.new(geo.outputs['Position'],noise.inputs['Vector'])
    tint=nt.nodes.new('ShaderNodeMixRGB'); tint.blend_type='MULTIPLY'; tint.inputs[0].default_value=.12
    nt.links.new(mix.outputs[0],tint.inputs[1]); nt.links.new(noise.outputs['Fac'],tint.inputs[2])
    nt.links.new(tint.outputs[0],p.inputs['Base Color']); nt.links.new(p.outputs[0],out.inputs[0])
    return m
iron=mat('MAT_Anchor_IronPainted',(.045,.06,.08),(.17,.205,.245))
edge=mat('MAT_Anchor_EdgePainted',(.17,.20,.23),(.34,.38,.42))
dark=mat('MAT_Anchor_Reinforcement',(.027,.033,.043),(.115,.14,.17))
parts=[]
def finish(o, bevel=0):
    adopt(o); o.data.materials.clear()
    o.data.materials.append(iron); o.data.materials.append(edge); o.data.materials.append(dark)
    if bevel:
        mod=o.modifiers.new('Forged rounded edges','BEVEL'); mod.width=bevel; mod.segments=3; mod.material=1
        mod.affect='EDGES'; mod.harden_normals=True
        bpy.context.view_layer.objects.active=o
        o.select_set(True); bpy.ops.object.modifier_apply(modifier=mod.name); o.select_set(False)
    parts.append(o); return o
def extrude(name, polygon, depth, material=0, bevel=.009):
    verts=[(x,-depth/2,z) for x,z in polygon]+[(x,depth/2,z) for x,z in polygon]
    n=len(polygon); faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    for i in range(n): j=(i+1)%n; faces.append((i,j,j+n,i+n))
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    o=bpy.data.objects.new(name,mesh); col.objects.link(o)
    o=finish(o,bevel)
    for p in o.data.polygons:
        if p.material_index==0: p.material_index=material
    return o
def bezier(t, points):
    return sum((Vector(points[i])*math.comb(3,i)*(1-t)**(3-i)*t**i for i in range(4)),Vector((0,0)))
def arm(sign):
    points=[(0,-.255),(.18,-.31),(.40,-.18),(.345,.06)]
    cross=[(-.8,1),(.8,1),(1,.8),(1,-.8),(.8,-1),(-.8,-1),(-1,-.8),(-1,.8)]
    verts=[]; rows=36; sides=len(cross)
    for i in range(rows+1):
        t=i/rows; q=bezier(t,points); a=bezier(max(0,t-.001),points); b=bezier(min(1,t+.001),points)
        tangent=(b-a).normalized(); normal=Vector((-tangent.y,tangent.x))
        width=.066*(1-.25*t); thickness=.074*(1-.12*t)
        for j in range(sides):
            across,front=cross[j]
            x=q.x+normal.x*width*across; z=q.y+normal.y*width*across
            verts.append((sign*x,thickness*front,z))
    faces=[]
    for i in range(rows):
        for j in range(sides):
            k=i*sides+j; n=i*sides+(j+1)%sides
            faces.append((k,n,n+sides,k+sides) if sign==1 else (k,k+sides,n+sides,n))
    faces += [tuple(range(sides-1,-1,-1)),tuple(rows*sides+j for j in range(sides))]
    mesh=bpy.data.meshes.new('Curved arm'); mesh.from_pydata(verts,[],faces); mesh.update()
    o=bpy.data.objects.new('Anchor curved arm '+str(sign),mesh); col.objects.link(o); finish(o)
    for p in mesh.polygons: p.use_smooth=True
arm(1); arm(-1)
def faceted(name,polygon,depth,bevel=.006):
    n=len(polygon)
    center=(sum(p[0] for p in polygon)/n,sum(p[1] for p in polygon)/n)
    verts=[(x,-depth*.20,z) for x,z in polygon]+[(x,depth*.20,z) for x,z in polygon]
    verts += [(center[0],-depth*.65,center[1]),(center[0],depth*.45,center[1])]
    faces=[]
    for i in range(n):
        j=(i+1)%n
        faces += [(j,i,2*n),(i+n,j+n,2*n+1),(i,j,j+n,i+n)]
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    o=bpy.data.objects.new(name,mesh); col.objects.link(o)
    return finish(o,bevel)
for sign in [-1,1]:
    poly=[(sign*.25,.032),(sign*.37,.245),(sign*.445,.017),(sign*.34,-.044)]
    if sign==-1: poly.reverse()
    faceted('Broad fluke '+str(sign),poly,.15,bevel=.010)
extrude('Heavy stem',[(-.052,-.245),(.052,-.245),(.046,.405),(-.046,.405)],.12,bevel=.012)
faceted('Central diamond',[(-.15,-.20),(0,-.41),(.15,-.20),(0,-.095)],.24,bevel=.010)
for z in [-.13,.32]:
    extrude('Forged collar',[(-.075,z-.023),(.075,z-.023),(.075,z+.023),(-.075,z+.023)],.152,material=2,bevel=.007)
bpy.ops.mesh.primitive_torus_add(major_segments=40,minor_segments=10,location=(0,0,.459),
    rotation=(math.pi/2,0,0),major_radius=.064,minor_radius=.017)
ring=finish(bpy.context.object); ring.name='Anchor attachment eye'
for p in ring.data.polygons: p.use_smooth=True
bpy.ops.object.select_all(action='DESELECT')
for p in parts: p.select_set(True)
bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join()
head=bpy.context.object; head.name='Anchor_Head'; head.data.name='Anchor_Head_Demo'
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.uv.smart_project(angle_limit=math.radians(65),island_margin=.018)
bpy.ops.object.mode_set(mode='OBJECT')
# A real marker at the attachment eye is exported with the mesh.
attach=bpy.data.objects.new('Anchor_Attachment',None); col.objects.link(attach)
attach.parent=head; attach.location=(0,0,.46); attach.hide_render=True
# Bake authored material colour to an atlas; no photographic or generated texture.
image=bpy.data.images.new('Anchor_Demo_Painted',width=1024,height=1024)
outputs=[]
for m in head.data.materials:
    nt=m.node_tree
    target=nt.nodes.new('ShaderNodeTexImage'); target.image=image; nt.nodes.active=target
    out=next(n for n in nt.nodes if n.type=='OUTPUT_MATERIAL')
    principled=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
    color_link=principled.inputs['Base Color'].links[0].from_socket
    emit=nt.nodes.new('ShaderNodeEmission'); nt.links.new(color_link,emit.inputs[0])
    nt.links.new(emit.outputs[0],out.inputs[0]); outputs.append((nt,out,principled,emit))
bpy.context.view_layer.objects.active=head
scene.render.engine='CYCLES'; scene.cycles.samples=16
scene.render.bake.margin=12
bpy.ops.object.bake(type='EMIT')
export=root/'export'; export.mkdir(exist_ok=True)
image.filepath_raw=str(export/'Anchor_Demo_BaseColor.png'); image.file_format='PNG'; image.save()
for nt,out,principled,emit in outputs:
    nt.links.new(principled.outputs[0],out.inputs[0]); nt.nodes.remove(emit)
# Export static mesh in metres. Existing hero rig is untouched.
bpy.ops.object.select_all(action='DESELECT'); head.select_set(True); attach.select_set(True)
bpy.context.view_layer.objects.active=head
bpy.ops.export_scene.fbx(filepath=str(export/'Pelag_AnchorHead_Demo.fbx'),
    use_selection=True,object_types={'MESH','EMPTY'},use_mesh_modifiers=True,
    apply_unit_scale=True,axis_forward='-Z',axis_up='Y',bake_space_transform=True,
    add_leaf_bones=False,bake_anim=False)
scene.render.engine='CYCLES'; scene.cycles.samples=48
scene.render.resolution_x=1000; scene.render.resolution_y=1000; scene.render.resolution_percentage=100
world=bpy.data.worlds.new('Anchor review warm-grey'); scene.world=world; world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.17,.18,.20,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.45
def area(name,location,energy,size):
    data=bpy.data.lights.new(name,'AREA'); data.energy=energy; data.shape='DISK'; data.size=size
    o=bpy.data.objects.new(name,data); col.objects.link(o); o.location=location
    o.rotation_euler=(Vector((0,0,.03))-o.location).to_track_quat('-Z','Y').to_euler()
area('LGT_Key',(-1.6,-2.2,2.8),330,2)
area('LGT_Fill',(1.9,-.2,1.0),170,1.6)
area('LGT_Rim',(.6,1.8,1.5),260,1.1)
cam_data=bpy.data.cameras.new('CAM_AnchorReview'); camera=bpy.data.objects.new('CAM_AnchorReview',cam_data)
col.objects.link(camera); scene.camera=camera; cam_data.type='ORTHO';cam_data.ortho_scale=1.22
for label,loc in [('front',(0,-3,.02)),('three-quarter',(1.7,-3,1.1))]:
    camera.location=loc; camera.rotation_euler=(Vector((0,0,.025))-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.filepath=str(root/('anchor_'+label+'.png')); bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(root/'Pelag_ColorAnchor_Work.blend'))
head.data.calc_loop_triangles()
report={'head_triangles':len(head.data.loop_triangles),'vertices':len(head.data.vertices),
    'dimensions_m':list(head.dimensions),'attachment_blender':list(attach.location),
    'fbx':str(export/'Pelag_AnchorHead_Demo.fbx'),'atlas':str(export/'Anchor_Demo_BaseColor.png'),
    'body_source_unmodified':True}
(root/'anchor-build.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
result=report

