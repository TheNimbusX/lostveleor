"""Deformation test: pose the rig, measure edge stretch per region, render front/side/game views.
blender -b -P pose_test.py -- <rig.blend> <out_dir> [pose ...]
"""
import sys, json, math
from pathlib import Path
import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import pose_lib  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = Path(argv[0]).resolve(), Path(argv[1]).resolve()
names = [a for a in argv[2:] if not a.startswith("--")] or list(pose_lib.POSES)
out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(src))
scene = bpy.context.scene
arm = next(o for o in scene.objects if o.type == "ARMATURE")
mesh = next(o for o in scene.objects if o.type == "MESH")
arm.hide_render = True

scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"; sh.color_type = "TEXTURE"; sh.show_cavity = True
sh.show_shadows = False
scene.render.resolution_x = scene.render.resolution_y = 800
cd = bpy.data.cameras.new("C"); cd.type = "ORTHO"
cam = bpy.data.objects.new("C", cd); scene.collection.objects.link(cam); scene.camera = cam
cd.clip_end = 200
# ground: a flat grey slab under the mob
bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, -0.002))
ground = bpy.context.object
gm = bpy.data.materials.new("GroundGrey"); gm.diffuse_color = (0.32, 0.34, 0.33, 1)
ground.data.materials.append(gm)

rest_co = [v.co.copy() for v in mesh.data.vertices]
edges = [tuple(e.vertices) for e in mesh.data.edges]
rest_len = [(rest_co[a] - rest_co[b]).length for a, b in edges]


def region(p):
    if p.z > 0.85:
        return "dome_top"
    if abs(p.x) > 0.47 and p.y < -0.1:
        return "arm_slab"
    if abs(p.x) > 0.3 and p.y > 0.3 and p.z < 0.6:
        return "hind_leg"
    if abs(p.x) > 0.3 and -0.45 < p.y < 0.0 and 0.45 < p.z < 0.85:
        return "shoulder"
    if p.y < -0.45 and abs(p.x) < 0.3:
        return "face"
    return "body"


def views_for(co):
    lo = Vector((min(c.x for c in co), min(c.y for c in co), min(min(c.z for c in co), 0)))
    hi = Vector((max(c.x for c in co), max(c.y for c in co), max(c.z for c in co)))
    ctr = (lo + hi) / 2
    size = max(hi - lo) * 1.15
    el = math.radians(52); az = math.radians(-60)
    game_dir = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
    return ctr, size, {"front": Vector((0, -1, 0)), "side": Vector((1, 0, 0)), "game": game_dir}


report = {}
for name in names:
    pose_lib.ik_off(arm)
    pose_lib.reset(arm)
    pose_lib.POSES[name](arm)
    bpy.context.view_layer.update()
    ik_err = pose_lib.ik_error(arm) if name == "ik_plant" else None
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    co = [mesh.matrix_world @ v.co for v in ev.data.vertices]
    stats = {}
    for (a, b), L0 in zip(edges, rest_len):
        if L0 < 1e-6:
            continue
        r = (co[a] - co[b]).length / L0
        reg = region(rest_co[a])
        s = stats.setdefault(reg, {"max_stretch": 0.0, "min_ratio": 9.0, "over_1_5": 0, "under_0_6": 0, "edges": 0})
        s["edges"] += 1
        s["max_stretch"] = max(s["max_stretch"], r)
        s["min_ratio"] = min(s["min_ratio"], r)
        s["over_1_5"] += r > 1.5
        s["under_0_6"] += r < 0.6
    for s in stats.values():
        s["max_stretch"] = round(s["max_stretch"], 3); s["min_ratio"] = round(s["min_ratio"], 3)
    report[name] = {"edge_ratio_by_region": stats, "ik_target_error_m": ik_err, "min_z": round(min(c.z for c in co), 3),
                    "max_z": round(max(c.z for c in co), 3)}
    # per-vertex worst edge ratio (stretch or squash), for the heat-map pass
    worst = [1.0] * len(co)
    for (a, b), L0 in zip(edges, rest_len):
        if L0 < 0.004:  # ignore sub-4 mm edges: ratios meaningless there
            continue
        r = (co[a] - co[b]).length / L0
        r = max(r, 1.0 / max(r, 1e-6))
        worst[a] = max(worst[a], r); worst[b] = max(worst[b], r)
    big = sorted(((w, i) for i, w in enumerate(worst)), reverse=True)[:12]
    report[name]["worst_vertices"] = [(round(w, 2), i, [round(c, 3) for c in rest_co[i]]) for w, i in big]
    report[name]["verts_ratio_over_2"] = sum(w > 2 for w in worst)
    attr = mesh.data.color_attributes.get("stretch") or mesh.data.color_attributes.new("stretch", "FLOAT_COLOR", "POINT")
    for i, w in enumerate(worst):
        t = min(1.0, max(0.0, (w - 1.2) / 1.3))
        attr.data[i].color = (0.35 + 0.65 * t, 0.35 * (1 - t) + 0.1, 0.35 * (1 - t), 1)
    ctr, size, views = views_for(co)
    cd.ortho_scale = size
    passes = [("TEXTURE", "")] + ([("VERTEX", "_stretch")] if "--stretch" in sys.argv else [])
    for mode, suffix in passes:
        sh.color_type = mode
        mesh.data.color_attributes.active_color = attr
        vset = views if not suffix else dict(views, back=Vector((0, 1, 0.3)).normalized(),
                                             top=Vector((0.001, 0.2, 1)).normalized())
        for vn, d in vset.items():
            cam.location = ctr + d * 30
            cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = str(out / f"{name}_{vn}{suffix}.png")
            bpy.ops.render.render(write_still=True)
(out / "pose_report.json").write_text(json.dumps(report, indent=1), encoding="utf-8")
print("POSE_DONE", json.dumps({k: {r: (v["max_stretch"], v["over_1_5"]) for r, v in d["edge_ratio_by_region"].items()} for k, d in report.items()}))
