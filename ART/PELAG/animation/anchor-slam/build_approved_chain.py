"""Овальное звено по anchor-pelag1; UV использует сталь существующего оружия."""
import bpy, math
from pathlib import Path
from mathutils import Vector
ROOT=Path('C:/Users/d.grab/Desktop/the-game')
scene=bpy.data.scenes.get('Pelag_Approved_Chain') or bpy.data.scenes.new('Pelag_Approved_Chain')
bpy.context.window.scene=scene
verts=[];faces=[];N=24;M=8;radius=.031;straight=.04;tube=.014
for i in range(N):
    a=2*math.pi*i/N
    # Две круглые дуги соединены длинными прямыми боковинами, как в эталонном арте.
    x=radius*math.sin(a);z=radius*math.cos(a)+(straight if math.cos(a)>=0 else -straight)
    normal=Vector((math.sin(a),0,math.cos(a)))
    for j in range(M):
        b=2*math.pi*j/M
        verts.append(Vector((x,0,z))+normal*(tube*math.cos(b))+Vector((0,tube*math.sin(b),0)))
for i in range(N):
    for j in range(M):faces.append((i*M+j,((i+1)%N)*M+j,((i+1)%N)*M+(j+1)%M,i*M+(j+1)%M))
mesh=bpy.data.meshes.new('Pelag Oval Forged Link');mesh.from_pydata(verts,[],faces);mesh.update()
uv=mesh.uv_layers.new(name='SteelAtlas')
for p in mesh.polygons:
    p.use_smooth=True
    for li in p.loop_indices:
        vi=mesh.loops[li].vertex_index;i,j=divmod(vi,M)
        # Полоса рисованной стали без ткани/рукояти, мягкий светлый кант на изгибе.
        uv.data[li].uv=(.393+.024*(.5+.5*math.cos(j*math.pi/4)), .738+.072*i/(N-1))
obj=bpy.data.objects.new('Pelag_ChainLink_Painted',mesh);scene.collection.objects.link(obj)
mat=bpy.data.materials.new('Pelag Original Painted Steel');mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Roughness'].default_value=.46;bs.inputs['Metallic'].default_value=.12
tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(ROOT/'razlom/Assets/Resources/Weapons/Pelag/AnchorChain/Pelag_AnchorChain_BaseColor.jpg'),check_existing=True)
mat.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color']);mesh.materials.append(mat)
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
bpy.ops.export_scene.fbx(filepath=str(ROOT/'razlom/Assets/Resources/Weapons/Pelag/AnchorChain/Pelag_ChainLink_Painted.fbx'),use_selection=True,object_types={'MESH'},bake_anim=False,axis_forward='-Z',axis_up='Y')
for i in range(1,5):
    o=obj.copy();scene.collection.objects.link(o);o.location.z=-i*.135;o.rotation_euler.z=(i%2)*math.pi/2
camera=bpy.data.objects.new('Link review',bpy.data.cameras.new('Link review'));scene.collection.objects.link(camera);scene.camera=camera
camera.location=(.35,-1,.1);camera.rotation_euler=(Vector((0,0,-.27))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=.84
bpy.ops.object.light_add(type='AREA',location=(1,-2,2));bpy.context.object.data.energy=220;bpy.context.object.data.size=3
scene.world=bpy.data.worlds.new('Link review world');scene.world.color=(.22,.22,.22)
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.render.resolution_x=700;scene.render.resolution_y=900;scene.render.resolution_percentage=100
scene.render.filepath=str(ROOT/'artifacts/anchor-chain-painted.png');bpy.ops.render.render(write_still=True)
bpy.data.libraries.write(str(ROOT/'ART/PELAG/animation/anchor-slam/Pelag_Approved_Chain_Work.blend'),{scene},fake_user=True,compress=True)
result={'triangles':N*M*2,'length':.17,'pitch':.135,'render':scene.render.filepath}
