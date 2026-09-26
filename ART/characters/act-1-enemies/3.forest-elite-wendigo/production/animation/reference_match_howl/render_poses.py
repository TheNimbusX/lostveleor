"""Render arbitrary Howl poses headless (review / fitting aid, never saves the .blend).

blender -b <scene.blend> --python render_poses.py -- --poses poses.json --out DIR
        [--camera fit|game|side] [--fit az,el,scale,cx,cy] [--res 960x960] [--samples 16]
        [--frames 0,4,8] [--action NAME]

poses.json: {"frames": {"<frame>": [66 floats]}} in the fit parameterisation
(pelvis world offset + bone-local rotation vectors, fit_source bone order), or
--action renders an existing action of the scene at the listed frames.
"""
import bpy, sys, json, math, argparse
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument('--poses'); ap.add_argument('--action'); ap.add_argument('--out', required=True)
ap.add_argument('--camera', default='game'); ap.add_argument('--fit', default='')
ap.add_argument('--res', default='960x720'); ap.add_argument('--samples', type=int, default=16)
ap.add_argument('--frames', default=''); ap.add_argument('--names', default='')
ap.add_argument('--prefix', default='pose_'); ap.add_argument('--floor', default='grid')
args = ap.parse_args(argv)

HERE = Path(__file__).resolve().parent
s = bpy.context.scene
r = s.objects['ARM_ForestWendigo']
names = [b['name'] for b in json.loads((HERE / 'fit_source.json').read_text())['bones']]
out = Path(args.out); out.mkdir(parents=True, exist_ok=True)
w, h = map(int, args.res.split('x'))
s.render.resolution_x, s.render.resolution_y = w, h
s.render.resolution_percentage = 100
s.render.engine = 'BLENDER_EEVEE'
s.eevee.taa_render_samples = args.samples
s.render.film_transparent = False
s.render.image_settings.file_format = 'PNG'

F = Vector((1, 1, 0)).normalized()          # creature forward (FacingGuide)
R = Vector((1, -1, 0)).normalized()         # creature right
cam = s.camera
if args.camera == 'fit':
    az, el, scale, cx, cy = map(float, args.fit.split(','))
    t = Vector((0, 0, 1.55))
    cam.location = t + Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), math.sin(el))) * 5.5
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'PERSP'; cam.data.lens = 36 * 5.5 / scale; cam.data.sensor_width = 36
    cam.data.sensor_fit = 'HORIZONTAL'
    cam.data.shift_x = .5 - cx / 960; cam.data.shift_y = cy / 960 - .5
elif args.camera == 'game':
    # Game combat camera: orthographic, 48 degrees pitch (CameraFollow.CombatPitch);
    # the creature is seen three-quarter from its front-right, as when it faces the hero.
    pitch = math.radians(48)
    horizontal = (F * math.cos(math.radians(35)) + R * math.sin(math.radians(35))).normalized()
    look = Vector((0, 0, 1.25))
    d = (horizontal * math.cos(pitch) + Vector((0, 0, math.sin(pitch)))).normalized()
    cam.location = look + d * 40
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 5.2; cam.data.shift_x = 0; cam.data.shift_y = 0
    cam.data.clip_start = 1; cam.data.clip_end = 100
elif args.camera == 'side':
    look = Vector((0, 0, 1.45))
    cam.location = look + R * 8.5 + Vector((0, 0, .2))
    cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'PERSP'; cam.data.lens = 50; cam.data.sensor_width = 36; cam.data.sensor_fit = 'HORIZONTAL'
    cam.data.shift_x = 0; cam.data.shift_y = 0; cam.data.clip_start = .5; cam.data.clip_end = 100
cam.data.show_background_images = False

# Neutral floor with a 0.5 m checker at z = 0 so contact and sliding are readable.
if args.floor == 'grid':
    s.objects['ReferenceFloor'].hide_render = True
    me = bpy.data.meshes.new('HowlReviewFloor')
    me.from_pydata([(-15, -15, 0), (15, -15, 0), (15, 15, 0), (-15, 15, 0)], [], [(0, 1, 2, 3)])
    grid = bpy.data.objects.new('HowlReviewFloor', me); s.collection.objects.link(grid)
    mat = bpy.data.materials.new('HowlReviewFloor'); mat.use_nodes = True
    nt = mat.node_tree; bsdf = nt.nodes.get('Principled BSDF')
    tc = nt.nodes.new('ShaderNodeTexCoord'); ck = nt.nodes.new('ShaderNodeTexChecker')
    ck.inputs['Scale'].default_value = 2.0
    ck.inputs['Color1'].default_value = (.43, .41, .37, 1); ck.inputs['Color2'].default_value = (.36, .345, .31, 1)
    nt.links.new(tc.outputs['Object'], ck.inputs['Vector'])
    nt.links.new(ck.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 1
    me.materials.append(mat)
s.world.node_tree.nodes.get('Background').inputs[0].default_value = (.55, .57, .58, 1)
s.world.node_tree.nodes.get('Background').inputs[1].default_value = .45
s.objects['KEY'].data.energy = 380; s.objects['FILL'].data.energy = 120

def apply(x):
    for b in r.pose.bones:
        b.location = (0, 0, 0); b.rotation_mode = 'QUATERNION'; b.rotation_quaternion = (1, 0, 0, 0); b.scale = (1, 1, 1)
        for c in b.constraints: c.influence = 0
    r.pose.bones['pelvis'].location = r.data.bones['pelvis'].matrix_local.to_3x3().transposed() @ Vector(x[:3])
    for j, n in enumerate(names[1:]):
        v = Vector(x[3 + j * 3:6 + j * 3])
        r.pose.bones[n].rotation_quaternion = Quaternion(v.normalized(), v.length) if v.length > 1e-9 else Quaternion()

if args.action:
    r.animation_data_create(); r.animation_data.action = bpy.data.actions[args.action]
    frames = [float(f) for f in args.frames.split(',')] if args.frames else list(range(int(r.animation_data.action.frame_range[0]), int(r.animation_data.action.frame_range[1]) + 1))
    for i, f in enumerate(frames):
        s.frame_set(int(f), subframe=f % 1)
        s.render.filepath = str(out / f'{args.prefix}{i:04d}.png'); bpy.ops.render.render(write_still=True)
else:
    if r.animation_data: r.animation_data.action = None
    poses = json.loads(Path(args.poses).read_text())['frames']
    keys = args.frames.split(',') if args.frames else list(poses)
    for i, k in enumerate(keys):
        apply(poses[k]); bpy.context.view_layer.update()
        label = args.names.split(',')[i] if args.names else f'{i:04d}'
        s.render.filepath = str(out / f'{args.prefix}{label}.png'); bpy.ops.render.render(write_still=True)
print('RENDERED', out)
