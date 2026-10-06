"""Крушение v4 (06.10): тело — принятая серия сабли (Pelag_AN_Sabre1–3, перенос на Pelag_v6 как в Unity),
руки — обе на рукояти якоря (инструменты v3: wk_grip / v3_arms), голова якоря — физика игры (tools/wreck4sim).

Тики Wreck4 → тики сабельного клипа (warp): замах растянут под контакт Крушения, проводка — родным темпом.
"""
import bpy, os, sys, math
from mathutils import Vector, Matrix, Quaternion

HERE = os.path.dirname(os.path.abspath(__file__))
V4 = os.path.abspath(os.path.join(HERE, ".."))
V3S = os.path.abspath(os.path.join(V4, "..", "animation-v3", "scripts"))
if V3S not in sys.path: sys.path.insert(0, V3S)
SABRE = os.path.abspath(os.path.join(V4, "..", "..", "basic-attack-2026-10-01", "animation"))

# Предложение по срокам (тики Sim, 30/с): контакт / конец удара (следующий удар стартует в конце) / кадров в клипе (с хвостом).
# Сабля: 4/8, 4/8, 7/14 (серия 30 тиков). Крушение v4: +1 тик замаха на мах (вес якоря), выпад +1 — серия 34 тика.
TIMING = {
    "Swing1": dict(sabre="Pelag_AN_Sabre1", contact=5, end=9, frames=15, s_contact=4, s_end=8, s_frames=14),
    "Swing2": dict(sabre="Pelag_AN_Sabre2", contact=5, end=9, frames=15, s_contact=4, s_end=8, s_frames=14),
    "Lunge": dict(sabre="Pelag_AN_Sabre3", contact=8, end=16, frames=24, s_contact=7, s_end=14, s_frames=22),
    # снятие (кадр 8 = Swing1 0, хват рукояти в тик 3) и уборка (тело — хвост Lunge 16–24, рукоять за плечом в тик 3,5)
    "Draw": dict(contact=3, end=8, frames=8),
    "Stow": dict(contact=4, end=8, frames=8),
}
LUNGE_M, LUNGE_S = 0.6, (3.0, 7.0)          # выпад сабли: корень 0,6 м за тики сабли 3–7 (стопа в клипе так и стоит)


# Тик Wreck4 → тик сабли: монотонная кубика по узлам. Самый быстрый кусок сабли (рывок к контакту и сразу после)
# растянут на тик больше — иначе руки двуручного хвата крутятся быстрее 70°/тик; замах в начале — чуть быстрее.
KNOTS = {
    "Swing1": [(0, 0.0), (1, 1.25), (2, 2.0), (3, 2.65), (4, 3.3), (5, 4.0), (6, 4.7), (7, 5.6), (8, 6.8), (9, 8.0), (15, 14.0)],
    "Swing2": [(0, 0.0), (1, 1.3), (2, 2.2), (3, 2.9), (4, 3.5), (5, 4.0), (6, 4.7), (7, 5.6), (8, 6.8), (9, 8.0), (15, 14.0)],
    "Lunge": [(0, 0.0), (1, 1.0), (2, 1.9), (3, 2.6), (4, 3.25), (5, 4.0), (6, 5.0), (7, 6.0), (8, 7.0), (16, 14.0), (24, 22.0)],
}


def warp(name, t):
    """Тик Wreck4 (дробный) → тик сабельного клипа."""
    from s_lib import pchip
    return pchip(KNOTS[name], t)


def lunge_root(name, t):
    """Сдвиг корня вперёд (м) — как Sim везёт героя на выпаде; по времени сабли, чтобы стопа клипа не ехала."""
    if name != "Lunge": return 0.0
    s = warp(name, t)
    return LUNGE_M * min(1.0, max(0.0, (s - LUNGE_S[0]) / (LUNGE_S[1] - LUNGE_S[0])))


def _load(path, name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = [o for o in new if o.type == 'ARMATURE'][0]
    for o in new:
        if o is not arm: bpy.data.objects.remove(o, do_unlink=True)
    arm.name = name
    return arm


def _rest(arm, n):
    m = arm.matrix_world @ arm.data.bones[n].matrix_local
    return m.translation.copy(), m.to_3x3().normalized().to_quaternion()


def _pose(arm, n):
    m = arm.matrix_world @ arm.pose.bones[n].matrix
    return m.translation.copy(), m.to_3x3().normalized().to_quaternion()


def _basis(pos):
    up = (pos["mixamorig:Head"] - pos["mixamorig:Hips"]).normalized()
    right = (pos["mixamorig:LeftArm"] - pos["mixamorig:RightArm"]).normalized()
    fwd = up.cross(right).normalized(); right = fwd.cross(up).normalized()
    return Matrix((right, fwd, up)).transposed().to_quaternion()


class SabreSource:
    """Выгруженные сабельные клипы → риг v6 (rig.dst) по формуле RazlomPelagAuthoredClips.Build (как wk_view.Transfer)."""

    def __init__(self, tgt, clips=("Pelag_AN_Sabre1", "Pelag_AN_Sabre2", "Pelag_AN_Sabre3")):
        self.sc = bpy.context.scene
        self.src = {c: _load(os.path.join(SABRE, c + ".fbx"), c) for c in clips}
        self.bind = _load(os.path.join(SABRE, "Pelag_AN_SabreBind.fbx"), "SabreBind")
        self.tgt = t = tgt
        keep = {pb.name: pb.matrix_basis.copy() for pb in t.pose.bones}
        for pb in t.pose.bones: pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()
        self.names = [b.name for b in t.data.bones if b.name.startswith("mixamorig:")]
        self.bpos = {n: _rest(self.bind, n)[0] for n in self.names}
        self.brot = {n: _rest(self.bind, n)[1] for n in self.names}
        self.tpos = {n: _rest(t, n)[0] for n in self.names}
        self.trot = {n: _rest(t, n)[1] for n in self.names}
        self.A = _basis(self.tpos) @ _basis(self.bpos).inverted()
        self.body_bind = (self.bpos["mixamorig:Hips"] - self.bpos["mixamorig:Head"]).length
        self.hip_scale = (self.tpos["mixamorig:Hips"] - self.tpos["mixamorig:Head"]).length / self.body_bind
        self.order = []
        def walk(b):
            self.order.append(b.name)
            for ch in b.children: walk(ch)
        walk(t.data.bones["mixamorig:Hips"])
        self.f0 = {c: int(a.animation_data.action.frame_range[0]) for c, a in self.src.items()}
        for pb in t.pose.bones: pb.matrix_basis = keep[pb.name]
        bpy.context.view_layer.update()

    def _child(self, n, src):
        kids = [c.name for c in self.tgt.data.bones[n].children if c.name in src.data.bones]
        for k in kids:
            if "Spine" in k or "Neck" in k: return k
        return kids[0] if kids else None

    def apply(self, clip, tick, only=None):
        """Поза сабельного клипа clip в тик tick (дробный) на rig.dst. only — набор костей (остальные не трогаются)."""
        src = self.src[clip]; fr = self.f0[clip] + tick
        self.sc.frame_set(int(math.floor(fr)), subframe=fr - math.floor(fr))
        bpy.context.view_layer.update()
        sp = {n: _pose(src, n) for n in self.names}
        t = self.tgt; mwi = t.matrix_world.inverted(); A = self.A
        hips = self.tpos["mixamorig:Hips"] + A @ (sp["mixamorig:Hips"][0] - self.bpos["mixamorig:Hips"]) * self.hip_scale
        for n in self.order:
            if only is not None and n not in only: continue
            pb = t.pose.bones[n]
            rot = A @ sp[n][1] @ self.brot[n].inverted() @ A.inverted() @ self.trot[n]
            head = hips if n == "mixamorig:Hips" else (t.matrix_world @ pb.matrix).translation
            pb.matrix = mwi @ (Matrix.Translation(head) @ rot.to_matrix().to_4x4())
            bpy.context.view_layer.update()
            c = self._child(n, src)
            if c:
                want = A @ (sp[c][0] - sp[n][0])
                cur = (t.matrix_world @ t.pose.bones[c].matrix).translation - head
                fix = cur.rotation_difference(want)
                pb.matrix = mwi @ (Matrix.Translation(head) @ (fix @ rot).to_matrix().to_4x4())
                bpy.context.view_layer.update()


def head_follow(rig, base_relP, tol=3.0):
    """Голова за грудью: наклон головы к груди — как в стойке (± tol°), поворот шеи и головы вокруг оси «вправо» груди."""
    from s_lib import rotate_world
    d = rig.dst
    for _ in range(10):
        e = base_relP - rig.head_m()["relP"]
        if abs(e) <= tol: break
        e = e - math.copysign(tol * 0.5, e)
        cf, cu, cr = rig.hframe("Spine2")
        for n in ("Neck", "Head"):
            rotate_world(d, d.pose.bones["mixamorig:" + n], Quaternion(cr, math.radians(e * .5)), rig.P(n))


def elbow_pole(rig, s):
    """Куда локоть в осях корня (f, l, u) по текущей позе руки — подсказка решению руки."""
    S, E, H = rig.P(s + "Arm"), rig.P(s + "ForeArm"), rig.P(s + "Hand")
    ax = (H - S).normalized()
    p = (E - S) - (E - S).dot(ax) * ax
    if p.length < 1e-4: p = Vector((0.3 if s == "Left" else -0.3, 0, -1))
    p.normalize()
    return (-p.y, p.x, p.z)


BODY_BONES = ['Hips', 'Spine', 'Spine1', 'Spine2', 'Neck', 'Head', 'HeadTop_End',
              'LeftShoulder', 'LeftArm', 'LeftForeArm', 'LeftHand', 'LeftHandMiddle1', 'LeftHandIndex1',
              'RightShoulder', 'RightArm', 'RightForeArm', 'RightHand', 'RightHandMiddle1', 'RightHandIndex1',
              'LeftUpLeg', 'LeftLeg', 'LeftFoot', 'LeftToeBase', 'LeftToe_End',
              'RightUpLeg', 'RightLeg', 'RightFoot', 'RightToeBase', 'RightToe_End']
