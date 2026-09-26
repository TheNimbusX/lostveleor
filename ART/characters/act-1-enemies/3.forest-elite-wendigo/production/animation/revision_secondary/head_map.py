import runpy
from pathlib import Path
from types import SimpleNamespace
import bpy

out=Path(__file__).resolve().parent/'head_map';out.mkdir(exist_ok=True)
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o.find_armature()==rig)

def emission(name,color):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
 n=m.node_tree.nodes;n.clear();e=n.new('ShaderNodeEmission');e.inputs['Color'].default_value=(*color,1);e.inputs['Strength'].default_value=1.0
 o=n.new('ShaderNodeOutputMaterial');m.node_tree.links.new(e.outputs[0],o.inputs[0]);return m

mesh.data.materials.clear()
mesh.data.materials.append(emission('grey',(0.19,.20,.18)))
mesh.data.materials.append(emission('head_red',(.9,.06,.05)))
mesh.data.materials.append(emission('neck_blue',(.06,.35,.9)))
head=mesh.vertex_groups['head'].index;neck=mesh.vertex_groups['neck'].index
for p in mesh.data.polygons:
 h=sum(sum(g.weight for g in mesh.data.vertices[i].groups if g.group==head) for i in p.vertices)/len(p.vertices)
 n=sum(sum(g.weight for g in mesh.data.vertices[i].groups if g.group==neck) for i in p.vertices)/len(p.vertices)
 p.material_index=1 if h>.45 else (2 if n>.45 else 0)

preview=runpy.run_path(str(Path(__file__).resolve().parents[2]/'preview'/'render_clip_review.py'))
args=SimpleNamespace(width=700,height=700,no_floor=False,front=False)
scene=bpy.context.scene
rig.animation_data.action=bpy.data.actions['AN_ForestWendigo_Death'];scene.frame_set(0)
cameras,_=preview['setup_review_scene'](scene,[mesh],args)
for f in (0,40,60):
 scene.frame_set(f)
 for view,camera in cameras.items():preview['render_frame'](scene,camera,out/f'{f:03d}_{view}.png')
