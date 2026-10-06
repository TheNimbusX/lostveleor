"""Шквал v2: сцена съёмки на Pelag_v6 в цвете и перенос ВЫГРУЖЕННЫХ FBX как в Unity
(RazlomPelagAuthoredClips.Build — та же формула, что в dash-2026-10-02/animation/scripts/d_render_v6.py).
Используется из sq_render.py."""
import bpy, os, math
from mathutils import Vector, Matrix
from b_common import make_blade_object, blade

CH = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters"
V6 = CH + r"\Pelag_v6\Runtime\Pelag_v6_MixamoRig.fbx"
TEX = CH + r"\Pelag_v6\Pelag_v6_BaseColor.jpg"
BLADE_SHOW = 0.75


def load(path, name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    arm = [o for o in new if o.type == 'ARMATURE'][0]
    arm.name = name
    return arm, [o for o in new if o.type == 'MESH']


def rest_world(arm, n):
    m = arm.matrix_world @ arm.data.bones[n].matrix_local
    return m.translation.copy(), m.to_3x3().normalized().to_quaternion()


def pose_world(arm, n):
    m = arm.matrix_world @ arm.pose.bones[n].matrix
    return m.translation.copy(), m.to_3x3().normalized().to_quaternion()


def body_basis(pos):
    up = (pos["mixamorig:Head"] - pos["mixamorig:Hips"]).normalized()
    right = (pos["mixamorig:LeftArm"] - pos["mixamorig:RightArm"]).normalized()
    fwd = up.cross(right).normalized(); right = fwd.cross(up).normalized()
    return Matrix((right, fwd, up)).transposed().to_quaternion()


class Transfer:
    """Клипы (каждый — своя арматура-источник) → Pelag_v6 по формуле Unity."""

    def __init__(self, anim_dir, clips, bind_name):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        self.sc = bpy.context.scene; self.sc.render.fps = 30
        self.src = {}
        for c in clips:
            a, ms = load(os.path.join(anim_dir, c + ".fbx"), c)
            for m in ms: bpy.data.objects.remove(m, do_unlink=True)
            self.src[c] = a
        self.bind, _ = load(os.path.join(anim_dir, bind_name + ".fbx"), "Bind")
        self.tgt, self.meshes = load(V6, "Tgt")
        t = self.tgt; t.scale = (1, 1, 1)
        if t.animation_data: t.animation_data.action = None
        for pb in t.pose.bones:
            pb.rotation_mode = 'QUATERNION'; pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
        bpy.context.view_layer.update()
        self.names = [b.name for b in t.data.bones if b.name.startswith("mixamorig:")]
        self.bpos = {n: rest_world(self.bind, n)[0] for n in self.names}
        self.brot = {n: rest_world(self.bind, n)[1] for n in self.names}
        self.tpos = {n: rest_world(t, n)[0] for n in self.names}
        self.trot = {n: rest_world(t, n)[1] for n in self.names}
        self.A = body_basis(self.tpos) @ body_basis(self.bpos).inverted()
        self.body_bind = (self.bpos["mixamorig:Hips"] - self.bpos["mixamorig:Head"]).length
        self.hip_scale = (self.tpos["mixamorig:Hips"] - self.tpos["mixamorig:Head"]).length / self.body_bind
        self.order = []
        def walk(b):
            self.order.append(b.name)
            for ch in b.children: walk(ch)
        walk(t.data.bones["mixamorig:Hips"])
        self.range = {c: tuple(int(x) for x in a.animation_data.action.frame_range) for c, a in self.src.items()}

    def child_of(self, n, src):
        kids = [c.name for c in self.tgt.data.bones[n].children if c.name in src.data.bones]
        for k in kids:
            if "Spine" in k or "Neck" in k: return k
        return kids[0] if kids else None

    def apply(self, clip, tick):
        """tick — кадр клипа от 0 (может быть дробным: перевремённый полёт)."""
        src = self.src[clip]; f0 = self.range[clip][0]
        fr = f0 + tick
        self.sc.frame_set(int(math.floor(fr)), subframe=fr - math.floor(fr))
        bpy.context.view_layer.update()
        sp = {n: pose_world(src, n) for n in self.names}
        t = self.tgt; mwi = t.matrix_world.inverted(); A = self.A
        hips_rest = self.tpos["mixamorig:Hips"]
        hips = hips_rest + A @ (sp["mixamorig:Hips"][0] - self.bpos["mixamorig:Hips"]) * self.hip_scale
        ratio = (hips - hips_rest).length / (self.body_bind * self.hip_scale)
        for n in self.order:
            pb = t.pose.bones[n]
            rot = A @ sp[n][1] @ self.brot[n].inverted() @ A.inverted() @ self.trot[n]
            head = (t.matrix_world @ hips) if n == "mixamorig:Hips" else (t.matrix_world @ pb.matrix).translation
            pb.matrix = mwi @ (Matrix.Translation(head) @ (t.matrix_world.to_quaternion() @ rot).to_matrix().to_4x4())
            bpy.context.view_layer.update()
            c = self.child_of(n, src)
            if c:
                want = t.matrix_world.to_quaternion() @ (A @ (sp[c][0] - sp[n][0]))
                cur = (t.matrix_world @ t.pose.bones[c].matrix).translation - head
                fix = cur.rotation_difference(want)
                pb.matrix = mwi @ (Matrix.Translation(head) @ (fix @ t.matrix_world.to_quaternion() @ rot).to_matrix().to_4x4())
                bpy.context.view_layer.update()
        return ratio
