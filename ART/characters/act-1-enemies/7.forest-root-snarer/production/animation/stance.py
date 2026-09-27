"""Base stance shared by every take (idle frame 0 = hit 0/12 = slam 0/72 = death 0).

Rest pose is not planted (rig report): -X slab 4-6 cm up, L hind sole 10 cm up, R 3 cm up.
Base: pelvis 4 cm lower, L hind foot 10 cm further forward (the L leg is almost straight at
rest and cannot reach the ground where it hangs), all four contacts snapped to z = 0.
"""
import copy

BASE = {
    "pelvis_off": (0.0, 0.0, -0.04),
    "pelvis_rot": (0.0, 0.0, 0.0),
    "rot": {},
    "hand": {"L": (0.60, -0.60, 0.10), "R": (-0.60, -0.66, 0.10)},
    "hand_ground": {"L": 0.0, "R": 0.0},
    "hand_twist": {"L": 0.0, "R": 0.0},
    "foot": {"L": (0.55, 0.64, 0.0), "R": (-0.53, 0.53, 0.0)},
    "foot_rot": {"L": (0.0, 0.0, 0.0), "R": (0.0, 0.0, 0.0)},
}


def base():
    return copy.deepcopy(BASE)


def add3(a, b, k=1.0):
    return tuple(x + y * k for x, y in zip(a, b))


def rot_add(P, bone, e, k=1.0):
    P["rot"][bone] = add3(P["rot"].get(bone, (0.0, 0.0, 0.0)), e, k)


def blend(A, B, t):
    """Linear blend of two pose dicts (t=0 -> A, t=1 -> B)."""
    def mix(a, b):
        return tuple(x + (y - x) * t for x, y in zip(a, b))
    out = {"pelvis_off": mix(A["pelvis_off"], B["pelvis_off"]), "pelvis_rot": mix(A["pelvis_rot"], B["pelvis_rot"]), "rot": {}}
    for k in set(A["rot"]) | set(B["rot"]):
        out["rot"][k] = mix(A["rot"].get(k, (0, 0, 0)), B["rot"].get(k, (0, 0, 0)))
    for key in ("hand", "foot", "foot_rot"):
        out[key] = {s: mix(A[key][s], B[key][s]) for s in ("L", "R")}
    out["hand_twist"] = {s: A["hand_twist"][s] + (B["hand_twist"][s] - A["hand_twist"][s]) * t for s in ("L", "R")}
    out["hand_ground"] = {}
    for s in ("L", "R"):
        ga, gb = A["hand_ground"].get(s), B["hand_ground"].get(s)
        # grounded + free never blend: freeze() the grounded pose first (explicit sockets)
        out["hand_ground"][s] = ga + (gb - ga) * t if (ga is not None and gb is not None) else None
    return out
