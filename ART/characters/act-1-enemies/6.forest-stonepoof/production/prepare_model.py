"""Модельный кандидат и честные контрольные ракурсы; исходники неизменны."""
import bpy, bmesh, json, math, statistics
from array import array
from pathlib import Path
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parent.parent
OUT=ROOT/'production'
REVIEW=OUT/'review'/'model_stage'
REVIEW.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Stonehoof_SourceRig.blend'))
bpy.context.preferences.filepaths.save_version=0
arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
raw_world=mesh.matrix_world.copy()
points=[raw_world @ v.co for v in mesh.data.vertices]
lo=Vector(tuple(min(p[i] for p in points) for i in range(3)))
hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
scale=1.45/(hi.z-lo.z)
transform=Matrix.Scale(scale,4) @ Matrix.Translation(Vector((-(lo.x+hi.x)/2,-(lo.y+hi.y)/2,-lo.z)))
original={'mesh_matrix':[list(r) for r in mesh.matrix_world],'armature_matrix':[list(r) for r in arm.matrix_world]}
for obj in [mesh,arm]:
    matrix=transform @ obj.matrix_world
    obj.parent=None
    obj.data.transform(matrix)
    obj.matrix_world=Matrix.Identity(4)
mesh.parent=arm
mesh.matrix_parent_inverse=Matrix.Identity(4)
arm.name='ARM_ForestStonehoof'
mesh.name='SM_ForestStonehoof_LOD0'

bm=bmesh.new();bm.from_mesh(mesh.data)
before={'verts':len(bm.verts),'boundary':sum(e.is_boundary for e in bm.edges),'nonmanifold':sum(not e.is_manifold for e in bm.edges)}
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=0.000015)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
after={'verts':len(bm.verts),'boundary':sum(e.is_boundary for e in bm.edges),'nonmanifold':sum(not e.is_manifold for e in bm.edges)}
boundary=[]
remaining={e for e in bm.edges if e.is_boundary}
while remaining:
    stack=[remaining.pop()]; component=set(stack)
    while stack:
        e=stack.pop()
        for v in e.verts:
            for n in v.link_edges:
                if n in remaining:
                    remaining.remove(n);component.add(n);stack.append(n)
    verts={v for e in component for v in e.verts}
    boundary.append({'edges':len(component),'min':[min(v.co[i] for v in verts) for i in range(3)],'max':[max(v.co[i] for v in verts) for i in range(3)]})
bm.to_mesh(mesh.data);bm.free();mesh.data.update()

# Закрываем обратные стороны отдельных накладок коры и листьев,
# сохраняя исходные внешние поверхности и координаты UV.
bm=bmesh.new();bm.from_mesh(mesh.data)
uv=bm.loops.layers.uv.active
image=bpy.data.images.load(str(ROOT/'wooden+boar+3d+model.fbm'/'wooden+boar+3d+model_basecolor.jpg'),check_existing=True)
pixels=array('f',[0.0])*(len(image.pixels));image.pixels.foreach_get(pixels)
def pixel(uv):
    x=max(0,min(image.size[0]-1,int(uv.x*image.size[0])))
    y=max(0,min(image.size[1]-1,int(uv.y*image.size[1])))
    i=(y*image.size[0]+x)*4
    return tuple(pixels[i:i+3])
duplicates=[];seen=set()
for face in bm.faces:
    key=tuple(sorted(v.index for v in face.verts))
    if key in seen:duplicates.append(face)
    else:seen.add(key)
if duplicates:bmesh.ops.delete(bm,geom=duplicates,context='FACES_ONLY')
bad=[e for e in bm.edges if len(e.link_faces)>2]
overloaded=[{'faces':len(e.link_faces),'ends':[list(v.co) for v in e.verts]} for e in bad]
if bad:bmesh.ops.split_edges(bm,edges=bad)
filled=[];cap_count=0
pending={e for e in bm.edges if e.is_boundary}
deform=bm.verts.layers.deform.active
while pending:
    first=pending.pop();chain=[first];vertices=[first.verts[0],first.verts[1]]
    while vertices[-1] != vertices[0]:
        choices=[e for e in vertices[-1].link_edges if e in pending]
        if not choices:raise RuntimeError('Open boundary is not a closed loop')
        edge=choices[0];pending.remove(edge);chain.append(edge);vertices.append(edge.other_vert(vertices[-1]))
    ring=vertices[:-1]
    center=bm.verts.new(sum((v.co for v in ring),Vector())/len(ring))
    if deform:
        weights={}
        for v in ring:
            for group,weight in v[deform].items():weights[group]=weights.get(group,0)+weight/len(ring)
        for group,weight in weights.items():center[deform][group]=weight
    uv_by_vertex={v:next(l[uv].uv.copy() for l in v.link_loops) for v in ring} if uv else {}
    uv_center=sum(uv_by_vertex.values(),Vector((0,0)))/len(ring) if uv else None
    # UV-швы внешней оболочки нельзя соединять через разные острова:
    # из-за этого на изнанке получаются коричневые звёзды. Берём оттенок
    # самой накладки из соседних внешних треугольников.
    if uv:
        neighbors={l.face for v in ring for l in v.link_loops}
        samples=[sum((l[uv].uv for l in f.loops),Vector((0,0)))/len(f.loops) for f in neighbors]
        colors=[pixel(p) for p in samples]
        median=tuple(statistics.median(c[i] for c in colors) for i in range(3))
        uv_center=samples[min(range(len(samples)),key=lambda k:sum((colors[k][i]-median[i])**2 for i in range(3)))]
    for edge in chain:
        original_loop=edge.link_loops[0]
        a=original_loop.vert;b=original_loop.link_loop_next.vert
        face=bm.faces.new((b,a,center));filled.append(face)
        if uv:
            for loop in face.loops:loop[uv].uv=uv_center
    cap_count+=1
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
repair={'caps_added':cap_count,'cap_triangles':len(filled),'duplicate_faces_removed':len(duplicates),'overloaded_before':overloaded,'boundary_after':sum(e.is_boundary for e in bm.edges),'nonmanifold_after':sum(not e.is_manifold for e in bm.edges)}
bm.to_mesh(mesh.data);bm.free();mesh.data.update()

trimmed=0
for vertex in mesh.data.vertices:
    weights=sorted([(g.group,g.weight) for g in vertex.groups if g.weight>1e-8],key=lambda g:-g[1])
    trimmed+=len(weights)>4
    keep=weights[:4];total=sum(w for _,w in keep)
    for group,weight in weights:
        mesh.vertex_groups[group].remove([vertex.index])
    for group,weight in keep:
        mesh.vertex_groups[group].add([vertex.index],weight/total,'REPLACE')
for poly in mesh.data.polygons:poly.use_smooth=True
mat=bpy.data.materials.new('MAT_ForestStonehoof_Bark');mat.use_nodes=True
bsdf=mat.node_tree.nodes.get('Principled BSDF')
bsdf.inputs['Roughness'].default_value=0.84
bsdf.inputs['Specular IOR Level'].default_value=0.18
image=bpy.data.images.load(str(ROOT/'wooden+boar+3d+model.fbm'/'wooden+boar+3d+model_basecolor.jpg'),check_existing=True)
image.pack()
node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image
mat.node_tree.links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
mesh.data.materials.clear();mesh.data.materials.append(mat)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
scene.render.engine='BLENDER_EEVEE';scene.render.resolution_x=1000;scene.render.resolution_y=900;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
scene.world=bpy.data.worlds.new('World_Review');scene.world.use_nodes=True
scene.world.node_tree.nodes.get('Background').inputs[0].default_value=(0.105,0.125,0.13,1)
scene.world.node_tree.nodes.get('Background').inputs[1].default_value=0.5
scene.view_settings.view_transform='AgX'
def aim(obj,point):obj.rotation_euler=(Vector(point)-obj.location).to_track_quat('-Z','Y').to_euler()
for name,location,power,color,size in [('KEY',(3,-4,5),750,(1,0.9,0.75),4),('FILL',(-3,-1,3),500,(0.75,0.86,1),4),('RIM',(0,4,4),850,(1,0.96,0.84),3)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.color=color;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=location;aim(obj,(0,0,0.7))
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-0.003));floor=bpy.context.object;floor.name='ReviewFloor'
fm=bpy.data.materials.new('MAT_ReviewFloor');fm.diffuse_color=(0.12,0.15,0.15,1);fm.use_nodes=True
fm.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(0.12,0.15,0.15,1)
fm.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=0.95;floor.data.materials.append(fm)
data=bpy.data.cameras.new('CAM_StonehoofReview');cam=bpy.data.objects.new(data.name,data);scene.collection.objects.link(cam);scene.camera=cam
data.type='ORTHO';data.ortho_scale=2.9
bpy.context.view_layer.update()
head=arm.matrix_world @ arm.data.bones['head0'].head_local
body=arm.matrix_world @ arm.data.bones['body'].head_local
front=1 if head.y>body.y else -1
mesh.data.calc_loop_triangles()
report={'status':'model_candidate_needs_owner_review','triangles':len(mesh.data.loop_triangles),'dimensions':list(mesh.dimensions),'texture_size':list(image.size),'scale_factor_from_rig':scale,'front_y':front,'source_matrices':original,'weld_before':before,'weld_after':after,'boundary_components':boundary,'repair':repair,'vertices_trimmed_to_four_weights':trimmed,'bones':len(arm.data.bones),'deforming_bones':sum(b.use_deform for b in arm.data.bones)}
(OUT/'model_audit.json').write_text(json.dumps(report,indent=2),encoding='utf8')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Stonehoof_ModelCandidate_r03.blend'))
for name,vector in [('front',(0,front,0.12)),('side',(1,0,0.12)),('back',(0,-front,0.12)),('three_quarter',(1,front,0.55)),('game_camera',(1,front,1.05))]:
    target=Vector((0,0,0.72));cam.location=target+Vector(vector).normalized()*9;aim(cam,target)
    scene.render.filepath=str(REVIEW/(name+'.png'));bpy.ops.render.render(write_still=True)
print('STONEHOOF_MODEL '+json.dumps(report))
