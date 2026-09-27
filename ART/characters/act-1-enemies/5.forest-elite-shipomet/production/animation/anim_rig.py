"""Pure-math pose solver for ARM_ForestThorncaster (no depsgraph while authoring).

A Pose keeps armature-space bone matrices.  Every edit transforms a whole
subtree (bone + descendants), so children follow like FK.  At the end
basis() converts to pose_bone.matrix_basis:
    M[b] = M[parent] @ rest[parent]^-1 @ rest[b] @ basis[b]
Character faces -Y, left = +X, Z up, ground z = 0.
"""
import math

from mathutils import Matrix, Quaternion, Vector

ARM = "ARM_ForestThorncaster"
MESH = "SM_ForestThorncaster_LOD0"


class Skeleton:
    def __init__(self, arm_obj):
        bones = arm_obj.data.bones
        self.names = [b.name for b in bones]
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        self.length = {b.name: b.length for b in bones}
        self.children = {n: [] for n in self.names}
        for n, p in self.parent.items():
            if p:
                self.children[p].append(n)
        self.sub = {n: self._subtree(n) for n in self.names}

    def _subtree(self, n):
        out = [n]
        for c in self.children[n]:
            out += self._subtree(c)
        return out

    def rest_head(self, n):
        return self.rest[n].translation.copy()

    def rest_tail(self, n):
        return self.rest[n] @ Vector((0, self.length[n], 0))


class Pose:
    def __init__(self, sk):
        self.sk = sk
        self.M = {n: m.copy() for n, m in sk.rest.items()}

    # -- queries
    def head(self, n):
        return self.M[n].translation.copy()

    def tail(self, n):
        return self.M[n] @ Vector((0, self.sk.length[n], 0))

    def dir(self, n):
        return (self.tail(n) - self.head(n)).normalized()

    def delta(self, n):
        """Rigid transform that carries bone n from rest to now."""
        return self.M[n] @ self.sk.rest[n].inverted()

    # -- edits
    def transform(self, n, T):
        for c in self.sk.sub[n]:
            self.M[c] = T @ self.M[c]

    def rotate(self, n, R, pivot=None):
        """World rotation R (Quaternion/Matrix3) of subtree n about pivot (default: bone head)."""
        p = self.head(n) if pivot is None else Vector(pivot)
        R4 = (R.to_matrix() if isinstance(R, Quaternion) else R).to_4x4()
        self.transform(n, Matrix.Translation(p) @ R4 @ Matrix.Translation(-p))

    def translate(self, n, v):
        self.transform(n, Matrix.Translation(Vector(v)))

    def aim(self, n, direction):
        """Minimal rotation so bone n points along direction (keeps inherited twist)."""
        d = Vector(direction)
        if d.length < 1e-9:
            return
        self.rotate(n, self.dir(n).rotation_difference(d.normalized()))

    def set_rotation(self, n, q_world):
        """Bone n gets world orientation q_world @ rest orientation (about its head)."""
        want = q_world @ self.sk.rest[n].to_quaternion()
        cur = self.M[n].to_quaternion()
        self.rotate(n, want @ cur.inverted())

    def ik2(self, upper, lower, target, pole):
        """Fixed-length two-bone IK: lower's tail -> target, bend toward pole. Returns reach shortfall (m)."""
        a, b = self.sk.length[upper], self.sk.length[lower]
        A = self.head(upper)
        T = Vector(target)
        v = T - A
        dist = v.length
        d = max(abs(a - b) + 1e-4, min(a + b - 1e-5, dist))
        axis = v.normalized()
        p = Vector(pole)
        p = (p - axis * p.dot(axis))
        if p.length < 1e-6:
            p = axis.orthogonal()
        p.normalize()
        ca = max(-1.0, min(1.0, (a * a + d * d - b * b) / (2 * a * d)))
        knee = A + axis * (a * ca) + p * (a * math.sqrt(max(0.0, 1 - ca * ca)))
        self.aim(upper, knee - A)
        self.aim(lower, (A + axis * d) - self.head(lower))
        return max(0.0, dist - d)

    # -- output
    def basis(self):
        sk, out = self.sk, {}
        for n in sk.names:
            p = sk.parent[n]
            if p is None:
                ref = sk.rest[n]
            else:
                ref = self.M[p] @ sk.rest[p].inverted() @ sk.rest[n]
            out[n] = ref.inverted() @ self.M[n]
        return out


def euler_q(pitch=0.0, roll=0.0, yaw=0.0):
    """Degrees. pitch about +X (positive leans an upright bone forward, -Y), roll about +Y, yaw about +Z (turn left)."""
    qx = Quaternion((1, 0, 0), math.radians(pitch))
    qy = Quaternion((0, 1, 0), math.radians(roll))
    qz = Quaternion((0, 0, 1), math.radians(yaw))
    return qz @ qy @ qx
