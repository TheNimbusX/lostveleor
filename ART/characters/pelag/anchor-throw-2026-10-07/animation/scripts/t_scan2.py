"""Обзор принятых клипов на риге v6 (сбоку, правый бок, герой смотрит вправо экрана):
  wreck:<Действие>  — действие из Pelag_Wreck4_Work.blend (Крушение v4, кадр = тик);
  sabre:<Клип>      — Pelag_AN_Sabre1–3, перенос на v6 как в Unity (v4_lib.SabreSource).
blender -b --factory-startup -P t_scan2.py -- <out_dir> <step> <spec> [<spec> ...]
"""
import bpy, sys, os, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PEL = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
V4S = os.path.join(PEL, "wreck-2026-10-03", "animation-v4", "scripts")
V3S = os.path.join(PEL, "wreck-2026-10-03", "animation-v3", "scripts")
for p in (V4S, V3S):
    if p not in sys.path: sys.path.insert(0, p)
argv = sys.argv[sys.argv.index("--") + 1:]
OUT, STEP, SPECS = argv[0], float(argv[1]), argv[2:]
ORTHO = float(os.environ.get("T_ORTHO", "2.4")); ZC = float(os.environ.get("T_ZC", "0.9"))
WORK = os.path.join(PEL, "wreck-2026-10-03", "animation-v4", "clips", "Pelag_Wreck4_Work.blend")


def camera():
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'MATERIAL'
    sc.render.resolution_x, sc.render.resolution_y = 300, 360
    w = bpy.data.worlds.new("w"); w.color = (0.55, 0.55, 0.55); sc.world = w
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = ORTHO
    return cam


def shoot(arm, d, frames, setter):
    sc = bpy.context.scene; cam = sc.camera
    os.makedirs(d, exist_ok=True)
    P = lambda n: arm.matrix_world @ arm.pose.bones["mixamorig:" + n].head
    right = Vector((-1, 0, 0))            # v6 в Blender смотрит в −Y: правый бок — −X
    for i, f in enumerate(frames):
        setter(f)
        hp = P("Hips"); c = Vector((hp.x, hp.y, ZC))
        cam.location = c + right * 10
        cam.rotation_euler = (c - cam.location).normalized().to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(d, "f_%03d.png" % i)
        bpy.ops.render.render(write_still=True)


def frange(a, b):
    out, f = [], a
    while f <= b + 1e-6: out.append(f); f += STEP
    return out


wreck = [s for s in SPECS if s.startswith("wreck:")]
sabre = [s for s in SPECS if s.startswith("sabre:")]
if wreck:
    bpy.ops.wm.open_mainfile(filepath=WORK)
    sc = bpy.context.scene
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    camera()
    for s in wreck:
        act = bpy.data.actions[s[6:]]
        arm.animation_data.action = act
        a, b = act.frame_range
        def setf(f):
            sc.frame_set(int(math.floor(f)), subframe=f - math.floor(f)); bpy.context.view_layer.update()
        shoot(arm, os.path.join(OUT, s[6:]), frange(a, b), setf)
        print("SCANNED", s, a, b)
if sabre:
    import v4_lib as L
    from wk_rig import Rig
    rig = Rig()
    src = L.SabreSource(rig.dst)
    camera()
    for s in sabre:
        clip = s[6:]
        n = {"Pelag_AN_Sabre1": 14, "Pelag_AN_Sabre2": 14, "Pelag_AN_Sabre3": 22}[clip]
        def setf(f):
            rig.reset(); src.apply(clip, f); bpy.context.view_layer.update()
        shoot(rig.dst, os.path.join(OUT, clip), frange(0, n), setf)
        print("SCANNED", s, 0, n)
