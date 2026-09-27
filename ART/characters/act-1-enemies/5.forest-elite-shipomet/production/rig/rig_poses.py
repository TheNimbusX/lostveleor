"""Deformation test poses, authored as world-space rotations about joint heads.

Character faces -Y, left = +X.  Rotation signs: about +X, positive tips a
vertical bone toward -Y (forward); about +Y, negative raises the +X (left) arm.
"""
import math

import bpy
from mathutils import Matrix, Vector

X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))

POSES = {
    "rest": [],
    "arms_up": [
        ("LeftShoulder", "rot", Y, -12), ("LeftArm", "rot", Y, -88), ("LeftForeArm", "rot", Y, -20),
        ("LeftHand", "rot", Y, 25),
        ("RightShoulder", "rot", Y, 12), ("RightArm", "rot", Y, 88), ("RightForeArm", "rot", Y, 20),
        ("RightHand", "rot", Y, -25),
        ("Spine01", "rot", X, -6), ("Head", "rot", X, -12),
    ],
    "crouch": [
        ("Hips", "move", Vector((0, 0.10, -0.46)), 0), ("Spine02", "rot", X, 18), ("Spine01", "rot", X, 10),
        ("Head", "rot", X, -20),
        ("LeftUpLeg", "rot", X, -70), ("LeftLeg", "rot", X, 110), ("LeftFoot", "rot", X, -40),
        ("RightUpLeg", "rot", X, -70), ("RightLeg", "rot", X, 110), ("RightFoot", "rot", X, -40),
        ("LeftArm", "rot", X, -55), ("LeftForeArm", "rot", X, -35),
        ("RightArm", "rot", X, -55), ("RightForeArm", "rot", X, -35),
    ],
    "leg_lift": [
        ("LeftUpLeg", "rot", X, -80), ("LeftLeg", "rot", X, 90), ("LeftFoot", "rot", X, -15),
        ("LeftToeBase", "rot", X, 25),
        ("Spine02", "rot", Z, 15), ("Spine", "rot", Z, 10), ("Head", "rot", Z, -25),
        ("RightArm", "rot", X, -60), ("RightForeArm", "rot", Y, 40),
        ("LeftArm", "rot", X, 35),
    ],
}


def clear(arm):
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def apply(arm, name):
    clear(arm)
    for bone, kind, vec, ang in POSES[name]:
        pb = arm.pose.bones[bone]
        m = pb.matrix.copy()
        if kind == "move":
            pb.matrix = Matrix.Translation(vec) @ m
        else:
            h = pb.head.copy()
            r = Matrix.Translation(h) @ Matrix.Rotation(math.radians(ang), 4, vec) @ Matrix.Translation(-h)
            pb.matrix = r @ m
        bpy.context.view_layer.update()
