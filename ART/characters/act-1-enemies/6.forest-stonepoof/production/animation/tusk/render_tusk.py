"""Кадры просмотра клипа Stonehoof_Tusk: сбоку, игровая камера, спереди.

Запуск: blender -b --factory-startup --python tusk/render_tusk.py -- [master|baked] [percent]
Открывает мастер (или запечённый файл) без сохранения, пишет кадры 0–26 в
review/animation_tusk/<вид>_frames/. Листы собирает ffmpeg (tusk/sheets.sh).
"""
import bpy, math, sys
from pathlib import Path
from mathutils import Vector
HERE = Path(__file__).resolve().parent
OUT = HERE.parents[1] / 'review' / 'animation_tusk'
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
which = args[0] if args else 'master'
percent = int(args[1]) if len(args) > 1 else 50
src = HERE / ('Stonehoof_Tusk_r01.blend' if which == 'master' else 'Stonehoof_Tusk_Baked_r01.blend')
bpy.ops.wm.open_mainfile(filepath=str(src))
s = bpy.context.scene
s.render.resolution_percentage = percent
cam = s.camera
if cam is None:
    cam = bpy.data.objects.new('CAM_Review', bpy.data.cameras.new('CAM_Review'))
    cam.data.type = 'ORTHO'
    s.collection.objects.link(cam)
    s.camera = cam
target = Vector((0, -0.3, 0.6))
el = math.radians(52)
views = {
    # левый бок кабана (+X): видно проносу головы к камере
    'side': (Vector((8, -1.2, 2.2)), 3.2),
    # игровая: сверху под ~52°, с диагонали спереди-слева
    'game': (Vector((math.cos(el) * 6.4, -math.cos(el) * 6.4, math.sin(el) * 10)), 3.4),
    # спереди, чуть сверху: видно крюк вбок и вверх
    'front': (Vector((0.4, -8, 2.6)), 2.8),
}
for view, (offset, scale) in views.items():
    cam.location = target + offset
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.ortho_scale = scale
    for f in range(0, 27):
        s.frame_set(f)
        s.render.filepath = str(OUT / f'{which}_{view}_frames' / f'{f:04d}.png')
        bpy.ops.render.render(write_still=True)
print('TUSK_RENDER_DONE', which, flush=True)
