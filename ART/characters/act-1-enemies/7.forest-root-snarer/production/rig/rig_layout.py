"""Bone layout for ForestRootSnarer (model faces -Y, up +Z, metres, origin on the ground).

Side naming is the creature's own side: facing -Y, its LEFT is +X  (L_* = +X, R_* = -X),
same convention as Stonehoof (front_left at +X) and Wendigo (L = creature left).
The candidate is not mirror-symmetric (Tripo): the +X hind leg sits ~0.18 m further back
than the -X one, so the leg joints are measured per side (slice centroids, rig/work/views).
"""

# name: (head, tail, parent, connect)
BONES = [
    ("root",        (0, 0, 0),            (0, 0, 0.12),          None,        False),
    ("pelvis",      (0, 0.45, 0.62),      (0, 0.18, 0.72),       "root",      False),
    ("spine_01",    (0, 0.18, 0.72),      (0, -0.08, 0.76),      "pelvis",    True),
    ("spine_02",    (0, -0.08, 0.76),     (0, -0.30, 0.72),      "spine_01",  True),
    ("neck",        (0, -0.30, 0.72),     (0, -0.44, 0.66),      "spine_02",  True),
    ("head",        (0, -0.44, 0.66),     (0, -0.72, 0.64),      "neck",      True),
    ("jaw",         (0, -0.42, 0.47),     (0, -0.70, 0.40),      "head",      False),
    # arms: +X = creature left. Short upper arm = shoulder junction; lower arm = the whole
    # bark slab incl. the fist, 100% rigid; hand = knuckle-contact socket at the slab's ground
    # point (exported, no skin weights: a wrist bend would crease the bark).
    ("L_clavicle",  (0.14, -0.20, 0.80),  (0.40, -0.24, 0.68),   "spine_02",  False),
    ("L_arm_upper", (0.40, -0.24, 0.68),  (0.55, -0.27, 0.66),   "L_clavicle", True),
    ("L_arm_lower", (0.55, -0.27, 0.66),  (0.60, -0.60, 0.10),   "L_arm_upper", True),
    ("L_hand",      (0.60, -0.60, 0.10),  (0.61, -0.72, 0.03),   "L_arm_lower", True),
    ("R_clavicle",  (-0.14, -0.22, 0.80), (-0.40, -0.28, 0.68),  "spine_02",  False),
    ("R_arm_upper", (-0.40, -0.28, 0.68), (-0.54, -0.31, 0.66),  "R_clavicle", True),
    ("R_arm_lower", (-0.54, -0.31, 0.66), (-0.60, -0.66, 0.10),  "R_arm_upper", True),
    ("R_hand",      (-0.60, -0.66, 0.10), (-0.60, -0.77, 0.03),  "R_arm_lower", True),
    # hind legs (short column legs). Knee set forward inside the thigh mass so the chain has a
    # clear forward bend (stable IK pole) and 3-5 cm of reach beyond the rest hip-ankle distance.
    ("L_leg_upper", (0.36, 0.50, 0.56),   (0.52, 0.56, 0.37),    "pelvis",    False),
    ("L_leg_lower", (0.52, 0.56, 0.37),   (0.55, 0.74, 0.15),    "L_leg_upper", True),
    ("L_foot",      (0.55, 0.74, 0.15),   (0.58, 0.58, 0.09),    "L_leg_lower", True),
    ("R_leg_upper", (-0.36, 0.42, 0.56),  (-0.49, 0.38, 0.37),   "pelvis",    False),
    ("R_leg_lower", (-0.49, 0.38, 0.37),  (-0.53, 0.55, 0.13),   "R_leg_upper", True),
    ("R_foot",      (-0.53, 0.55, 0.13),  (-0.56, 0.40, 0.04),   "R_leg_lower", True),
]

# bones left out of the heat solve and weighted by rule (jaw: closed mouth, small chin drop)
MANUAL_BONES = {"jaw"}

SIDES = ("L", "R")
ARM = ("clavicle", "arm_upper", "arm_lower", "hand")
LEG = ("leg_upper", "leg_lower", "foot")
