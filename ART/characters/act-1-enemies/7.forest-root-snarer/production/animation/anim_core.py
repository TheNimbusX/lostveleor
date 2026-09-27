"""Pose solver for the ForestRootSnarer clips (armature space = world, metres, faces -Y, L = +X).

FK with body-frame rotation deltas + analytic two-bone IK:
  slabs  : arm_upper + arm_lower -> knuckle socket (arm_lower tail = L/R_hand head)
  hinds  : leg_upper + leg_lower -> ankle (leg_lower tail), foot orientation set in world.
Ground helpers put the rigid slab / foot sole exactly on a requested height.
A pose is a plain dict (see solve()); the clip modules only build such dicts per frame.
"""
import math
from mathutils import Euler, Matrix, Quaternion, Vector

ARM_NAME = "ARM_ForestRootSnarer"
MESH_NAME = "SM_ForestRootSnarer_LOD0"
SIDES = ("L", "R")
DELTA_BONES = ("spine_01", "spine_02", "neck", "head", "jaw", "L_clavicle", "R_clavicle")


def qdeg(e):
    return Euler([math.radians(a) for a in e], "XYZ").to_quaternion()


def rot_about(m, q, pivot):
    return Matrix.Translation(pivot) @ q.to_matrix().to_4x4() @ Matrix.Translation(-pivot) @ m


def tail(m, length):
    return m @ Vector((0, length, 0))


class Rig:
    def __init__(self, arm, mesh):
        self.arm, self.mesh = arm, mesh
        bones = arm.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        self.length = {b.name: b.length for b in bones}
        order, todo = [], [b for b in bones if b.parent is None]
        while todo:
            b = todo.pop(0)
            order.append(b.name)
            todo.extend(b.children)
        self.order = order
        self.deform = [n for n in order if bones[n].use_deform]
        self.keyed = ["root"] + self.deform
        gi = {g.name: g.index for g in mesh.vertex_groups}

        def w(v, name):
            return next((g.weight for g in v.groups if g.group == gi[name]), 0.0)

        vs = mesh.data.vertices
        self.slab = {s: [vs[i].co.copy() for i in range(len(vs)) if w(vs[i], f"{s}_arm_lower") >= 0.99] for s in SIDES}
        # foot sole points relative to the rest ankle (foot bone head)
        self.sole = {}
        for s in SIDES:
            ank = self.rest[f"{s}_foot"].translation
            pts = [vs[i].co - ank for i in range(len(vs)) if w(vs[i], f"{s}_foot") >= 0.6]
            pts.sort(key=lambda c: c.z)
            self.sole[s] = pts[:60]
        # the same sole as the game deforms it (linear blend skinning, full weights): the foot mesh
        # is weighted ~50/50 to the shin, so a rigid-foot plant still lets the sole drift (foot_pin)
        names = {g.index: g.name for g in mesh.vertex_groups}
        self.rest_inv = {n: m.inverted() for n, m in self.rest.items()}
        self.sole_skin = {}
        for s in SIDES:
            ids = [i for i in range(len(vs)) if w(vs[i], f"{s}_foot") >= 0.5]
            zmin = min(vs[i].co.z for i in ids)
            self.sole_skin[s] = [(vs[i].co.copy(), [(names[g.group], g.weight) for g in vs[i].groups
                                                    if g.weight > 0 and names[g.group] in self.rest])
                                 for i in ids if vs[i].co.z < zmin + 0.03]
        self.bend_rest = {}
        for s in SIDES:
            for kind, up, lo in (("arm", f"{s}_arm_upper", f"{s}_arm_lower"), ("leg", f"{s}_leg_upper", f"{s}_leg_lower")):
                a = self.rest[up].translation
                j = self.rest[lo].translation
                t = tail(self.rest[lo], self.length[lo])
                ax = (t - a).normalized()
                n = (j - a) - ax * (j - a).dot(ax)
                self.bend_rest[(s, kind)] = n.normalized()

    # ------------------------------------------------------------------ helpers
    def inherit(self, M, name):
        p = self.parent[name]
        return M[p] @ self.rest[p].inverted() @ self.rest[name]

    def aim(self, m, length, target):
        head = m.translation.copy()
        cur = (tail(m, length) - head).normalized()
        q = cur.rotation_difference((target - head).normalized())
        return rot_about(m, q, head)

    def two_bone(self, M, D, s, kind, target, twist=0.0, bend=None):
        up, lo = (f"{s}_arm_upper", f"{s}_arm_lower") if kind == "arm" else (f"{s}_leg_upper", f"{s}_leg_lower")
        mu = self.inherit(M, up)
        l1, l2 = self.length[up], self.length[lo]
        S = mu.translation.copy()
        d = Vector(target) - S
        dist = d.length
        err = max(0.0, dist - (l1 + l2)) + max(0.0, abs(l1 - l2) - dist)
        dist = min(max(dist, abs(l1 - l2) + 1e-4), l1 + l2 - 1e-5)
        ax = d.normalized()
        hint = bend if bend is not None else (D[self.parent[up]] @ self.bend_rest[(s, kind)])
        n = hint - ax * hint.dot(ax)
        n = n.normalized() if n.length > 1e-6 else Vector((1 if s == "L" else -1, 0, 0))
        a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist)
        E = S + ax * a + n * math.sqrt(max(0.0, l1 * l1 - a * a))
        T = S + ax * dist
        mu = self.aim(mu, l1, E)
        M[up] = mu
        ml = self.aim(mu @ self.rest[up].inverted() @ self.rest[lo], l2, T)
        if twist:
            axis = (T - E).normalized()
            ml = rot_about(ml, Quaternion(axis, math.radians(twist)), E)
        M[lo] = ml
        return err

    def slab_min_z(self, m_lower, s):
        tr = m_lower @ self.rest[f"{s}_arm_lower"].inverted()
        return min((tr @ c).z for c in self.slab[s])

    def skinned_sole(self, M, s):
        """World positions of the sole vertices under linear blend skinning for the bone matrices M."""
        out = []
        for co, ws in self.sole_skin[s]:
            p, tw = Vector((0, 0, 0)), 0.0
            for b, wt in ws:
                if b in M:
                    p += wt * (M[b] @ self.rest_inv[b] @ co)
                    tw += wt
            out.append(p / tw)
        return out

    def slab_point(self, m_lower, s, c):
        """World position of the slab material point c (rest space) for the posed arm_lower."""
        return m_lower @ self.rest[f"{s}_arm_lower"].inverted() @ c

    def slab_low_rest(self, m_lower, s):
        """Rest-space coordinate of the slab vertex that is lowest in this pose."""
        tr = m_lower @ self.rest[f"{s}_arm_lower"].inverted()
        return min(self.slab[s], key=lambda c: (tr @ c).z).copy()

    def slab_low_point(self, m_lower, s):
        tr = m_lower @ self.rest[f"{s}_arm_lower"].inverted()
        return min((tr @ c for c in self.slab[s]), key=lambda p: p.z)

    # ------------------------------------------------------------------ solve
    def solve(self, P):
        """P keys: pelvis_off (xyz), pelvis_rot (deg xyz), rot {bone: deg xyz},
        hand {s: (x,y,z) socket}, hand_ground {s: slab min z or None}, hand_twist {s: deg},
        foot {s: (x,y,sole_z)}, foot_rot {s: deg xyz world}. Returns (M, info)."""
        M, D = {}, {}
        info = {"reach_err": {}, "slab_min_z": {}}
        rot = P.get("rot", {})
        for name in self.order:
            if name == "root":
                M[name], D[name] = self.rest[name].copy(), Quaternion()
                continue
            if name == "pelvis":
                q = qdeg(P.get("pelvis_rot", (0, 0, 0)))
                m = rot_about(self.rest[name], q, self.rest[name].translation)
                M[name] = Matrix.Translation(Vector(P.get("pelvis_off", (0, 0, 0)))) @ m
                D[name] = q
                continue
            par = self.parent[name]
            if name.endswith(("arm_upper", "arm_lower", "leg_upper", "leg_lower", "_foot", "_hand")):
                continue  # limbs solved below, after their parents exist
            m = self.inherit(M, name)
            if name in rot:
                qw = D[par] @ qdeg(rot[name]) @ D[par].inverted()
                m = rot_about(m, qw, m.translation)
                D[name] = qw @ D[par]
            else:
                D[name] = D[par]
            M[name] = m
        for s in SIDES:
            # slab
            tgt = Vector(P["hand"][s])
            ground = P.get("hand_ground", {}).get(s)
            twist = P.get("hand_twist", {}).get(s, 0.0)
            err = 0.0
            pin = P.get("hand_pin", {}).get(s)
            if pin is not None:
                # hand_pin {s: (slab point in rest space, world point)}: the socket target is moved
                # until that material point of the rigid slab sits exactly on the world point
                # (a planted slab pivots about its contact instead of about the knuckle socket)
                # mode "xy": only the ground-plane position is pinned and the slab is snapped so its
                # lowest vertex sits on hand_ground (rolling contact, see clip_walk)
                c, p = Vector(pin[0]), Vector(pin[1])
                xy = len(pin) > 2 and pin[2] == "xy"
                for _ in range(16):
                    err = self.two_bone(M, D, s, "arm", tgt, twist)
                    d = p - self.slab_point(M[f"{s}_arm_lower"], s, c)
                    if xy:
                        d.z = (ground or 0.0) - self.slab_min_z(M[f"{s}_arm_lower"], s)
                    if d.length < 5e-5:
                        break
                    tgt = tgt + d
                d = p - self.slab_point(M[f"{s}_arm_lower"], s, c)
                info.setdefault("pin_err", {})[s] = d.to_2d().length if xy else d.length
            for _ in range(0 if pin is not None else (4 if ground is not None else 1)):
                err = self.two_bone(M, D, s, "arm", tgt, twist)
                if ground is None:
                    break
                dz = ground - self.slab_min_z(M[f"{s}_arm_lower"], s)
                if abs(dz) < 2e-4:
                    break
                tgt = tgt + Vector((0, 0, dz))
            info["reach_err"][f"{s}_arm"] = err
            info.setdefault("hand_target", {})[s] = tuple(tgt)
            info["slab_min_z"][s] = self.slab_min_z(M[f"{s}_arm_lower"], s)
            M[f"{s}_hand"] = self.inherit(M, f"{s}_hand")
            # hind leg
            fx, fy, sole_z = P["foot"][s]
            qf = qdeg(P.get("foot_rot", {}).get(s, (0, 0, 0)))
            low = min((qf @ c).z for c in self.sole[s])
            ankle = Vector((fx, fy, sole_z - low))
            if P.get("ankle", {}).get(s) is not None:      # explicit ankle (walk rolling table)
                ankle = Vector(P["ankle"][s])
            # foot_pin {s: (sole index, (x, y))}: move the ankle until that skinned sole vertex sits
            # on (x, y) and the lowest skinned sole vertex on sole_z (planted in game, not just
            # in the rigid-foot approximation)
            fpin = P.get("foot_pin", {}).get(s)
            for _ in range(10 if fpin is not None else 1):
                info["reach_err"][f"{s}_leg"] = self.two_bone(M, D, s, "leg", ankle)
                ank = tail(M[f"{s}_leg_lower"], self.length[f"{s}_leg_lower"])
                mf = (qf.to_matrix() @ self.rest[f"{s}_foot"].to_3x3()).to_4x4()
                mf.translation = ank
                M[f"{s}_foot"] = mf
                if fpin is None:
                    break
                pts = self.skinned_sole(M, s)
                i, (x, y) = fpin
                d = Vector((x - pts[i].x, y - pts[i].y, sole_z - min(p.z for p in pts)))
                if d.length < 5e-5:
                    break
                ankle = ankle + d
            info.setdefault("ankle_target", {})[s] = tuple(ankle)
        return M, info

    def freeze(self, P):
        """Replace ground-snapped slab targets by the explicit socket positions they solved to."""
        import copy
        _, info = self.solve(P)
        Q = copy.deepcopy(P)
        for s in SIDES:
            Q["hand"][s] = info["hand_target"][s]
            Q["hand_ground"][s] = None
        Q.pop("hand_pin", None)
        return Q

    def shoulder(self, P, s):
        """Posed shoulder junction (arm_upper head) for pose P."""
        M, _ = self.solve(P)
        return M[f"{s}_arm_upper"].translation.copy()

    def apply(self, P):
        """Pose the armature (matrix_basis) with P; returns (M, info)."""
        import bpy
        M, info = self.solve(P)
        B = self.basis(M)
        for name, mb in B.items():
            self.arm.pose.bones[name].matrix_basis = mb
        bpy.context.view_layer.update()
        return M, info

    def mesh_co(self):
        import bpy
        import numpy as np
        dg = bpy.context.evaluated_depsgraph_get()
        ev = self.mesh.evaluated_get(dg)
        co = np.empty(len(ev.data.vertices) * 3)
        ev.data.vertices.foreach_get("co", co)
        return co.reshape(-1, 3)

    def body_mask(self):
        """Vertices dominated by the torso/head bones (belly, chest, chin, dome)."""
        import numpy as np
        if getattr(self, "_body_mask", None) is None:
            names = {g.index: g.name for g in self.mesh.vertex_groups}
            body = {"pelvis", "spine_01", "spine_02", "neck", "head", "jaw"}
            m = np.zeros(len(self.mesh.data.vertices), bool)
            for v in self.mesh.data.vertices:
                w = sum(g.weight for g in v.groups if names[g.group] in body)
                m[v.index] = w > 0.5
            self._body_mask = m
        return self._body_mask

    def basis(self, M):
        out = {}
        for name in self.keyed:
            par = self.parent[name]
            inh = self.rest[name] if par is None else M[par] @ self.rest[par].inverted() @ self.rest[name]
            out[name] = inh.inverted() @ M[name]
        return out


def lerp(a, b, t):
    if isinstance(a, (int, float)):
        return a + (b - a) * t
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)
