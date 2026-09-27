"""Looping takes: Idle (60 f) and Walk (in-place trot, 10 f = one stride at 3.1 m/s)."""
import math

from curves import track, wave

NEUTRAL_Z = -0.025      # shared settle of the body: keeps the near-straight hind legs off full stretch
NEUTRAL_SHELL = 2.0

# ---------------- Idle ----------------
IDLE_N = 60


def idle(t):
    w = lambda k=1, ph=0.0: wave(t, IDLE_N / k, ph)  # noqa: E731
    return {
        "body": {"loc": (0.006 * w(1, 0.1), 0.0, NEUTRAL_Z + 0.008 * w(1)),
                 "rot": (-0.5 * w(1, 0.15), 1.1 * w(1, 0.1), 0.0)},
        "spine": {"rot": (0.8 * w(1, 0.1), 0.0, 1.0 * w(1, 0.2))},
        "neck": {"loc": (0.0, 0.006 * w(1, 0.16), 0.0)},
        "head": {"rot": (-1.5 + 1.5 * w(2, 0.16), 0.0, 5.0 * w(1, 0.32))},
        "shell_L": {"open": 2.5 + 1.8 * w(1, -0.08) + 0.7 * w(2), "tilt": 0.8 * w(1, 0.25)},
        "shell_R": {"open": 2.5 - 1.8 * w(1, -0.08) + 0.7 * w(2, 0.05), "tilt": -0.8 * w(1, 0.25)},
        "legs": {},
    }


# ---------------- Walk (trot) ----------------
WALK_N = 10
SPEED = 3.1                          # m/s, Simulation.SplitterMoveSpeed
STEP = SPEED / 30.0                  # ground travel per frame
DUTY = 0.37                          # stance share of the cycle: two short airborne moments
STANCE = STEP * WALK_N * DUTY        # 0.382 m of foot travel while planted
WALK_Z = -0.08                       # crouched trot so the short legs can reach the stride
PHASE = {"front_L": 0.0, "hind_R": 0.0, "front_R": 0.5, "hind_L": 0.5}   # diagonal pairs
CENTER = {"front": -0.06, "hind": 0.16}   # mid-stance forward offset (foot under shoulder / hip)
LIFT = {"front": 0.09, "hind": 0.08}
_swing_pitch = track([(0.0, 0.0), (0.35, 18.0), (0.75, -6.0), (1.0, 0.0)])


def foot_cycle(u, kind):
    """u in [0,1): stance first (DUTY), then swing. Returns forward offset (-Y), lift, pitch."""
    front = CENTER[kind] + STANCE / 2
    back = CENTER[kind] - STANCE / 2
    if u < DUTY:
        return front - (front - back) * (u / DUTY), 0.0, 0.0
    s = (u - DUTY) / (1 - DUTY)
    sm = s * s * (3 - 2 * s)
    fwd = back + (front - back) * sm
    lift = LIFT[kind] * math.sin(math.pi * s)
    return fwd, lift, _swing_pitch(s)


def walk(t):
    u0 = (t / WALK_N) % 1.0
    tau = 2 * math.pi
    bob = math.cos(2 * tau * (u0 - 0.22))            # +1 at the lowest point (just after mid-stance)
    legs = {}
    for leg, ph in PHASE.items():
        kind = leg.split("_")[0]
        fwd, lift, pitch = foot_cycle((u0 + ph) % 1.0, kind)
        legs[leg] = {"foot": (0.0, -fwd, lift), "pitch": pitch}
    return {
        "body": {"loc": (0.0, 0.0, WALK_Z - 0.012 * bob),
                 "rot": (3.0 + 1.0 * math.sin(2 * tau * (u0 - 0.1)), 1.5 * math.sin(tau * (u0 - 0.1)),
                         1.5 * math.sin(tau * u0))},
        "spine": {"rot": (0.0, -0.6 * math.sin(tau * (u0 - 0.1)), -2.0 * math.sin(tau * u0))},
        "neck": {"loc": (0.0, -0.02, 0.006 * bob)},
        "head": {"rot": (-3.0 - 0.8 * math.sin(2 * tau * (u0 - 0.1)), 0.0, 1.0 * math.sin(tau * u0))},
        "shell_L": {"open": 2.5 + 2.0 * math.cos(2 * tau * (u0 - 0.27)) + 1.0 * math.sin(tau * (u0 - 0.15))},
        "shell_R": {"open": 2.5 + 2.0 * math.cos(2 * tau * (u0 - 0.27)) - 1.0 * math.sin(tau * (u0 - 0.15))},
        "legs": legs,
    }
