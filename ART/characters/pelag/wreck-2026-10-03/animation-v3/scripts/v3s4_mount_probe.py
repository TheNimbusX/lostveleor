"""Крушение v3, Stow: куда на спине повесить рукоять, чтобы ЛЕВАЯ кисть доставала её чисто (без головы, шеи и корпуса).
blender -b --factory-startup -P v3s4_mount_probe.py -- <out.json>
Поза — Stow кадр 3 (v3st_keys) с вариантами рысканья груди; точки-кандидаты — в осях груди (вправо, вверх, назад от Spine2, м);
левая рука перебором полюса локтя ставит гнездо хвата в точку; печатаются промах гнезда, пересечения кисти/предплечья
с корпусом, головой, бёдрами (как wk_check), крутка предплечья. Только замер — клипы не трогаются."""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector
from s_lib import Body
from wk_rig import Rig, fl
import wk_grip, wk_pose, wk_fingers, wk_check, v3_arms
from wk_clips import rot
import v3st_keys as K

OUTF = sys.argv[sys.argv.index("--") + 1:][0]
rig = Rig(); wk_grip.prepare(rig); body = Body(rig.mesh); mesh = wk_check.Mesh(rig, body)
rig.reset(); wk_fingers.apply(rig, 0, 0); bpy.context.view_layer.update()
base = {(a, b): (t, d) for a, b, t, d, w in mesh.contacts(body.verts())}
CAND = {"neck_R_old": None, "scap_R_top": (0.11, 0.08, 0.15), "scap_R_mid": (0.12, 0.02, 0.16), "scap_R": (0.13, -0.04, 0.16)}
POLES = {"cross_front": (0.6, -0.3, -0.6), "cross_low": (0.5, -0.2, -0.8), "cross_out": (0.6, 0.2, -0.7), "cross_down": (0.3, 0.2, -0.9), "back_out": (-0.6, 0.6, -0.5), "back_down": (-0.8, 0.3, -0.5), "out_up": (0.0, 0.9, 0.4), "front_up": (0.6, -0.4, 0.5),
         "front_out": (0.5, 0.6, -0.2)}


def chest():
    o = rig.P("Spine2"); r = (rig.P("RightArm") - rig.P("LeftArm")).normalized()
    u = rig.P("Neck") - o; u = (u - u.dot(r) * r).normalized(); b = r.cross(u).normalized()   # назад = вправо × вверх (герой смотрит в −Y)
    return o, r, u, b


def place_left(target_w, pole_root, cyaw):
    v3_arms.CLAV.clear()
    D = Vector((0, 0, 1))
    fist = target_w + (wk_grip.fist_center(rig, "Left") - v3_arms.socket(rig))
    h = (0.0, 0.0, 0.0, 0.0)
    for it in range(5):
        sol, miss, ax = v3_arms.solve(rig, "Left", fist, D, rot(pole_root, cyaw), h, full=(it == 0))
        err = target_w - v3_arms.socket(rig)
        if err.length < 0.002: break
        fist = fist + err; h = sol
    return (v3_arms.socket(rig) - target_w).length


res = []
for dcy in (-25.0, 0.0, 25.0):
    for name, off in CAND.items():
        best = None
        for pn, pv in POLES.items():
            p = K.body_at(3); p = dict(p); p["cyaw"] = p["cyaw"] + dcy
            wk_pose.body(rig, p); bpy.context.view_layer.update()
            o, r, u, b = chest()
            if off is None:      # старое крепление: за шеей справа (0,48 м от левого плеча), как Stow.stow.txt
                tw = o + r * 0.231 / 0.98881 + u * 0.280 / 0.98881 + b * 0.169 / 0.98881
            else:
                tw = o + (r * off[0] + u * off[1] + b * off[2]) / 0.98881
            miss = place_left(tw, pv, p["cyaw"])
            wk_pose.head(rig, p); bpy.context.view_layer.update()
            vs = body.verts()
            hits = [h for h in mesh.contacts(vs) if "Left" in h[0] and (h[2] > base.get((h[0], h[1]), (0, 0))[0] or h[3] > base.get((h[0], h[1]), (0, 0))[1])]
            depth = max([h[4] for h in hits], default=0.0)
            score = miss * 10 + depth * 20 + len(hits) * 0.2
            row = dict(dcy=dcy, mount=name, pole=pn, miss=round(miss, 3), hits=[(h[0], h[1], h[2], h[3], h[4]) for h in hits],
                       fore=round(rig.forearm_twist("Left"), 1), score=round(score, 3))
            if best is None or score < best["score"]: best = row
        res.append(best)
        print("MOUNT dcy %+4.0f %-15s pole %-10s miss %.3f fore %5.1f hits %s" % (dcy, name, best["pole"], best["miss"], best["fore"],
              [(h[0], h[1], round(h[4], 3)) for h in best["hits"]]))
json.dump(res, open(OUTF, "w"), indent=1)
# стоп-кадры лучших поз (сзади-справа и спереди-слева): только для выбора, в медиа не идут
sc = bpy.context.scene; sc.render.engine = 'BLENDER_WORKBENCH'; sc.render.resolution_x, sc.render.resolution_y = 360, 420
sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'OBJECT'
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 1.1
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.04); mk = bpy.context.active_object; mk.color = (1, 0.2, 0.1, 1)
OUTD = os.path.dirname(OUTF)
for row in res:
    if row["dcy"] != 0.0: continue
    p = K.body_at(3); p = dict(p); p["cyaw"] = p["cyaw"] + row["dcy"]
    wk_pose.body(rig, p); bpy.context.view_layer.update()
    o, r, u, b = chest(); off = CAND[row["mount"]]
    tw = o + r * 0.231 / 0.98881 + u * 0.280 / 0.98881 + b * 0.169 / 0.98881 if off is None else o + (r * off[0] + u * off[1] + b * off[2]) / 0.98881
    place_left(tw, POLES[row["pole"]], p["cyaw"]); wk_pose.head(rig, p); bpy.context.view_layer.update()
    mk.location = tw
    for vn, d in (("backR", Vector((-0.8, 1.0, 0.25))), ("frontL", Vector((0.9, -1.0, 0.25))), ("top", Vector((0.05, 0.3, 1.0)))):
        cam.location = o + d.normalized() * 6; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(OUTD, "m_%s_%s.png" % (row["mount"], vn)); bpy.ops.render.render(write_still=True)
