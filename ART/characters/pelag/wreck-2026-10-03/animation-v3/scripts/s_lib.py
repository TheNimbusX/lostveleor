"""Общие функции сборки Шквала v2 — вынуты без изменений из dash-2026-10-02/animation/scripts/d_author.py
(интерполяция, повороты костей, IK двух костей, перенос стойки как в Unity, сетка для проверки пересечений)."""
import bpy, math
from mathutils import Vector, Quaternion, Matrix
from mathutils.bvhtree import BVHTree
from b_common import wpos
CLEAR_SEARCH = .06
CLEAR_DEEP = .005

def pchip(knots, x):
    """Монотонная кубика Фрича–Карлсона (как в серии сабли): без перелётов узлов."""
    xs = [k[0] for k in knots]; ys = [k[1] for k in knots]
    n = len(xs)
    if x <= xs[0]: return ys[0]
    if x >= xs[-1]: return ys[-1]
    h = [xs[i + 1] - xs[i] for i in range(n - 1)]
    d = [(ys[i + 1] - ys[i]) / h[i] for i in range(n - 1)]
    m = [0.0] * n
    m[0], m[-1] = d[0], d[-1]
    for i in range(1, n - 1):
        if d[i - 1] * d[i] <= 0: m[i] = 0.0
        else:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])
    i = max(j for j in range(n - 1) if xs[j] <= x)
    t = (x - xs[i]) / h[i]
    h00 = 2 * t ** 3 - 3 * t ** 2 + 1; h10 = t ** 3 - 2 * t ** 2 + t
    h01 = -2 * t ** 3 + 3 * t ** 2; h11 = t ** 3 - t ** 2
    return h00 * ys[i] + h10 * h[i] * m[i] + h01 * ys[i + 1] + h11 * h[i] * m[i + 1]


def table(knots, t):
    first = knots[0][1]
    if isinstance(first, (tuple, list)):
        return tuple(pchip([(k, v[c]) for k, v in knots], t) for c in range(len(first)))
    return pchip(knots, t)


# ----------------------------------------------------------- позы и кости ---
def mw3(arm):
    return arm.matrix_world.to_3x3().normalized()


def bone_world_rot(arm, pb):
    return mw3(arm) @ pb.matrix.to_3x3().normalized()


def rotate_world(arm, pb, quat, pivot):
    mw = arm.matrix_world
    turn = Matrix.Translation(pivot) @ quat.to_matrix().to_4x4() @ Matrix.Translation(-pivot)
    pb.matrix = mw.inverted() @ (turn @ (mw @ pb.matrix))
    bpy.context.view_layer.update()


def translate_world(arm, pb, delta):
    mw = arm.matrix_world
    pb.matrix = mw.inverted() @ (Matrix.Translation(delta) @ (mw @ pb.matrix))
    bpy.context.view_layer.update()


def sagittal_lean(arm):
    v = wpos(arm, "mixamorig:Neck") - wpos(arm, "mixamorig:Hips")
    return math.degrees(math.atan2(-v.y, v.z))


def foot_pitch(arm, side):
    v = wpos(arm, f"mixamorig:{side}ToeBase") - wpos(arm, f"mixamorig:{side}Foot")
    return math.degrees(math.atan2(-v.z, Vector((v.x, v.y)).length)), Vector((v.x, v.y, 0)).normalized()


def two_bone(arm, names, target, pole_hint=None, keep_end=False):
    """Две кости к цели: колено/локоть в плоскости подсказки (или своей), конец — по желанию в своей ориентации."""
    up, low, end = (arm.pose.bones[n] for n in names)
    mw = arm.matrix_world
    a = mw @ up.head; b = mw @ low.head; c = mw @ end.head
    end_world = (mw @ end.matrix).copy()
    la = (b - a).length; lb = (c - b).length
    d = target - a
    dist = max(1e-5, min(d.length, (la + lb) * 0.999))
    dn = d.normalized()
    if pole_hint is None:
        pole = (b - a) - (b - a).dot((c - a).normalized()) * (c - a).normalized()
    else:
        pole = pole_hint.copy()
    pole = pole - pole.dot(dn) * dn
    if pole.length < 1e-9: pole = Vector((0, 1, 0)) - dn.y * dn
    pole.normalize()
    x = (la * la - lb * lb + dist * dist) / (2 * dist)
    h = math.sqrt(max(0.0, la * la - x * x))
    b2 = a + dn * x + pole * h
    c2 = a + dn * dist
    rotate_world(arm, up, (b - a).rotation_difference(b2 - a), a)
    b_now = mw @ low.head; c_now = mw @ end.head
    rotate_world(arm, low, (c_now - b_now).rotation_difference(c2 - b_now), b_now)
    if keep_end:
        fw = end_world.copy(); fw.translation = mw @ end.head
        end.matrix = mw.inverted() @ fw
        bpy.context.view_layer.update()
    return (mw @ end.head - target).length


def slerp_dir(a, b, w):
    return a.normalized().slerp(b.normalized(), max(0.0, min(1.0, w))) if a.angle(b) > 1e-6 else b.normalized()


def import_rig(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = [o for o in new if o.type == 'ARMATURE'][0]
    meshes = [o for o in new if o.type == 'MESH']
    return arm, (meshes[0] if meshes else None)


def body_basis(pos):
    up = (pos["mixamorig:Head"] - pos["mixamorig:Hips"]).normalized()
    right = (pos["mixamorig:LeftArm"] - pos["mixamorig:RightArm"]).normalized()
    fwd = up.cross(right).normalized()
    right = fwd.cross(up).normalized()
    return Matrix((right, fwd, up)).transposed().to_quaternion()


def hierarchy(arm):
    order = []
    def walk(b):
        order.append(b.name)
        for c in b.children: walk(c)
    walk(arm.data.bones["mixamorig:Hips"])
    return order


# ------------------------------------------------- сетка v6: части тела ---
def part_of(bone):
    """Часть тела по кости с наибольшим весом вершины (как в независимой проверке)."""
    b = bone.replace("mixamorig:", "")
    if b in ("Hips", "Spine", "Spine1", "Spine2", "LeftShoulder", "RightShoulder"): return "torso"
    if b.startswith("Neck") or b.startswith("Head") or "Eye" in b: return "head"
    for s in ("Left", "Right"):
        if b.startswith(s):
            r = b[len(s):]
            if r == "Arm": return s + "UpperArm"
            if r == "ForeArm": return s + "ForeArm"
            if r.startswith("Hand"): return s + "Hand"
            if r == "UpLeg": return s + "Thigh"
            if r == "Leg": return s + "Shin"
            if r in ("Foot", "ToeBase", "Toe_End"): return s + "Foot"
    return "other"


class Body:
    """Сетка Pelag_v6 после скиннинга: пересечения и глубина между частями тела."""

    def __init__(self, mesh):
        self.mesh = mesh
        vg = {g.index: g.name for g in mesh.vertex_groups}
        self.vpart = []
        for v in mesh.data.vertices:
            best = max(v.groups, key=lambda g: g.weight, default=None)
            self.vpart.append(part_of(vg[best.group]) if best is not None else "other")
        mesh.data.calc_loop_triangles()
        self.tris = [tuple(t.vertices) for t in mesh.data.loop_triangles]
        self.tpart = []
        for t in self.tris:
            ps = {self.vpart[i] for i in t}
            self.tpart.append(ps.pop() if len(ps) == 1 else "seam")

    def verts(self):
        dg = bpy.context.evaluated_depsgraph_get()
        ev = self.mesh.evaluated_get(dg)
        me = ev.to_mesh()
        mw = self.mesh.matrix_world
        vs = [mw @ v.co for v in me.vertices]
        ev.to_mesh_clear()
        return vs

    def bvh(self, vs, parts):
        idx = [i for i, p in enumerate(self.tpart) if p in parts]
        return BVHTree.FromPolygons(vs, [self.tris[i] for i in idx], all_triangles=True), idx

    def overlap(self, vs, a, b):
        """Число треугольников части a, пересекающих часть b (общие вершины не в счёт)."""
        ta, ia = self.bvh(vs, [a]); tb, ib = self.bvh(vs, [b])
        hit = set()
        for x, y in ta.overlap(tb):
            if set(self.tris[ia[x]]) & set(self.tris[ib[y]]): continue
            hit.add(ia[x])
        return hit

    def contact(self, a_parts, b_part, vs=None):
        """Части a_parts в части b_part: (треугольники-пересечения, вершин глубже CLEAR_DEEP,
        наибольшая глубина, центр «вошедших» вершин)."""
        vs = vs or self.verts()
        tri = set()
        for a in a_parts: tri |= self.overlap(vs, a, b_part)
        bad = {v for t in tri for v in self.tris[t]}
        tb, _ = self.bvh(vs, [b_part])
        deep = 0; worst = 0.0
        for i, (v, p) in enumerate(zip(vs, self.vpart)):
            if p not in a_parts: continue
            loc, nor, fi, dist = tb.find_nearest(v, CLEAR_SEARCH)
            if loc is None or (v - loc).dot(nor) >= 0: continue
            worst = max(worst, dist)
            if dist > CLEAR_DEEP: deep += 1; bad.add(i)
        cen = sum((vs[i] for i in bad), Vector()) / len(bad) if bad else None
        return len(tri), deep, worst, cen


def transfer_stance(src, dst, rest):
    """Поза тейка → v6 так же, как RazlomPelagAuthoredClips.Build переносит клип в Unity.

    Привязка тейка — его покой стоя (объект без поворота, масштаб тейка).
    Возвращает K — отношение длины ноги v6 к ноге тейка.
    """
    names = [n for n in dst.data.bones.keys() if n in src.data.bones]
    S = Matrix.Diagonal(src.scale.to_4d())
    bind = {n: S @ src.data.bones[n].matrix_local for n in names}
    bpos = {n: m.translation.copy() for n, m in bind.items()}
    brot = {n: m.to_3x3().normalized().to_quaternion() for n, m in bind.items()}
    tpos = {n: rest[n].translation.copy() for n in names}
    trot = {n: rest[n].to_3x3().normalized().to_quaternion() for n in names}
    A = body_basis(tpos) @ body_basis(bpos).inverted()
    hip_scale = (tpos["mixamorig:Hips"] - tpos["mixamorig:Head"]).length / (bpos["mixamorig:Hips"] - bpos["mixamorig:Head"]).length
    sp = {}
    for n in names:
        m = src.matrix_world @ src.pose.bones[n].matrix
        sp[n] = (m.translation.copy(), m.to_3x3().normalized().to_quaternion())
    mwi = dst.matrix_world.inverted()
    hips = tpos["mixamorig:Hips"] + A @ (sp["mixamorig:Hips"][0] - bpos["mixamorig:Hips"]) * hip_scale
    for n in hierarchy(dst):
        if n not in sp: continue
        pb = dst.pose.bones[n]
        rot = A @ sp[n][1] @ brot[n].inverted() @ A.inverted() @ trot[n]
        head = hips if n == "mixamorig:Hips" else (dst.matrix_world @ pb.matrix).translation
        pb.matrix = mwi @ (Matrix.Translation(head) @ rot.to_matrix().to_4x4())
        bpy.context.view_layer.update()
        kids = [c.name for c in dst.data.bones[n].children if c.name in sp]
        child = next((k for k in kids if "Spine" in k or "Neck" in k), kids[0] if kids else None)
        if child:
            want = A @ (sp[child][0] - sp[n][0])
            cur = (dst.matrix_world @ dst.pose.bones[child].matrix).translation - head
            pb.matrix = mwi @ (Matrix.Translation(head) @ (cur.rotation_difference(want) @ rot).to_matrix().to_4x4())
            bpy.context.view_layer.update()
    leg_t = tpos["mixamorig:Hips"].z - tpos["mixamorig:LeftFoot"].z
    leg_s = bpos["mixamorig:Hips"].z - bpos["mixamorig:LeftFoot"].z
    print("перенос стойки: выравнивание %.1f°, hipScale %.3f" % (math.degrees(A.angle), hip_scale))
    return leg_t / leg_s


def ground_stance(dst, rest):
    """Стойка после переноса висит над землёй (ноги тейка короче) — таз опускается,
    стопы ставятся IK на землю плоско, как в покое v6, с поворотом носка из стойки."""
    mw = dst.matrix_world
    ankle_rest = {s: rest[f"mixamorig:{s}Foot"].translation.z for s in ("Left", "Right")}
    ankles = {s: wpos(dst, f"mixamorig:{s}Foot").copy() for s in ("Left", "Right")}
    yaw = {}
    for s in ("Left", "Right"):
        v = wpos(dst, f"mixamorig:{s}ToeBase") - ankles[s]
        r = rest[f"mixamorig:{s}ToeBase"].translation - rest[f"mixamorig:{s}Foot"].translation
        a, b = Vector((r.x, r.y)).normalized(), Vector((v.x, v.y)).normalized()
        yaw[s] = math.atan2(a.x * b.y - a.y * b.x, a.dot(b))
    drop = min(ankles[s].z - ankle_rest[s] for s in ankles)
    if drop > 0:
        translate_world(dst, dst.pose.bones["mixamorig:Hips"], Vector((0, 0, -drop)))
    for s in ("Left", "Right"):
        target = Vector((ankles[s].x, ankles[s].y, ankle_rest[s]))
        two_bone(dst, [f"mixamorig:{s}UpLeg", f"mixamorig:{s}Leg", f"mixamorig:{s}Foot"], target)
        foot = dst.pose.bones[f"mixamorig:{s}Foot"]
        rot = Matrix.Rotation(yaw[s], 3, 'Z') @ rest[f"mixamorig:{s}Foot"].to_3x3().normalized()
        foot.matrix = mw.inverted() @ (Matrix.Translation(wpos(dst, f"mixamorig:{s}Foot")) @ rot.to_4x4())
        bpy.context.view_layer.update()
        for t in ("ToeBase", "Toe_End"):
            pb = dst.pose.bones[f"mixamorig:{s}{t}"]
            pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0)
        bpy.context.view_layer.update()
    print("стойка: таз опущен на %.3f м, стопы на земле" % max(drop, 0))
    return min(rest[f"mixamorig:{s}{t}"].translation.z for s in ("Left", "Right") for t in ("ToeBase", "Toe_End"))

