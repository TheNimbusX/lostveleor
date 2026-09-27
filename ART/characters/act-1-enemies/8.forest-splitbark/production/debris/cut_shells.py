"""Обломки распада Расщепеня: две половины коры и шляпка гриба — статичные меши для Unity.

Запуск (читает пакет, НЕ сохраняет его):
    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b \
        ../animation/unity_package/ForestSplitter_Package.blend -P cut_shells.py

Зачем. Скорлупа в модели срослась с телом (одна сваренная поверхность Tripo), и
клип Death — только короткая трещина. Распад продаёт картинка: на последнем кадре
трещины вид прячет тело, и две половины коры разлетаются дугами, пока из пыли
выпрыгивают два детёныша (SplitterCombatView).

Откуда меш. Тот же меш 24k, что ForestSplitter_24k_candidate.glb (11910 вершин,
24000 треугольников, те же UV и текстура), но из пакета анимации: в нём есть
скин, и половины снимаются в позе ПОСЛЕДНЕГО КАДРА трещины (ForestSplitter_Death,
кадр 12, раковины уже разошлись) — подмена тела обломками не щёлкает позой.

Как режем. По разметке раковин рига (rig/work/tri_labels.npy: 0 — тело,
1 — shell_L (+X Blender, левый бок зверя), 2 — shell_R): её граница проложена
по вогнутым складкам «раковина — тело» (rig/segment.py). Мелкие острова разметки
отбрасываются, края каждой половины грубо закрываются крышкой (holes_fill) —
плоский тёмный срез: у крышки одна UV на тёмную тёплую кору тела из той же текстуры.

Шляпка гриба. Оранжевые шляпки — часть той же текстуры: по цвету текстуры
ищутся связные оранжевые участки поверхности, берётся самый крупный в пределах
размера шляпки. Меш нормирован (наибольший размер 1 м, центр в нуле) — частица
задаёт размер сама.

Оси и масштаб экспорта — как у пакета персонажа (axis_forward -Z, axis_up Y,
apply_unit_scale): половины стоят в пространстве сущности с началом в ступнях,
в Unity ложатся ровно на тело. Unity-координаты = (-x, z, -y) Blender.

Пишет: razlom/Assets/Resources/VFX/Splitter/ShellHalfL.fbx, ShellHalfR.fbx,
MushroomCap.fbx; рядом со скриптом — shells_report.json и preview_shells.png.
"""
import json
import math
from collections import deque
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Vector

HERE = Path(__file__).resolve().parent
PROD = HERE.parent
REPO = PROD.parents[4]
PKG = PROD / "animation" / "unity_package" / "ForestSplitter_Package.blend"
TRI_LABELS = PROD / "rig" / "work" / "tri_labels.npy"
OUT = REPO / "razlom" / "Assets" / "Resources" / "VFX" / "Splitter"
POSE_ACTION, POSE_FRAME = "ForestSplitter_Death", 12
SIDES = ((1, "ShellHalfL"), (2, "ShellHalfR"))
MIN_ISLAND_FACES = 60
CAP_SIZE = (0.06, 0.20)

assert Path(bpy.data.filepath).resolve() == PKG.resolve(), "открыт не пакет: " + bpy.data.filepath
OUT.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
arm = bpy.data.objects["ARM_ForestSplitter"]
body = bpy.data.objects["SM_ForestSplitter_LOD0"]
assert arm.matrix_world.is_identity and body.matrix_world.is_identity


def unity(p):
    return [round(-p[0], 4), round(p[2], 4), round(-p[1], 4)]


# ---- поза: последний кадр трещины, NLA выключен ----
ad = arm.animation_data or arm.animation_data_create()
for track in ad.nla_tracks:
    track.mute = True
act = bpy.data.actions[POSE_ACTION]
ad.action = act
if len(act.slots):
    ad.action_slot = act.slots[0]
scene.frame_set(POSE_FRAME)
deps = bpy.context.evaluated_depsgraph_get()
posed = bpy.data.meshes.new_from_object(body.evaluated_get(deps), preserve_all_data_layers=True, depsgraph=deps)
posed.name = "ForestSplitter_Death12"
labels = np.load(TRI_LABELS).astype(int)
assert len(labels) == len(posed.polygons) == len(body.data.polygons), (len(labels), len(posed.polygons))
material = body.data.materials[0]

# ---- текстура цвета: тёмная кора для крышки, оранжевые шляпки ----
image = next(i for i in bpy.data.images if i.name.startswith("Color_"))
w, h = image.size
pixels = np.empty(w * h * 4, dtype=np.float32)
image.pixels.foreach_get(pixels)
pixels = pixels.reshape(h, w, 4)
uv_all = np.empty(len(posed.loops) * 2, dtype=np.float32)
posed.uv_layers.active.data.foreach_get("uv", uv_all)
uv_all = uv_all.reshape(-1, 2)


def sample(uv):
    x = np.clip((uv[..., 0] % 1.0) * w, 0, w - 1).astype(int)
    y = np.clip((uv[..., 1] % 1.0) * h, 0, h - 1).astype(int)
    return pixels[y, x, :3]


loop_poly = np.empty(len(posed.loops), dtype=np.int64)
for p in posed.polygons:
    loop_poly[p.loop_start:p.loop_start + p.loop_total] = p.index
body_loops = labels[loop_poly] == 0
colors = sample(uv_all)
lum = colors @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
warm = body_loops & (colors[:, 0] >= colors[:, 1] * 0.95) & (colors[:, 0] > colors[:, 2] * 1.1) & (lum > 0.04)
candidates = np.flatnonzero(warm)
target = np.percentile(lum[candidates], 15)
cap_loop = candidates[np.argmin(np.abs(lum[candidates] - target))]
CAP_UV = tuple(float(v) for v in uv_all[cap_loop])
CAP_COLOR = [round(float(v), 3) for v in colors[cap_loop]]


def islands(bm, faces):
    """Связные по рёбрам группы граней из faces."""
    pool = set(faces)
    groups = []
    while pool:
        seed = pool.pop()
        group, queue = [seed], deque([seed])
        while queue:
            f = queue.popleft()
            for e in f.edges:
                for g in e.link_faces:
                    if g in pool:
                        pool.remove(g)
                        group.append(g)
                        queue.append(g)
        groups.append(group)
    return groups


def trim_ears(bm, passes=4):
    """Грани, торчащие из края двумя рёбрами, — зубья лесенки разметки: срезаем."""
    removed = 0
    for _ in range(passes):
        ears = [f for f in bm.faces if sum(1 for e in f.edges if e.is_boundary) >= 2]
        if not ears:
            break
        bmesh.ops.delete(bm, geom=ears, context="FACES")
        removed += len(ears)
    return removed


def fan_caps(bm):
    """
    Грубая крышка среза: каждое связное кольцо края закрывается веером к своему
    центру. holes_fill давал многоугольник, который на изогнутом крае
    триангулировался иглами наружу и не закрывал кольца с «бабочками» (вершина
    на двух контурах). Здесь у каждого рёбра края ровно один треугольник, и он
    обходит ребро навстречу соседней грани — нормаль крышки смотрит наружу сама.
    """
    halves, parent = [], {}

    def find(x):
        root = x
        while parent.setdefault(root, root) is not root:
            root = parent[root]
        while parent[x] is not root:
            parent[x], x = root, parent[x]
        return root

    for e in bm.edges:
        if not e.is_boundary:
            continue
        loop = e.link_loops[0]
        u, v = loop.vert, loop.link_loop_next.vert
        halves.append((u, v))
        ru, rv = find(u), find(v)
        if ru is not rv:
            parent[ru] = rv
    groups = {}
    for u, v in halves:
        groups.setdefault(find(u), []).append((u, v))
    caps = []
    for group in groups.values():
        ring = {p for pair in group for p in pair}
        center = bm.verts.new(sum((p.co for p in ring), Vector()) / len(ring))
        for u, v in group:
            caps.append(bm.faces.new((v, u, center)))
    return caps, len(groups)


def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, object_types={"MESH"}, axis_forward="-Z", axis_up="Y",
        apply_unit_scale=True, mesh_smooth_type="FACE", use_mesh_modifiers=True, bake_anim=False,
        path_mode="STRIP", embed_textures=False, add_leaf_bones=False)


report = {"source_mesh": str(PKG.name), "same_as": "model/ForestSplitter_24k_candidate.glb (11910 verts / 24000 tris)",
          "pose": {"action": POSE_ACTION, "frame": POSE_FRAME}, "labels": str(TRI_LABELS.relative_to(PROD)),
          "cap_uv": [round(v, 4) for v in CAP_UV], "cap_color": CAP_COLOR,
          "unity_axes": "unity_local = (-x, z, -y) Blender; origin = entity origin at the feet", "parts": {}}
made = []
for label, name in SIDES:
    bm = bmesh.new()
    bm.from_mesh(posed)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if labels[f.index] != label], context="FACES")
    dropped = 0
    for group in islands(bm, list(bm.faces)):
        if len(group) < MIN_ISLAND_FACES:
            dropped += len(group)
            bmesh.ops.delete(bm, geom=group, context="FACES")
    uv = bm.loops.layers.uv.active
    ears = trim_ears(bm)
    caps, contours = fan_caps(bm)
    for f in caps:
        f.smooth = False
        f.material_index = 0
        for loop in f.loops:
            loop[uv].uv = CAP_UV
    bm.normal_update()
    open_edges = sum(1 for e in bm.edges if e.is_boundary)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    co = np.array([v.co[:] for v in mesh.vertices])
    lo, hi = co.min(0), co.max(0)
    center = (lo + hi) / 2
    report["parts"][name] = {
        "label": label, "tris": sum(len(p.vertices) - 2 for p in mesh.polygons), "verts": len(mesh.vertices),
        "cap_faces": len(caps), "cap_contours": contours, "dropped_island_faces": dropped, "trimmed_ears": ears,
        "open_edges_after_fill": open_edges,
        "bounds_unity": {"min": unity((hi[0], hi[1], lo[2])), "max": unity((lo[0], lo[1], hi[2]))},
        "center_unity": unity(center)}
    export(obj, OUT / (name + ".fbx"))
    made.append(obj)

# ---- шляпка гриба: связный оранжевый участок поверхности ----
bm = bmesh.new()
bm.from_mesh(posed)
bm.faces.ensure_lookup_table()
uv = bm.loops.layers.uv.active
orange = []
for f in bm.faces:
    c = sample(np.array([sum((l[uv].uv for l in f.loops), Vector((0, 0))) / len(f.loops)], dtype=np.float32))[0]
    r, g, b = float(c[0]), float(c[1]), float(c[2])
    if r > 0.5 and 0.18 < g < 0.6 and b < 0.35 and r - g > 0.22:
        orange.append(f)
best = None
for group in islands(bm, orange):
    pts = np.array([v.co[:] for f in group for v in f.verts])
    size = float((pts.max(0) - pts.min(0)).max())
    if CAP_SIZE[0] <= size <= CAP_SIZE[1] and len(group) >= 30 and (best is None or len(group) > len(best[0])):
        best = (group, size)
if best is not None:
    keep = set(best[0])
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep], context="FACES")
    pts = np.array([v.co[:] for v in bm.verts])
    center = Vector(((pts.min(0) + pts.max(0)) / 2).tolist())
    size = float((pts.max(0) - pts.min(0)).max())
    normal = sum((f.normal * f.calc_area() for f in bm.faces), Vector())
    bmesh.ops.translate(bm, verts=bm.verts, vec=-center)
    # Верх шляпки — вверх (+Z Blender = +Y Unity), размер — 1 м.
    rot = normal.normalized().rotation_difference(Vector((0, 0, 1))).to_matrix()
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=rot)
    bmesh.ops.scale(bm, verts=bm.verts, vec=Vector((1, 1, 1)) / size)
    mesh = bpy.data.meshes.new("MushroomCap")
    bm.to_mesh(mesh)
    mesh.materials.append(material)
    obj = bpy.data.objects.new("MushroomCap", mesh)
    scene.collection.objects.link(obj)
    report["parts"]["MushroomCap"] = {"tris": sum(len(p.vertices) - 2 for p in mesh.polygons),
                                      "source_size_m": round(size, 4), "orange_faces": len(orange),
                                      "center_unity_in_body": unity(center)}
    export(obj, OUT / "MushroomCap.fbx")
else:
    report["parts"]["MushroomCap"] = {"error": "оранжевый участок нужного размера не найден", "orange_faces": len(orange)}
bm.free()

# ---- проверочный кадр: тело в позе трещины и разведённые половины ----
for ob in scene.objects:
    ob.hide_render = ob not in made
cam_data = bpy.data.cameras.new("PreviewCam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = 2.6
cam = bpy.data.objects.new("PreviewCam", cam_data)
scene.collection.objects.link(cam)
made[0].location.x += 0.35
made[1].location.x -= 0.35
cam.location = (0.0, -3.2, 2.6)
cam.rotation_euler = (math.radians(58), 0.0, 0.0)
scene.camera = cam
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x, scene.render.resolution_y = 1100, 700
scene.render.filepath = str(HERE / "preview_shells.png")
bpy.ops.render.render(write_still=True)
# Второй кадр — срезом к камере: видно, чем закрыта изнанка половины.
made[0].rotation_euler.z, made[0].location = math.radians(90), (0.55, -0.40, 0.0)
made[1].rotation_euler.z, made[1].location = math.radians(-90), (-0.55, -0.42, 0.0)
scene.render.filepath = str(HERE / "preview_shells_cut.png")
bpy.ops.render.render(write_still=True)

(HERE / "shells_report.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
print("SHELLS_REPORT " + json.dumps(report, ensure_ascii=False), flush=True)
