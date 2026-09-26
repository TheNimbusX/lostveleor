"""Render the baked Howl for review and run the Blender-side checks (headless).

blender -b Howl_Baked_r01.blend --python render_review.py -- --out <review dir> [--views game,side,fit]
                                [--samples 24] [--checks-only]
* frames 0..48 from the game camera (orthographic, 48 deg pitch, three-quarter front),
  a true side camera and the reference camera fitted on howl.mp4;
* evaluated-mesh triangle intersections between body parts (new vs the Idle pose);
* evaluated bone transforms vs final_samples.json (the action reproduces the solve).
Writes blender_checks.json next to this script.
"""
import bpy, bmesh, sys, json, math, argparse
from pathlib import Path
from mathutils import Vector, Quaternion, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ap = argparse.ArgumentParser(); ap.add_argument('--out', required=True); ap.add_argument('--views', default='game,side,fit')
ap.add_argument('--samples', type=int, default=24); ap.add_argument('--checks-only', action='store_true')
args = ap.parse_args(argv)
HERE = Path(__file__).resolve().parent
OUT = Path(args.out).resolve()
s = bpy.context.scene
r = s.objects['ARM_ForestWendigo']; mesh = s.objects['SM_ForestWendigo_LOD0']
act = bpy.data.actions['AN_ForestWendigo_Howl_Baked']
r.animation_data.action = act
F = Vector((1, 1, 0)).normalized(); R = Vector((1, -1, 0)).normalized()

# ---------------------------------------------------------------- checks
src = json.loads((HERE / 'fit_source.json').read_text())
names = [b['name'] for b in src['bones']]
joints = json.loads((HERE / 'expected_joints.json').read_text())   # numpy FK of final_samples
bone_err = 0.0
for i, frame_joints in enumerate(joints):
    f = i / 4
    s.frame_set(int(f), subframe=f % 1)
    for j, n in enumerate(names):
        bone_err = max(bone_err, (Vector(frame_joints[j]) - r.pose.bones[n].matrix.to_translation()).length)

groups = {'L_arm': ['L_arm_lower', 'L_hand'], 'R_arm': ['R_arm_lower', 'R_hand'],
          'L_upper': ['L_arm_upper'], 'R_upper': ['R_arm_upper'],
          'L_leg': ['L_leg_upper', 'L_leg_lower', 'L_foot', 'L_toe'], 'R_leg': ['R_leg_upper', 'R_leg_lower', 'R_foot', 'R_toe'],
          'head': ['head'], 'torso': ['pelvis', 'spine_01', 'spine_02']}
pairs = [('L_arm', 'L_leg'), ('R_arm', 'R_leg'), ('L_arm', 'R_leg'), ('R_arm', 'L_leg'), ('L_arm', 'head'), ('R_arm', 'head'),
         ('L_arm', 'torso'), ('R_arm', 'torso'), ('head', 'torso'), ('L_arm', 'R_arm'), ('L_leg', 'R_leg'),
         ('head', 'L_upper'), ('head', 'R_upper'), ('L_upper', 'L_leg'), ('R_upper', 'R_leg')]
vg = {g.index: g.name for g in mesh.vertex_groups}
dom = []
for v in mesh.data.vertices:
    best = max(v.groups, key=lambda g: g.weight) if v.groups else None
    dom.append(vg[best.group] if best else '')
mesh.data.calc_loop_triangles()
tri_group = {}
for t in mesh.data.loop_triangles:
    gs = {dom[i] for i in t.vertices}
    for g, bones in groups.items():
        if all(x in bones for x in gs):
            tri_group.setdefault(g, []).append(list(t.vertices))


def overlaps(frame):
    s.frame_set(int(frame), subframe=frame % 1)
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    co = [mesh.matrix_world @ v.co for v in ev.data.vertices]
    trees = {g: BVHTree.FromPolygons(co, tris, all_triangles=True) for g, tris in tri_group.items()}
    return {f'{a}-{b}': len(trees[a].overlap(trees[b])) for a, b in pairs if a in trees and b in trees}


base = overlaps(0)
inter = {}
for i in range(0, 48 * 4 + 1):          # every quarter frame (the exported sampling)
    f = i / 4
    o = overlaps(f)
    new = {k: v - base.get(k, 0) for k, v in o.items() if v > base.get(k, 0)}
    if new: inter[f] = new
checks = {'evaluated_bone_vs_solve_max_m': bone_err, 'idle_baseline_overlaps': base, 'samples_checked': 193,
          'new_triangle_overlaps_by_frame': inter,
          'frames_with_new_overlaps': sorted(inter), 'action': act.name, 'action_frame_range': list(act.frame_range)}
(HERE / 'blender_checks.json').write_text(json.dumps(checks, indent=1))
print('CHECKS', json.dumps(checks)[:600])
if args.checks_only:
    raise SystemExit

# ---------------------------------------------------------------- review renders
s.render.engine = 'BLENDER_EEVEE'; s.eevee.taa_render_samples = args.samples
s.render.image_settings.file_format = 'PNG'; s.render.film_transparent = False
s.objects['ReferenceFloor'].hide_render = True
me = bpy.data.meshes.new('HowlReviewFloor')
me.from_pydata([(-15, -15, 0), (15, -15, 0), (15, 15, 0), (-15, 15, 0)], [], [(0, 1, 2, 3)])
grid = bpy.data.objects.new('HowlReviewFloor', me); s.collection.objects.link(grid)
mat = bpy.data.materials.new('HowlReviewFloor'); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes.get('Principled BSDF')
tc = nt.nodes.new('ShaderNodeTexCoord'); ck = nt.nodes.new('ShaderNodeTexChecker'); ck.inputs['Scale'].default_value = 2.0
ck.inputs['Color1'].default_value = (.43, .41, .37, 1); ck.inputs['Color2'].default_value = (.36, .345, .31, 1)
nt.links.new(tc.outputs['Object'], ck.inputs['Vector']); nt.links.new(ck.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = 1; me.materials.append(mat)
s.world.node_tree.nodes.get('Background').inputs[0].default_value = (.55, .57, .58, 1)
s.world.node_tree.nodes.get('Background').inputs[1].default_value = .45
s.objects['KEY'].data.energy = 380; s.objects['FILL'].data.energy = 120
cam = s.camera; cam.data.show_background_images = False


def setup(view):
    if view == 'game':
        pitch = math.radians(48)
        horizontal = (F * math.cos(math.radians(35)) + R * math.sin(math.radians(35))).normalized()
        d = (horizontal * math.cos(pitch) + Vector((0, 0, math.sin(pitch)))).normalized()
        cam.location = Vector((0, 0, 1.1)) + d * 40
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        cam.data.type = 'ORTHO'; cam.data.ortho_scale = 5.4; cam.data.shift_x = cam.data.shift_y = 0
        cam.data.clip_start = 1; cam.data.clip_end = 100
        s.render.resolution_x, s.render.resolution_y = 960, 720
    elif view == 'side':
        look = Vector((0, 0, 1.4))
        cam.location = look + R * 8.5 + Vector((0, 0, .2))
        cam.rotation_euler = (look - cam.location).to_track_quat('-Z', 'Y').to_euler()
        cam.data.type = 'PERSP'; cam.data.lens = 50; cam.data.sensor_width = 36; cam.data.sensor_fit = 'HORIZONTAL'
        cam.data.shift_x = cam.data.shift_y = 0; cam.data.clip_start = .5; cam.data.clip_end = 100
        s.render.resolution_x, s.render.resolution_y = 960, 720
    else:
        az, el, scale, cx, cy = json.loads((HERE / 'landmarks.json').read_text())['camera']
        t = Vector((0, 0, 1.55))
        cam.location = t + Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), math.sin(el))) * 5.5
        cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
        cam.data.type = 'PERSP'; cam.data.lens = 36 * 5.5 / scale; cam.data.sensor_width = 36; cam.data.sensor_fit = 'HORIZONTAL'
        cam.data.shift_x = .5 - cx / 960; cam.data.shift_y = cy / 960 - .5; cam.data.clip_start = .1
        s.render.resolution_x, s.render.resolution_y = 720, 720


for view in args.views.split(','):
    setup(view)
    folder = OUT / f'{view}_frames'; folder.mkdir(parents=True, exist_ok=True)
    for f in range(0, 49):
        s.frame_set(f)
        s.render.filepath = str(folder / f'{f:04d}.png')
        bpy.ops.render.render(write_still=True)
    print('RENDERED', view, flush=True)
