"""Шквал v2: риг v6 + стойка серии сабли (как d_author.py рывка) и замеры позы.
Используется из sq_author.py (blender -b --factory-startup -P sq_author.py -- <out>)."""
import bpy, math
from mathutils import Vector, Quaternion, Matrix
from b_common import wpos, blade
from s_lib import import_rig, transfer_stance, ground_stance, sagittal_lean, foot_pitch, bone_world_rot

CH = r"C:\Users\d.grab\Desktop\the-game\razlom\Assets\Resources\Characters"
V6 = CH + r"\Pelag_v6\Runtime\Pelag_v6_MixamoRig.fbx"
SRC = CH + r"\Pelag_v5\Mixamo\Pelag_MX_SaberCombo.fbx"
BASE_FRAME = 2
SIDES = ("Left", "Right")


def M(n):
    return "mixamorig:" + n


def fl(f, l, u):
    """(вперёд, влево, вверх) в осях корня → мир Blender. Герой смотрит в −Y, его левый бок — +X."""
    return Vector((l, -f, u))


def yaw_of(v):
    """Рысканье направления в градусах: 0 = вперёд (−Y), + к левому боку (+X)."""
    return math.degrees(math.atan2(v.x, -v.y))


class Rig:
    def __init__(self):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        sc = bpy.context.scene
        sc.render.fps = 30
        dst, mesh = import_rig(V6)
        dst.name = "PelagSquall"; mesh.name = "Pelag_v6"
        if dst.animation_data: dst.animation_data.action = None
        for a in list(bpy.data.actions): bpy.data.actions.remove(a)
        self.export_scale = dst.scale.copy()
        dst.scale = (1, 1, 1)
        for pb in dst.pose.bones:
            pb.rotation_mode = 'QUATERNION'
            pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.scale = (1, 1, 1)
        bpy.context.view_layer.update()
        self.rest = {n: (dst.matrix_world @ dst.data.bones[n].matrix_local) for n in dst.data.bones.keys()}
        src, sm = import_rig(SRC)
        sc.frame_set(BASE_FRAME); bpy.context.view_layer.update()
        self.K = transfer_stance(src, dst, self.rest)
        for o in (src, sm): bpy.data.objects.remove(o, do_unlink=True)
        for a in list(bpy.data.actions): bpy.data.actions.remove(a)
        self.toe_floor = ground_stance(dst, self.rest)
        self.dst, self.mesh = dst, mesh
        self.base = {pb.name: pb.matrix_basis.copy() for pb in dst.pose.bones}
        self.reset()
        self._head_axes()
        self.B = self.measure_base()

    # ------------------------------------------------------------------
    def reset(self):
        for pb in self.dst.pose.bones:
            loc, rot, scl = self.base[pb.name].decompose()
            pb.location = loc; pb.rotation_quaternion = rot; pb.scale = scl
        bpy.context.view_layer.update()

    def P(self, n):
        return wpos(self.dst, M(n))

    def pelvis_yaw(self):
        return yaw_of((self.P("LeftUpLeg") - self.P("RightUpLeg")).cross(Vector((0, 0, 1))))

    def chest_yaw(self):
        return yaw_of((self.P("LeftArm") - self.P("RightArm")).cross(Vector((0, 0, 1))))

    def toe_z(self, s):
        return min(self.P(s + "ToeBase").z, self.P(s + "Toe_End").z)

    def _head_axes(self):
        """Оси груди/шеи/головы в покое — как artifacts/tools/pelag-head/headlib.py (замер relP)."""
        R = lambda n: self.rest[M(n)].translation
        up0 = (R("Head") - R("Hips")).normalized()
        right0 = R("RightArm") - R("LeftArm"); right0 = (right0 - right0.dot(up0) * up0).normalized()
        fwd0 = up0.cross(right0).normalized()
        if (R("LeftToeBase") - R("LeftFoot")).dot(fwd0) < 0: fwd0 = -fwd0
        right0 = fwd0.cross(up0).normalized()
        self.HL = {}
        for n in ("Spine2", "Neck", "Head"):
            r = self.rest[M(n)].to_3x3().normalized().to_quaternion().inverted()
            self.HL[n] = (r @ fwd0, r @ up0, r @ right0)

    def hframe(self, n):
        q = bone_world_rot(self.dst, self.dst.pose.bones[M(n)]).to_quaternion()
        return tuple(q @ v for v in self.HL[n])

    def head_m(self):
        asind = lambda x: math.degrees(math.asin(max(-1.0, min(1.0, x))))
        cf, cu, cr = self.hframe("Spine2"); hf, hu, hr = self.hframe("Head")
        return dict(relP=round(asind(hf.dot(cu)), 2),
                    relY=round(math.degrees(math.atan2(hf.dot(cr), hf.dot(cf))), 2),
                    faceYaw=round(yaw_of(hf), 2), faceP=round(asind(hf.z), 2))

    def forearm_twist(self, s):
        """Крутка предплечья вокруг своей оси относительно покоя (°), как в s_probe."""
        q = self.dst.pose.bones[M(s + "ForeArm")].matrix_basis.to_quaternion()
        tw = 2 * math.degrees(math.atan2(q.y, q.w))
        return (tw + 180) % 360 - 180

    def measure_base(self):
        d = self.dst
        B = dict(hips=self.P("Hips").copy(), lean=sagittal_lean(d), pyaw=self.pelvis_yaw(), cyaw=self.chest_yaw(),
                 ankle={s: self.P(s + "Foot").copy() for s in SIDES},
                 pitch={s: foot_pitch(d, s)[0] for s in SIDES},
                 hand={s: self.P(s + "Hand").copy() for s in SIDES},
                 head=self.head_m())
        B["foot_yaw"] = {s: yaw_of(foot_pitch(d, s)[1]) for s in SIDES}
        B["arm_len"] = {s: (self.P(s + "ForeArm") - self.P(s + "Arm")).length + (self.P(s + "Hand") - self.P(s + "ForeArm")).length
                        for s in SIDES}
        r, t = blade(d)
        B["blade"] = (t - r).normalized()
        B["twist_fore"] = {s: self.forearm_twist(s) for s in SIDES}
        # левая кисть от левого плеча в осях груди (f — куда смотрит грудь): так её задают ключи
        rel = self.P("LeftHand") - self.P("LeftArm"); f, l, cy = -rel.y, rel.x, math.radians(B["cyaw"])
        B["hand_relL"] = (f * math.cos(cy) + l * math.sin(cy), -f * math.sin(cy) + l * math.cos(cy), rel.z)
        B["chest_rot"] = bone_world_rot(d, d.pose.bones[M("Spine2")]).to_quaternion()
        return B

    def snapshot(self):
        return {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy()) for pb in self.dst.pose.bones}

    def restore(self, snap):
        for pb in self.dst.pose.bones:
            loc, rot = snap[pb.name]
            pb.location = loc; pb.rotation_quaternion = rot
        bpy.context.view_layer.update()
