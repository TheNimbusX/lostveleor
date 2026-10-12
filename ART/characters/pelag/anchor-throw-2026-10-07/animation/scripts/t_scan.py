"""Бросок якоря 07.10 — обзор источников движения: кадры FBX-тейков/клипов сбоку (правый бок, герой смотрит вправо
экрана, как лист поз) для выбора кусков настоящего движения.

blender -b --factory-startup -P t_scan.py -- <out_dir> <step> <fbx> [<fbx> ...]
Кадры: <out_dir>/<имя>/f_###.png (каждый step-й кадр) + <out_dir>/<имя>/range.txt
"""
import bpy, sys, os, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT, STEP, FILES = argv[0], float(argv[1]), argv[2:]
ORTHO = float(os.environ.get("T_ORTHO", "2.6")); ZC = float(os.environ.get("T_ZC", "0.95"))
FR = [float(x) for x in os.environ.get("T_RANGE", "").split(",") if x]
CH = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters"
V6 = CH + r"\Pelag_v6\Runtime\Pelag_v6_MixamoRig.fbx"


def setup():
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.render.resolution_x, sc.render.resolution_y = 300, 360
    w = bpy.data.worlds.new("w"); w.color = (0.55, 0.55, 0.55); sc.world = w
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = ORTHO
    return cam


for path in FILES:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    cam = setup()
    sc = bpy.context.scene
    bpy.ops.import_scene.fbx(filepath=path)
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    if not meshes:          # клип без сетки — сетка v6 на его риг (кости те же)
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=V6)
        new = [o for o in bpy.data.objects if o not in before]
        v6arm = [o for o in new if o.type == 'ARMATURE'][0]
        for m in [o for o in new if o.type == 'MESH']:
            for mod in m.modifiers:
                if mod.type == 'ARMATURE': mod.object = arm
            m.parent = arm
            m.matrix_parent_inverse = arm.matrix_world.inverted() @ v6arm.matrix_world
        bpy.data.objects.remove(v6arm, do_unlink=True)
    act = arm.animation_data.action if arm.animation_data else None
    f0, f1 = (act.frame_range if act else (0, 0))
    name = os.path.splitext(os.path.basename(path))[0]
    d = os.path.join(OUT, name); os.makedirs(d, exist_ok=True)
    sc.frame_set(int(f0)); bpy.context.view_layer.update()
    P = lambda n: arm.matrix_world @ arm.pose.bones["mixamorig:" + n].head
    hips = P("Hips")
    right = (P("RightUpLeg") - P("LeftUpLeg")); right.z = 0; right.normalize()
    fwd = Vector((0, 0, 1)).cross(right).normalized()          # вперёд героя в первом кадре
    with open(os.path.join(d, "range.txt"), "w") as fh: fh.write("%g %g fwd %s\n" % (f0, f1, tuple(round(x, 3) for x in fwd)))
    if FR: f0, f1 = FR
    i, f = 0, f0
    while f <= f1 + 1e-6:
        sc.frame_set(int(math.floor(f)), subframe=f - math.floor(f)); bpy.context.view_layer.update()
        hp = P("Hips"); c = Vector((hp.x, hp.y, ZC))
        cam.location = c + right * 10
        look = (c - cam.location).normalized()
        cam.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(d, "f_%03d.png" % i)
        bpy.ops.render.render(write_still=True)
        i += 1; f += STEP
    print("SCANNED", name, f0, f1, i)
