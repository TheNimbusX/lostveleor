# Review render of the turn clips. Opens the editable r01 scene (not saved back),
# plays each 15-frame 90 deg cycle 4 times (360 deg, 60 frames) with the preview
# carrier yaw, so the reviewer sees the turn the way the game drives it.
import bpy, math, sys
from pathlib import Path
from mathutils import Vector
HERE = Path(__file__).resolve().parent
OUT = HERE.parents[1] / 'review' / 'animation_turn'
bpy.ops.wm.open_mainfile(filepath=str(HERE / 'Stonehoof_Turn_r01.blend'))
s = bpy.context.scene
cam = s.camera
carrier = bpy.data.objects['CTRL_PreviewMotion_Only']
spin = bpy.data.objects.new('REVIEW_Spin', None)
s.collection.objects.link(spin)
carrier.parent = spin
CYCLES = 4
only = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []

def assign(clip):
    for o in bpy.data.objects:
        if not o.animation_data or not o.animation_data.action:
            continue
        old = o.animation_data.action.name
        for d in ('TurnLeft', 'TurnRight'):
            old = old.replace(d, clip)
        act = bpy.data.actions[old]
        o.animation_data.action = act
        if len(act.slots):
            o.animation_data.action_slot = act.slots[0]

target = Vector((0, 0, 0.5))
el = math.radians(48)
views = {
    'side': (Vector((8, -1.6, 2.5)), 3.6),
    # game-like three-quarter top-down: 48 deg pitch, looking from the -Y/+X diagonal
    'game': (Vector((math.cos(el) * 7.07, -math.cos(el) * 7.07, math.sin(el) * 10)), 3.4),
}
for clip, sign in (('TurnLeft', 1), ('TurnRight', -1)):
    if only and clip not in only:
        continue
    assign(clip)
    for view, (offset, scale) in views.items():
        cam.location = target + offset
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        cam.data.ortho_scale = scale
        for f in range(15 * CYCLES):
            spin.rotation_euler = (0, 0, sign * math.radians(90) * (f // 15))
            s.frame_set(f % 15)
            s.render.filepath = str(OUT / f'{clip}_{view}_frames' / f'{f:04d}.png')
            bpy.ops.render.render(write_still=True)
print('TURN_RENDER_DONE')
