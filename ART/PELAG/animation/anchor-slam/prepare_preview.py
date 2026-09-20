"""Показ производного клипа на исходном скине; источники остаются в файле."""
import bpy
from pathlib import Path
folder=Path(__file__).parent
rig=bpy.data.objects.get('AnchorSlam_Preview_Rig') or bpy.data.objects['Squall_Preview_Rig']
rig.name='AnchorSlam_Preview_Rig'
rig.animation_data_clear();rig.animation_data_create()
action=bpy.data.actions['AN_Pelag_AnchorSlam']
rig.animation_data.action=action;rig.animation_data.action_slot=action.slots[0]
for o in bpy.context.scene.objects:
    visible=o==rig or o.parent==rig or o.type in {'LIGHT','CAMERA'}
    o.hide_set(not visible);o.hide_render=not visible
for image in bpy.data.images:
    if image.source=='FILE' and not Path(bpy.path.abspath(image.filepath)).exists():
        candidates=list(Path('C:/Users/d.grab/Desktop/the-game/ART/PELAG').rglob(Path(image.filepath).name))
        if candidates:image.filepath=str(candidates[0]);image.reload()
scene=bpy.context.scene;scene.frame_start=1;scene.frame_end=28;scene.frame_set(10)
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.wm.save_as_mainfile(filepath=str(folder/'Pelag_AnchorSlam_Work.blend'))
