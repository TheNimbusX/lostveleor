"""Write the Howl scenes from the solved samples (run headless on ForestWendigo_Production.blend).

Howl_r01.blend        editable master: the 18 key poses on their key frames (FK, Bezier), phase
                      markers, reference camera with howl.mp4 as camera background. Plain Bezier
                      between keys is not IK-corrected (feet drift up to ~8 cm between keys);
                      the source of truth is pose_keys.py -> build_howl.py -> the baked clip.
Howl_Baked_r01.blend  derived clip for the Unity package: every quarter frame keyed
                      (linear) from final_samples.json - action AN_ForestWendigo_Howl_Baked.
The production file itself is never saved.
"""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector, Quaternion

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
s = bpy.context.scene
r = s.objects['ARM_ForestWendigo']
names = [b['name'] for b in json.loads((HERE / 'fit_source.json').read_text())['bones']]
samples = json.loads((HERE / 'final_samples.json').read_text())
keys = json.loads((HERE / 'key_frames.json').read_text())
SUB = 4
MARKERS = [('GATHER', 0), ('CROUCH_CLAWS_BACK', 6), ('RISE', 8), ('FULL_HEIGHT', 11), ('HOWL_PEAK', 14),
           ('SLAM_START', 16), ('CONTACT', 24), ('COMPRESSION', 29), ('HOLD', 32), ('PUSH_BACK', 36),
           ('CLAWS_LIFT', 38), ('IDLE', 48)]

# Only the Howl lives in these scenes; the accepted clips stay in their own files.
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


def key_action(name, frames, interp, leg_plants=False):
    a = bpy.data.actions.new(name); a.use_fake_user = True; r.animation_data.action = a
    prev = {}
    for f, x in frames:
        s.frame_set(int(f), subframe=f % 1)
        q = pose(x)
        for n in names[1:]:
            if n in prev and q[n].dot(prev[n]) < 0: q[n].negate()
            prev[n] = q[n].copy()
            r.pose.bones[n].rotation_quaternion = q[n]
            r.pose.bones[n].keyframe_insert(data_path='rotation_quaternion', frame=f, group=n)
        r.pose.bones['pelvis'].keyframe_insert(data_path='location', frame=f, group='pelvis')
    # FK clip: the rig's IK plants (used by Idle) are keyed off. (Turning the leg plants on
    # in the master was tried: its fixed knee poles re-orient the feet, so it stays FK.)
    for b in r.pose.bones:
        for c in b.constraints:
            on = leg_plants and c.name in ('IK_L_foot_Plant', 'IK_R_foot_Plant')
            for f in (frames[0][0], frames[-1][0]):
                c.influence = 1.0 if on else 0.0; c.keyframe_insert(data_path='influence', frame=f, group='Export constraint state')
    for layer in a.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for k in fc.keyframe_points:
                        k.interpolation = interp
                        if interp == 'BEZIER':
                            k.handle_left_type = k.handle_right_type = 'AUTO_CLAMPED'
    return a


s.render.fps = 24; s.frame_start = 0; s.frame_end = 48
for m in list(s.timeline_markers): s.timeline_markers.remove(m)
for n, f in MARKERS: s.timeline_markers.new(n, frame=f)

# reference camera (fitted on howl.mp4 frame 0) with the accepted video behind it
az, el, scale, cx, cy = json.loads((HERE / 'landmarks.json').read_text())['camera']
c = s.camera; t = Vector((0, 0, 1.55))
c.location = t + Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), math.sin(el))) * 5.5
c.rotation_euler = (t - c.location).to_track_quat('-Z', 'Y').to_euler()
c.data.type = 'PERSP'; c.data.lens = 36 * 5.5 / scale; c.data.sensor_width = 36; c.data.sensor_fit = 'HORIZONTAL'
c.data.shift_x = .5 - cx / 960; c.data.shift_y = cy / 960 - .5
s.render.resolution_x = s.render.resolution_y = 960
movie = HERE.parents[2] / 'review' / 'higgsfield-refs' / 'howl.mp4'
c.data.background_images.clear()
if movie.exists():
    c.data.show_background_images = True
    bg = c.data.background_images.new(); bg.source = 'MOVIE_CLIP'; bg.clip = bpy.data.movieclips.load(str(movie)); bg.alpha = .4

# editable master: the key poses (as evaluated in the final clip) on the key frames
key_action('AN_ForestWendigo_Howl_Keys', [(float(k), samples[int(k) * SUB]) for k in keys], 'BEZIER')
s.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'Howl_r01.blend'), copy=True)

# derived export clip: every quarter frame
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
key_action('AN_ForestWendigo_Howl_Baked', [(i / SUB, x) for i, x in enumerate(samples)], 'LINEAR')
s.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(HERE / 'Howl_Baked_r01.blend'), copy=True)
print('HOWL SCENES', len(samples), 'samples', len(keys), 'keys')
