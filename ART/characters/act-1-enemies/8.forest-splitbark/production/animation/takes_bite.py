"""ForestSplitter_Bite: 30 f. Crouch back + shells open 0-12, lunge, beak snap + shell clap at CONTACT 18
(sim windup 18 ticks), recover by 30 (sim recovery 12). Front-left paw stomps on the contact frame."""
import math

from curves import track
from takes_loops import NEUTRAL_SHELL, NEUTRAL_Z

BITE_N = 30
CONTACT = 18

_body_loc = track([(0, (0, 0, NEUTRAL_Z)), (5, (0, 0.035, -0.055)), (12, (0, 0.05, -0.07), "out"),
                   (15, (0, -0.015, -0.06), "in"), (18, (0, -0.05, -0.055), "out"), (20, (0, -0.05, -0.055), "lin"),
                   (26, (0, 0.008, -0.03)), (30, (0, 0, NEUTRAL_Z))])
_body_rot = track([(0, (0, 0, 0)), (12, (-3.5, 0, 3.0), "out"), (15, (1.5, 0, 0.5), "in"),
                   (18, (3.0, 0, -2.0), "out"), (20, (3.0, 0, -2.0), "lin"), (26, (0.5, 0, 0)), (30, (0, 0, 0))])
_spine = track([(0, (0, 0, 0)), (12, (-4.0, 0, 1.5), "out"), (15, (0.5, 0, 0), "in"), (18, (2.0, 0, -1.5), "out"),
                (20, (2.0, 0, -1.5), "lin"), (28, (0, 0, 0)), (30, (0, 0, 0))])
# contact: the head shoots forward AND a little up, out past the shell rims, so the jab reads from the
# 52-degree game camera (a nose-down peck hid the beak under the clapping shells)
# _LUNGE_REF is the r01 neck track (0.25 m jab). It is kept only as the timing envelope of the jab.
_LUNGE_REF = track([(0, (0, 0, 0)), (12, (0, 0.06, -0.01), "out"), (15, (0, -0.03, 0.03), "in"),
                    (17, (0, -0.15, 0.05), "in"), (18, (0, -0.25, 0.05), "out"), (20, (0, -0.235, 0.04), "lin"),
                    (26, (0, -0.03, 0.0)), (30, (0, 0, 0))])
# Step-3 fix: the 0.25 m neck translation pulled a 24 cm sleeve of stretched faces between the head and the
# rigid shell rims (the rig was validated to a 0.14 m head lunge). The neck now travels 0.45 x (0.11 m), and
# the reach comes back from a 3.5 cm body lurch and a 10 deg nose-up snap on the same envelope
# (probe_bite.py: head/shell stretch 245 -> 130 mm, beak 0.916 -> 0.851 m forward, no IK pull).
NECK_SCALE, LUNGE_BODY_M, LUNGE_HEAD_PITCH = 0.45, 0.035, -10.0


def _lunge(t):
    """0 at rest / windup, 1 at the contact frame, following the jab timing."""
    return max(0.0, -_LUNGE_REF(t)[1]) / 0.25


def _neck_loc(t):
    x, y, z = _LUNGE_REF(t)
    return (x, y * NECK_SCALE, z)
_neck_rot = track([(0, (0, 0, 0)), (12, (-6, 0, 0), "out"), (16, (-6, 0, 0)), (17, (-5, 0, 0)), (18, (-3, 0, 0), "snap"),
                   (20, (-1, 0, 0), "lin"), (30, (0, 0, 0))])
_head_rot = track([(0, (0, 0, 0)), (12, (-8, 0, 0), "out"), (16, (-10, 0, 0)), (17, (-8, 0, 0)), (18, (-3, 0, 0), "snap"),
                   (20, (3, 0, 0), "out"), (24, (2, 0, 0)), (30, (0, 0, 0))])
_shell = track([(0, NEUTRAL_SHELL), (4, 5.0), (12, 11.0, "out"), (16, 13.0), (18, -4.0, "snap"),
                (20, -2.0, "out"), (23, 4.0), (27, 1.5), (30, NEUTRAL_SHELL)])
# front-left paw: lifts with the lunge, stomps down at contact, steps back during recovery
_paw = track([(0, (0, 0, 0)), (12, (0, 0, 0), "hold"), (15, (0, -0.04, 0.07), "out"), (18, (0, -0.06, 0.0), "in"),
              (22, (0, -0.06, 0.0), "lin"), (25, (0, -0.03, 0.045)), (28, (0, 0, 0), "in"), (30, (0, 0, 0))])
_paw_pitch = track([(0, 0), (12, 0, "hold"), (15, -12), (18, 0, "in"), (22, 0, "lin"), (25, 10), (28, 0), (30, 0)])


def bite(t):
    trem = 0.8 * math.sin(t * 2.6) if 9 <= t <= 16 else 0.0      # loaded shells tremble before the snap
    shell = _shell(t)
    lunge = _lunge(t)
    bx, by, bz = _body_loc(t)
    hp, hr, hy = _head_rot(t)
    return {
        "body": {"loc": (bx, by - LUNGE_BODY_M * lunge, bz), "rot": _body_rot(t)},
        "spine": {"rot": _spine(t)},
        "neck": {"loc": _neck_loc(t), "rot": _neck_rot(t)},
        "head": {"rot": (hp + LUNGE_HEAD_PITCH * lunge, hr, hy)},
        "shell_L": {"open": shell + trem},
        "shell_R": {"open": shell - trem},
        "legs": {"front_L": {"foot": _paw(t), "pitch": _paw_pitch(t)}},
    }
