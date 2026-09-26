"""Review the accepted windup against the new loop with identical set and lens."""
import bpy,math,hashlib,json
from pathlib import Path
from mathutils import Vector
HERE=Path(__file__).resolve().parent;PREV=HERE.parent/'windup_start';OUT=HERE.parents[1]/'review'/'animation_charge_loop'
source=PREV/'Stonehoof_WindupStart_r01.blend';before=hashlib.sha256(source.read_bytes()).hexdigest()
bpy.ops.wm.open_mainfile(filepath=str(source));scene=bpy.context.scene;carrier=bpy.data.objects['CTRL_PreviewMotion_Only'];cam=scene.camera
scene.render.resolution_x=960;scene.render.resolution_y=640;scene.eevee.taa_render_samples=24
verts=[];faces=[]
for step in range(-32,8):
 y=step*1.2;n=len(verts);verts.extend([(-6,y-.012,-.002),(6,y-.012,-.002),(6,y+.012,-.002),(-6,y+.012,-.002)]);faces.append((n,n+1,n+2,n+3))
grid=bpy.data.meshes.new('QA_FloorMarks');grid.from_pydata(verts,[],faces);go=bpy.data.objects.new('QA_FloorMarks',grid);scene.collection.objects.link(go)
mat=bpy.data.materials.new('MAT_QA_FloorMarks');mat.use_nodes=True;mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.035,.045,.045,1);mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.9;go.data.materials.append(mat)
lights={o:o.location.copy() for o in scene.objects if o.type=='LIGHT'}
for view in ('side','game'):
 folder=OUT/('transition_'+view+'_frames');folder.mkdir(exist_ok=True)
 for f in range(36):
  scene.frame_set(f);travel=carrier.location.y;target=Vector((0,travel,.72));offset=Vector((8,-1,2.3)) if view=='side' else Vector((6,-7,7))
  cam.location=target+offset;cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=3.35
  for light,base in lights.items():light.location=base+Vector((0,travel,0))
  bpy.context.view_layer.update();scene.render.filepath=str(folder/f'{f:04d}.png');bpy.ops.render.render(write_still=True)
assert hashlib.sha256(source.read_bytes()).hexdigest()==before
print('TRANSITION_RENDERED',before)
