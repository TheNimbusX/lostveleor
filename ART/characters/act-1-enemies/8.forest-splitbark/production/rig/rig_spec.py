"""Bone layout for ARM_ForestSplitter (Blender space: front -Y, up +Z, metres, adult 1.3 m).

Landmarks were measured on ForestSplitter_24k_candidate (cross-sections in rig/work):
feet centres FL(+0.45,-0.32) FR(-0.46,-0.33) HL(+0.43,+0.49) HR(-0.40,+0.49); beak tip
(0,-0.61,0.41); back strip (V floor between the shells) z~1.0; shell/body creases at
(|x|~0.2, z~0.88) on the back and (|x|~0.33, z~0.55) in the belly pockets.
Character left = +X (suffix _L), right = -X (_R).
"""

MESH_NAME = "SM_ForestSplitter_LOD0"
ARM_NAME = "ARM_ForestSplitter"

# name: (head, tail, parent, connect, roll_z_axis)  roll_z_axis = vector the bone Z should face
SPINE = [
    ("root", (0, 0, 0), (0, 0, 0.15), None, False, (0, -1, 0)),
    ("body", (0, 0.30, 0.60), (0, 0.05, 0.64), "root", False, (0, 0, 1)),
    ("spine", (0, 0.05, 0.64), (0, -0.22, 0.64), "body", True, (0, 0, 1)),
    ("neck", (0, -0.22, 0.64), (0, -0.38, 0.64), "spine", False, (0, 0, 1)),  # free to translate: head pops out of the shell
    ("head", (0, -0.38, 0.64), (0, -0.60, 0.50), "neck", True, (0, 0, 1)),
]

# Shell hinge: bone head = pivot on the belly-pocket crease, tail up along the shell.
# Z axis faces outward so +X rotation opens the half for both sides.
SHELL_PIVOT = (0.40, -0.05, 0.47)
SHELL_TIP = (0.30, -0.05, 1.02)


def shells():
    out = []
    for side, s in (("L", 1), ("R", -1)):
        px, py, pz = SHELL_PIVOT
        tx, ty, tz = SHELL_TIP
        out.append((f"shell_{side}", (s * px, py, pz), (s * tx, ty, tz), "spine", False, (s, 0, 0)))
    return out


# leg chains: upper (shoulder/hip -> elbow/knee), lower (-> ankle), foot (-> toe tip)
LEG_POINTS = {
    "front": [(0.29, -0.24, 0.52), (0.44, -0.25, 0.31), (0.455, -0.30, 0.12), (0.46, -0.45, 0.035)],
    "hind": [(0.22, 0.33, 0.58), (0.33, 0.45, 0.37), (0.42, 0.49, 0.12), (0.43, 0.36, 0.035)],
}
LEG_PARENT = {"front": "spine", "hind": "body"}


def legs():
    out = []
    for kind, pts in LEG_POINTS.items():
        for side, s in (("L", 1), ("R", -1)):
            p = [(s * x, y, z) for x, y, z in pts]
            pre = f"leg_{kind}_{side}_"
            out.append((pre + "upper", p[0], p[1], LEG_PARENT[kind], False, (0, -1, 0)))
            out.append((pre + "lower", p[1], p[2], pre + "upper", True, (0, -1, 0)))
            out.append((pre + "foot", p[2], p[3], pre + "lower", True, (0, 0, 1)))
    return out


def all_bones():
    return SPINE + shells() + legs()


# No jaw bone: the scan's beak is one solid wedge with no mouth line or cavity. A probe jaw
# (r01 build, lower half of the wedge) only stretched the beak longer and never read as a gape.
# A bite gape needs a mouth cut + interior in the model first (see rig_report.json).
# Body verts closer than this (surface metres) to a shell crease take harmonic shell influence.
SHELL_BAND_M = 0.25
HEAD_BONES = ("neck", "head")
