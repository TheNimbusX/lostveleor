"""Изображения принятой модели для входа Higgsfield, не новые анимации."""
import bpy,math,json
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parent;TARGET=OUT/'references';TARGET.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Stonehoof_ModelCandidate_r03.blend'))
scene=bpy.context.scene;cam=scene.camera
scene.render.resolution_x=1280;scene.render.resolution_y=720
scene.render.image_settings.file_format='JPEG';scene.render.image_settings.quality=94
def camera(target,offset,scale):
    target=Vector(target);cam.location=target+Vector(offset)
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
def render(name):
    scene.render.filepath=str(TARGET/name);bpy.ops.render.render(write_still=True)
camera((0,-1.7,0.7),(9,-1.6,3.7),7.0);render('charge_start.jpg')
bpy.ops.mesh.primitive_cube_add(size=1,location=(0,-2.2,0.9));wall=bpy.context.object;wall.name='REF_ImpactStone'
wall.scale=(1.5,0.8,1.8)
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
bevel=wall.modifiers.new('Rounded stone edges','BEVEL');bevel.width=0.09;bevel.segments=3
mat=bpy.data.materials.new('MAT_ReferenceStone');mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(0.24,0.26,0.24,1);bs.inputs['Roughness'].default_value=0.95
wall.data.materials.append(mat)
camera((0,-0.7,0.72),(9,-1.4,3.5),4.8);render('collision_start.jpg')
wall.hide_render=True
camera((0,0,0.6),(7,-7,3.8),3.7);render('death_start.jpg')
(TARGET/'inputs.json').write_text(json.dumps({'approved_model':'../Stonehoof_ModelCandidate_r03.blend','images':['charge_start.jpg','collision_start.jpg','death_start.jpg'],'note':'Rendered directly from owner-approved model; collision stone is a reference prop only.'},indent=2),encoding='utf8')
