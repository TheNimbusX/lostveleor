"""Run in Blender on the production rig after revise_walk.py. Keeps old Walk source."""
import bpy
from pathlib import Path
ROOT=Path(__file__).resolve().parent
destination=ROOT/'walk_revision_r04/Walk_Final.blend'
if destination.exists() and Path(bpy.data.filepath).resolve()!=destination.resolve():
    bpy.ops.wm.open_mainfile(filepath=str(destination))
source=(ROOT/'apply_final.py').read_text(encoding='utf-8')
source=source.replace("kind=globals().get('CLIP','Death');out=ROOT/(kind.lower()+'_work') if kind!='Leap' else ROOT.parent/'reference_match_leap'", "kind='Walk';out=ROOT/'walk_revision_r04'")
source=source.replace("k.interpolation='LINEAR'", "k.interpolation='BEZIER';k.handle_left_type='AUTO_CLAMPED';k.handle_right_type='AUTO_CLAMPED'")
exec(compile(source,str(ROOT/'apply_final.py'),'exec'),{'__file__':str(ROOT/'apply_final.py')})
scene=bpy.context.scene;cam=scene.camera
with bpy.data.libraries.load(str(ROOT/'walk_work/Walk_Final.blend'),link=False) as (src,dst):
    dst.objects=['CAM_ReferenceMatch']
reference=dst.objects[0];scene.collection.objects.link(reference);bpy.context.view_layer.update()
cam.matrix_world=reference.matrix_world.copy();cam.data.lens=48;cam.data.type=reference.data.type
bpy.data.objects.remove(reference,do_unlink=True)
scene.render.filepath=str(ROOT/'walk_revision_r04/front/pose_')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'walk_revision_r04/Walk_Final.blend'))
result={'file':bpy.data.filepath,'stride_metres':2.4}
