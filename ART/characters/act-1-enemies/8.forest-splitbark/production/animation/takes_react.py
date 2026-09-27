"""Reaction takes: Hit (12 f flinch), Death (12 f crack: shells jolt apart, body sags; the view hides the
parent on the last frame and pops two children), Pop (10 f child: crouch, hop, land on frame 8 =
end of the sim's 8-tick sideways SplitPop)."""
from curves import track
from takes_loops import NEUTRAL_SHELL, NEUTRAL_Z

HIT_N = 12
_h_body_loc = track([(0, (0, 0, NEUTRAL_Z)), (2, (0, 0.02, -0.042), "out"), (5, (0, 0.015, -0.045)),
                     (8, (0, 0.006, -0.03)), (12, (0, 0, NEUTRAL_Z))])
_h_body_rot = track([(0, (0, 0, 0)), (2, (-1.2, 1.2, 2.0), "out"), (5, (1.5, -1.5, -0.5)), (8, (-0.4, 0.5, 0)),
                     (12, (0, 0, 0))])
_h_spine = track([(0, (0, 0, 0)), (2, (-4, 0, 1.5), "out"), (5, (2, 0, -0.5)), (12, (0, 0, 0))])
_h_neck = track([(0, (0, 0, 0)), (2, (0, 0.06, 0.0), "out"), (5, (0, 0.03, 0)), (12, (0, 0, 0))])
_h_head = track([(0, (0, 0, 0)), (2, (-10, 0, 6), "out"), (4, (6, 0, 3)), (7, (-2, 0, 0.5)), (12, (0, 0, 0))])
_h_shell_L = track([(0, NEUTRAL_SHELL), (1, -3.0, "snap"), (3, 10.0, "out"), (5, 1.0), (7, 5.0), (9, 2.5),
                    (12, NEUTRAL_SHELL)])
_h_shell_R = track([(0, NEUTRAL_SHELL), (1, -3.0, "snap"), (3, 7.0, "out"), (5, 1.5), (7, 4.0), (9, 2.0),
                    (12, NEUTRAL_SHELL)])


def hit(t):
    return {"body": {"loc": _h_body_loc(t), "rot": _h_body_rot(t)}, "spine": {"rot": _h_spine(t)},
            "neck": {"loc": _h_neck(t)}, "head": {"rot": _h_head(t)},
            "shell_L": {"open": _h_shell_L(t)}, "shell_R": {"open": _h_shell_R(t)}, "legs": {}}


DEATH_N = 12
_d_body_loc = track([(0, (0, 0, NEUTRAL_Z)), (2, (0, 0.015, -0.02), "out"), (4, (0, 0.015, -0.06), "in"),
                     (8, (0, 0.015, -0.085)), (12, (0, 0.015, -0.11), "in")])
_d_body_rot = track([(0, (0, 0, 0)), (2, (-1.5, 0, 0), "out"), (6, (2, 2, 0)), (12, (4, -3, 0), "in")])
_d_spine = track([(0, (0, 0, 0)), (2, (-2, 0, 0)), (12, (5, 0, 0))])
_d_neck_loc = track([(0, (0, 0, 0)), (2, (0, 0.03, 0.01), "out"), (12, (0, -0.05, -0.03), "in")])
_d_neck_rot = track([(0, (0, 0, 0)), (2, (-4, 0, 0)), (12, (10, 0, 0), "in")])
_d_head = track([(0, (0, 0, 0)), (2, (-12, 0, 0), "out"), (6, (-4, 0, 0)), (12, (16, 0, 0), "in")])
_d_shell_L = track([(0, NEUTRAL_SHELL), (1, -3.0, "snap"), (3, 16.0, "out"), (4, 13.0), (5, 17.0), (6, 14.0),
                    (8, 17.0), (10, 22.0, "in"), (12, 26.0, "in")])
_d_shell_R = track([(0, NEUTRAL_SHELL), (2, -3.0, "snap"), (4, 14.0, "out"), (5, 11.5), (6, 15.0), (7, 12.5),
                    (9, 16.0), (11, 21.0, "in"), (12, 24.5, "in")])


def death(t):
    return {"body": {"loc": _d_body_loc(t), "rot": _d_body_rot(t)}, "spine": {"rot": _d_spine(t)},
            "neck": {"loc": _d_neck_loc(t), "rot": _d_neck_rot(t)}, "head": {"rot": _d_head(t)},
            "shell_L": {"open": _d_shell_L(t)}, "shell_R": {"open": _d_shell_R(t)}, "legs": {}}


POP_N = 10
LAND = 8
_p_body_z = track([(0, -0.13), (1, -0.07, "out"), (2, 0.0, "lin"), (3, 0.13, "out"), (5, 0.26, "out"),
                   (7, 0.14, "in"), (8, -0.03, "in"), (9, -0.09, "out"), (10, -0.05)])
_p_pitch = track([(0, 4.0), (2, -5.0), (5, 0.0), (8, 4.0), (9, 3.0), (10, 1.0)])
_p_tuck = track([(0, 0.0), (1, 0.0, "hold"), (2, 0.03), (3, 0.05), (6, 0.05), (7, 0.03), (8, 0.0, "in"), (10, 0.0)])
_p_reach = track([(0, 0.0), (2, 0.0), (4, 1.0, "out"), (6, 1.0), (8, 0.0, "in"), (10, 0.0)])
_p_shell = track([(0, -4.0), (1, 0.0), (3, 6.0), (5, 8.0), (7, 4.0), (8, -3.0, "snap"), (9, -1.0), (10, NEUTRAL_SHELL)])
_p_neck = track([(0, (0, 0.06, -0.01)), (2, (0, 0.02, 0.0)), (5, (0, -0.04, 0.02)), (8, (0, -0.01, 0.0)),
                 (9, (0, 0.01, -0.01)), (10, (0, 0, 0))])
_p_head = track([(0, (6, 0, 0)), (2, (-6, 0, 0)), (5, (-8, 0, 0)), (8, (4, 0, 0)), (9, (6, 0, 0)), (10, (2, 0, 0))])
# in the air the legs splay like the burst-leap frame: fronts reach forward-out, hinds trail back-out
_SPLAY = {"front": (0.05, -0.10, 0.0, -10.0), "hind": (0.04, 0.08, 0.0, 15.0)}


def pop(t):
    z = _p_body_z(t)
    lift = max(0.0, z - NEUTRAL_Z)
    r = _p_reach(t)
    legs = {}
    for leg in ("front_L", "front_R", "hind_L", "hind_R"):
        kind, side = leg.split("_")
        sx, sy, _, pitch = _SPLAY[kind]
        s = 1 if side == "L" else -1
        dz = (lift + _p_tuck(t)) if (t > 1 and t < LAND) else 0.0
        legs[leg] = {"foot": (s * sx * r, sy * r, dz), "pitch": pitch * r}
    return {"body": {"loc": (0.0, 0.0, z), "rot": (_p_pitch(t), 0.0, 0.0)}, "spine": {"rot": (0.0, 0.0, 0.0)},
            "neck": {"loc": _p_neck(t)}, "head": {"rot": _p_head(t)},
            "shell_L": {"open": _p_shell(t)}, "shell_R": {"open": _p_shell(t)}, "legs": legs}
