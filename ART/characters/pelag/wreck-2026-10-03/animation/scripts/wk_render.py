"""Крушение v2: проверка выгруженных FBX переносом как в Unity, кадры для листа поз и GIF в масштабе игры.

blender -b --factory-startup -P wk_render.py -- <anim_dir> <frames_dir> check|sheet|gif
Перенос — формула RazlomPelagAuthoredClips.Build (wk_view.Transfer, как sq_view/ab_view). Цепь и голова якоря на
кадрах — ПЛАН (прямая от правого кулака по плановой оси цепи из timing.json), не путь запечки.
"""
import bpy, sys, os, math, json
from mathutils import Vector, Matrix
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wk_view import Transfer, TEX

argv = sys.argv[sys.argv.index("--") + 1:]
anim, out, mode = argv[0], argv[1], argv[2]
os.makedirs(out, exist_ok=True)
P = "Pelag_AN_Wreck2_"
CLIPS = [P + c for c in ("Swing1", "Swing2", "Slam", "Wait1", "Wait2", "Stow", "Charge", "ChargeRelease", "Swing1_Braced",
                         "Swing2_Braced", "Slam_Drag")]
T = json.load(open(os.path.join(anim, "timing.json"), encoding="utf-8"))
TR = Transfer(anim, CLIPS, "Pelag_AN_Wreck2Bind")
sc, tgt = TR.sc, TR.tgt

mat = bpy.data.materials.new("Pelag_v6"); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes.get("Principled BSDF")
img = nt.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(TEX)
nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"]); nt.nodes.active = img
for m in TR.meshes:
    m.data.materials.clear(); m.data.materials.append(mat)


def solid(name, color):
    m = bpy.data.materials.new(name); m.diffuse_color = color
    return m


IRON = solid("iron", (0.16, 0.16, 0.18, 1)); STEEL = solid("steel", (0.85, 0.85, 0.9, 1)); LEATHER = solid("leather", (0.35, 0.2, 0.1, 1))


def unit_box(name, m):
    me = bpy.data.meshes.new(name)
    v = [(-.5, 0, -.5), (.5, 0, -.5), (.5, 0, .5), (-.5, 0, .5), (-.5, 1, -.5), (.5, 1, -.5), (.5, 1, .5), (-.5, 1, .5)]
    me.from_pydata(v, [], [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)])
    o = bpy.data.objects.new(name, me); sc.collection.objects.link(o); me.materials.append(m)
    return o


def place(o, a, b, w, d=None):
    v = b - a
    y = v.normalized() if v.length > 1e-6 else Vector((0, 0, 1))
    x = y.cross(Vector((0, 0, 1)))
    if x.length < 1e-6: x = Vector((1, 0, 0))
    x.normalize(); z = x.cross(y)
    o.matrix_world = Matrix.Translation(a) @ Matrix((x * w, y * max(v.length, 1e-4), z * (d or w))).transposed().to_4x4()


sabre, chain, head, arms, grip = (unit_box("sabre", STEEL), unit_box("chain", IRON), unit_box("anchor", IRON),
                                  unit_box("anchor_arms", IRON), unit_box("grip", LEATHER))


def bone(n):
    return tgt.matrix_world @ tgt.pose.bones["mixamorig:" + n].head


def W(u):
    """Оси корня Unity (x вправо, y вверх, z вперёд) → мир Blender (герой смотрит в −Y)."""
    return Vector((-u[0], -u[2], u[1]))


def place_props(clip, frame):
    hip = bone("LeftUpLeg"); hips = bone("Hips"); sp = bone("Spine")
    up = (sp - hips).normalized(); side = (bone("LeftUpLeg") - bone("RightUpLeg")).normalized()
    fwd = up.cross(side).normalized() * -1
    root = hip + side * 0.06 + up * 0.10 + fwd * 0.08
    place(sabre, root, root - fwd * 0.62 - up * 0.30, 0.045, 0.012)          # сабля за кушаком у левого бедра
    g = T["clips"][clip]["grip_track"][min(int(round(frame)), len(T["clips"][clip]["grip_track"]) - 1)]
    show = g["gL"] > 0.5
    for o in (chain, head, arms, grip): o.hide_render = not show
    if not show: return
    a, b = W(g["fistL"]), W(g["fistR"])
    ax = (b - a).normalized()
    place(grip, a - ax * 0.06, a + ax * 0.17, 0.04)
    d = W(g["chain_dir"]).normalized()
    end = b + d * 1.60
    place(chain, b, end, 0.035)
    place(head, end, end + d * 0.94, 0.10)
    s_ = d.cross(Vector((0, 0, 1)))
    if s_.length < 1e-6: s_ = Vector((1, 0, 0))
    s_.normalize()
    c = end + d * 0.80
    place(arms, c - s_ * 0.36, c + s_ * 0.36, 0.11)


bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
bpy.context.active_object.data.materials.append(solid("ground", (0.46, 0.34, 0.22, 1)))
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_shadows = True; sh.shadow_intensity = 0.35
sc.display.light_direction = (0.25, 0.30, 0.92)
sc.view_settings.view_transform = 'Standard'; sc.view_settings.exposure = 0.8
w = bpy.data.worlds.new("w"); w.color = (0.12, 0.15, 0.14); sc.world = w
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'
a15 = math.radians(15)
VIEWS = {"game": ((math.cos(a15), -math.sin(a15)), 48), "side": ((1, 0), 0), "q34": ((0.5, 0.866), 8), "front": ((0, 1), 5)}


def look(view_dir, pitch_deg, target, scale, dist=30.0):
    p = math.radians(pitch_deg)
    v = Vector((view_dir[0], view_dir[1], 0)).normalized()
    fwd = Vector((v.x * math.cos(p), v.y * math.cos(p), -math.sin(p)))
    cam.location = target - fwd * dist
    cam.rotation_euler = fwd.to_track_quat('-Z', 'Y').to_euler()
    cam.data.ortho_scale = scale


def pose_at(clip, frame):
    tgt.location = (0, 0, 0); tgt.rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    r = TR.apply(clip, frame)
    place_props(clip, frame)
    return r


if mode == "check":
    worst = {}
    for c in CLIPS:
        n = len(T["clips"][c]["grip_track"])
        rs = [pose_at(c, f) for f in range(n)]
        worst[c] = round(max(rs), 3)
    print("UNITY_HIP_RATIO", worst)
    fh = open(os.path.join(out, "unity_transfer.json"), "w"); fh.write(json.dumps(worst)); fh.close()
elif mode == "sheet":
    sc.render.resolution_x = sc.render.resolution_y = 420
    for k, (clip, frame, what) in T["sheet_pose_map"].items():
        pose_at(clip, frame)
        for vn in ("q34", "side", "game"):
            vd, pitch = VIEWS[vn]
            look(vd, pitch, Vector((0, -0.7, 0.95)), 4.6 if vn != "game" else 5.6)
            sc.render.filepath = os.path.join(out, "pose%s_%s.png" % (k, vn))
            bpy.ops.render.render(write_still=True)
elif mode == "sheet_forms":
    sc.render.resolution_x = sc.render.resolution_y = 420
    for k, (clip, frame, what) in T["sheet_pose_map_forms"].items():
        pose_at(clip, frame)
        for vn in ("q34", "side", "game"):
            vd, pitch = VIEWS[vn]
            look(vd, pitch, Vector((0, -0.7, 0.95)), 4.6 if vn != "game" else 5.6)
            sc.render.filepath = os.path.join(out, "pose%s_%s.png" % (k, vn))
            bpy.ops.render.render(write_still=True)
elif mode == "gif_forms":
    sc.render.resolution_x = 720; sc.render.resolution_y = 450
    ch = [(P + "Charge", f) for f in range(12)]
    NW, SH, BW = "Девятый вал", "Водяной панцирь", "Волнорез"
    seq = ([(NW, P + "Swing1", 0)] * 2 + [(NW, P + "Swing1", f) for f in range(0, 9)]
           + [(NW, P + "Swing2", f) for f in range(1, 8)] + [(NW, P + "Slam", f) for f in range(1, 6)]
           + [(NW, c, f) for c, f in (ch * 2 + ch[:3])] + [(NW, P + "Charge", 3)]
           + [(NW, P + "ChargeRelease", f) for f in range(1, 16)] + [(NW, P + "ChargeRelease", 15)] * 3
           + [(SH, P + "Swing1_Braced", f) for f in range(0, 9)] + [(SH, P + "Swing2_Braced", f) for f in range(1, 8)]
           + [(SH, P + "Slam", f) for f in range(1, 21)] + [(SH, P + "Slam", 20)] * 3
           + [(BW, P + "Swing2", 7)] * 2 + [(BW, P + "Slam_Drag", f) for f in range(1, 22)]
           + [(BW, P + "Stow", f) for f in range(0, 8)] + [(BW, P + "Stow", 7)] * 3)
    meta = []
    for i, (form, clip, f) in enumerate(seq):
        pose_at(clip, f)
        look(VIEWS["game"][0], 48, Vector((0.0, -0.9, 0.6)), 10.5)
        sc.render.filepath = os.path.join(out, "gif_%03d.png" % i)
        bpy.ops.render.render(write_still=True)
        meta.append(dict(form=form, clip=clip, frame=f))
    fh = open(os.path.join(out, "gif_seq.json"), "w", encoding="utf-8"); fh.write(json.dumps(meta, ensure_ascii=False)); fh.close()
elif mode == "gif":
    sc.render.resolution_x = 720; sc.render.resolution_y = 450
    seq = [(P + "Swing1", 0)] * 3 + [(P + "Swing1", f) for f in range(0, 9)] + [(P + "Swing2", f) for f in range(1, 8)] + \
          [(P + "Slam", f) for f in range(1, 21)] + [(P + "Stow", f) for f in range(0, 8)] + [(P + "Stow", 7)] * 4
    meta = []
    for i, (clip, f) in enumerate(seq):
        pose_at(clip, f)
        look(VIEWS["game"][0], 48, Vector((0.0, -0.9, 0.6)), 10.5)
        sc.render.filepath = os.path.join(out, "gif_%03d.png" % i)
        bpy.ops.render.render(write_still=True)
        meta.append(dict(clip=clip, frame=f))
    fh = open(os.path.join(out, "gif_seq.json"), "w"); fh.write(json.dumps(meta)); fh.close()
print("render done", mode)
