"""Сцены круга когтей из выборок (headless на unity_package/ForestWendigo_Production.blend).

Sweep_r01.blend        правка: поза на каждом целом кадре (FK, Безье), метки фаз.
Sweep_Baked_r01.blend  клип для пакета Unity: каждая четверть кадра (линейно) из
                       final_samples.json — действие AN_ForestWendigo_Sweep_Baked.
Кадр = тик Sim (30 в секунду), 0-39, удар на 21. Сцена пакета остаётся на 24 fps:
вид ведёт клип фазой, поэтому длина клипа в Unity на тайминг не влияет.
Сам файл пакета не сохраняется.
"""
import bpy, json, sys
from pathlib import Path
from mathutils import Vector, Quaternion

HERE = Path(__file__).resolve().parent
s = bpy.context.scene
r = s.objects['ARM_ForestWendigo']
names = [b['name'] for b in json.loads((HERE.parent / 'reference_match_howl' / 'fit_source.json').read_text())['bones']]
samples = json.loads((HERE / 'final_samples.json').read_text())
SUB = 4
END = (len(samples) - 1) // SUB
MARKERS = [('STANCE', 0), ('GATHER', 4), ('LOADED', 11), ('LAUNCH', 13), ('SPIN', 16), ('CONTACT', 21),
           ('LAND', 25), ('STEP', 28), ('IDLE', END)]

r.animation_data_create()
r.animation_data.action = None
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)


def pose(x):
    for b in r.pose.bones:
        b.location = (0, 0, 0); b.rotation_mode = 'QUATERNION'; b.rotation_quaternion = (1, 0, 0, 0); b.scale = (1, 1, 1)
    r.pose.bones['pelvis'].location = r.data.bones['pelvis'].matrix_local.to_3x3().transposed() @ Vector(x[:3])
    out = {}
    for j, n in enumerate(names[1:]):
        v = Vector(x[3 + j * 3:6 + j * 3])
        out[n] = Quaternion(v.normalized(), v.length) if v.length > 1e-9 else Quaternion()
    return out


def key_action(name, frames, interp):
    a = bpy.data.actions.new(name); a.use_fake_user = True; r.animation_data.action = a
    prev = {}
    for f, x in frames:
        s.frame_set(int(f), subframe=f % 1)
        q = pose(x)
        for n in names[1:]:
            # Полный оборот таза: знак кватерниона держим непрерывным.
            if n in prev and q[n].dot(prev[n]) < 0: q[n].negate()
            prev[n] = q[n].copy()
            r.pose.bones[n].rotation_quaternion = q[n]
            r.pose.bones[n].keyframe_insert(data_path='rotation_quaternion', frame=f, group=n)
        r.pose.bones['pelvis'].keyframe_insert(data_path='location', frame=f, group='pelvis')
    # FK-клип: опоры IK стойки Idle выключены ключами, как у воя.
    for b in r.pose.bones:
        for c in b.constraints:
            for f in (frames[0][0], frames[-1][0]):
                c.influence = 0.0; c.keyframe_insert(data_path='influence', frame=f, group='Export constraint state')
    for layer in a.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for k in fc.keyframe_points:
                        k.interpolation = interp
                        if interp == 'BEZIER':
                            k.handle_left_type = k.handle_right_type = 'AUTO_CLAMPED'
    return a


s.render.fps = 24; s.frame_start = 0; s.frame_end = END
for m in list(s.timeline_markers): s.timeline_markers.remove(m)
for n, f in MARKERS: s.timeline_markers.new(n, frame=f)

key_action('AN_ForestWendigo_Sweep_Keys', [(float(f), samples[f * SUB]) for f in range(0, END + 1)], 'BEZIER')
s.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'Sweep_r01.blend'), copy=True)

for a in list(bpy.data.actions): bpy.data.actions.remove(a)
key_action('AN_ForestWendigo_Sweep_Baked', [(i / SUB, x) for i, x in enumerate(samples)], 'LINEAR')
s.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'Sweep_Baked_r01.blend'), copy=True)
print('SWEEP SCENES', len(samples), 'samples, frames 0 ..', END)
