"""Read-only rendering of r03 package. Optional -- animation renders full game-camera takes."""
import sys
from pathlib import Path
import bpy
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE.parent))
import render_setup as rs
scene=rs.setup();rs.ground();scene.render.resolution_x=scene.render.resolution_y=600
scene.render.image_settings.file_format='PNG'
arm=bpy.data.objects['ARM_ForestSplitter'];ad=arm.animation_data
for tr in ad.nla_tracks:tr.mute=True
cameras={'game':rs.game_camera(),'side':rs.side_camera()}
animated='animation' in sys.argv
for role,end,frames in [('Death',12,[0,3,6,9,12]),('Bite',30,[0,6,12,16,18,24,30])]:
    action=bpy.data.actions['ForestSplitter_'+role];ad.action=action
    if action.slots:ad.action_slot=action.slots[0]
    for view,cam in cameras.items():
        scene.camera=cam
        output=HERE/'review'/role/view;output.mkdir(parents=True,exist_ok=True)
        for f in (range(end+1) if animated and view=='game' else frames):
            scene.frame_set(f);scene.render.filepath=str(output/(f'{f:03d}.png'))
            bpy.ops.render.render(write_still=True)
    print('REVIEW_DONE',role,flush=True)
