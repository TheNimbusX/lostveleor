"""Approval camera from above/rear: the fixed obstacle must not hide the head."""
import bpy, sys
from pathlib import Path
from mathutils import Vector

HERE=Path(__file__).resolve().parent
OUT=HERE.parents[1]/'review'/'animation_collision'
bpy.ops.wm.open_mainfile(filepath=str(HERE/'Stonehoof_Collision_r01.blend'))
scene=bpy.context.scene;cam=scene.camera
lights={o:o.location.copy() for o in scene.objects if o.type=='LIGHT'}
verts=[];faces=[]
for step in range(-32,8):
    y=step*1.2;n=len(verts)
    verts.extend([(-6,y-.012,-.002),(6,y-.012,-.002),(6,y+.012,-.002),(-6,y+.012,-.002)])
    faces.append((n,n+1,n+2,n+3))
grid=bpy.data.meshes.new('QA_FloorMarks');grid.from_pydata(verts,[],faces)
obj=bpy.data.objects.new('QA_FloorMarks',grid);scene.collection.objects.link(obj)
mat=bpy.data.materials.new('MAT_QA_FloorMarks');mat.use_nodes=True
mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.035,.045,.045,1)
mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.9
obj.data.materials.append(mat)
frames=(12,16,26,48) if '--keys' in sys.argv else range(49)
for frame in frames:
    scene.frame_set(frame);y=-.4*min(frame,12)
    target=Vector((0,y-.30,.72));cam.location=target+Vector((7,4.5,7))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.ortho_scale=3.9
    for light,base in lights.items():light.location=base+Vector((0,y,0))
    bpy.context.view_layer.update()
    scene.render.filepath=str(OUT/'game_frames'/f'{frame:04d}.png')
    bpy.ops.render.render(write_still=True)
print('UPPER_CAMERA_RENDER_COMPLETE')
