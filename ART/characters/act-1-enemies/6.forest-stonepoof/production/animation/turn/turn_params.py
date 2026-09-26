"""Pure-python parameters for the 90 deg turn-in-place cycle (no bpy).

Unsigned convention: s=+1 TurnLeft (+Z carrier yaw), s=-1 TurnRight.
Hoof angle phi_u (deg, about the object origin, carrier space) DEcreases
6 deg/frame while planted and is swung forward during a step; the signed
carrier-space yaw of the hoof is s*phi_u. All functions are periodic in f
(period 7.5 for the steps, 15 for the clip), so f=0 and f=15 are identical.
"""
import math
FRAMES = 15
FPS = 30
DEG_PER_FRAME = 90.0 / FRAMES      # sim: 6 deg per tick
HALF = FRAMES / 2.0                # two steps per hoof per 90 deg cycle
C_CLAMP = 5.0                      # max stance angle offset at f=0 (stance join)
SAMPLE_STEP = 0.25                 # dense keys every 1/4 frame
KEY_RANGE = (-2.0, 17.0)           # keys past the seam so handles stay periodic
REACH_MARGIN = 0.006               # metres of IK slack kept on every leg

DIRECTIONS = {'TurnLeft': 1, 'TurnRight': -1}

# Role timings (frames inside each 7.5-frame half cycle).
# o = lift, d = swing duration, H = sole clearance peak, P = toe-down pitch peak,
# bow = (lateral toward turn side, forward) swing bulge in metres.
ROLES = {
    'inside_front':  dict(o=1.0, d=2.25, H=0.100, P=-18.0, bow=(0.020, 0.015)),
    'outside_hind':  dict(o=1.5, d=2.00, H=0.055, P=-8.0,  bow=(0.000, 0.000)),
    'outside_front': dict(o=4.0, d=2.25, H=0.110, P=-20.0, bow=(0.050, 0.030)),
    'inside_hind':   dict(o=4.5, d=2.00, H=0.050, P=-8.0,  bow=(0.000, 0.000)),
}


def roles_for(s):
    """Map rig legs to roles. Left turn: the +X (left) legs are inside."""
    if s > 0:
        return {'front_left': 'inside_front', 'front_right': 'outside_front',
                'hind_left': 'inside_hind', 'hind_right': 'outside_hind'}
    return {'front_right': 'inside_front', 'front_left': 'outside_front',
            'hind_right': 'inside_hind', 'hind_left': 'outside_hind'}


def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * t * (t * (6 * t - 15) + 10)


def stance_offset(role):
    r = ROLES[role]
    centred = 6 * r['o'] + 3 * r['d'] - 22.5   # (lift+land)/2 == 0
    return max(-C_CLAMP, min(C_CLAMP, centred))


def hoof_state(role, f):
    """Return (phi_u deg, clearance m, pitch deg, bulge 0..1, planted bool)."""
    r = ROLES[role]
    o, d = r['o'], r['d']
    c = stance_offset(role)
    lift = c - DEG_PER_FRAME * o
    land = c + DEG_PER_FRAME * (HALF - o - d)
    p = f % HALF
    if o < p < o + d:
        t = (p - o) / d
        # Interpolate in WORLD angle so touchdown/lift-off have zero world
        # velocity (carrier-space velocity then matches the planted -6 deg/f).
        start = o + (f - p)
        w0 = lift + DEG_PER_FRAME * start
        w1 = land + DEG_PER_FRAME * (start + d)
        phi = w0 + (w1 - w0) * smooth(t) - DEG_PER_FRAME * f
        clear = r['H'] * math.sin(math.pi * t) ** 1.5
        pitch = r['P'] * math.sin(math.pi * min(1.0, t * 1.15))
        return phi, clear, pitch, math.sin(math.pi * t) ** 2, False
    q = p if p <= o else p - HALF        # time since mid-stance reference (f=0)
    return c - DEG_PER_FRAME * q, 0.0, 0.0, 0.0, True


def spine(s, f):
    """Armature-space Euler (pitch X, roll Y, yaw Z) degrees and body offset."""
    w = 2 * math.pi / HALF
    osc = math.sin(w * f)
    ang = {
        'body':      (0.4 * math.sin(2 * w * f), s * (-1.5 + 0.8 * math.cos(w * f)), s * 1.5),
        'body_top0': (0.8 * math.sin(2 * w * f + 0.5), s * -1.0, s * (2.0 + 0.5 * osc)),
        'body_top1': (0.3, 0.0, s * 1.5),
        'neck0':     (-1.0 + 0.8 * math.sin(2 * w * f + 1.0), 0.0, s * (4.0 + 1.0 * osc)),
        'head0':     (1.0 * math.sin(w * f + 1.2), s * 1.5, s * (6.0 + 1.5 * math.sin(w * f + 0.6))),
        'body_bot':  (0.0, 0.0, s * -1.5),
        'pelvis':    (0.0, 0.0, s * -1.0),
        'tail0':     (0.0, s * 3.0 * osc, s * (-4.0 + 2.0 * math.sin(w * f + 1.5))),
    }
    offset = (s * (-0.015 + 0.006 * math.cos(w * f)), 0.0,
              -0.010 - 0.004 * math.cos(2 * w * f))
    return ang, offset


def samples():
    a, b = KEY_RANGE
    n = int(round((b - a) / SAMPLE_STEP))
    return [a + i * SAMPLE_STEP for i in range(n + 1)]


if __name__ == '__main__':
    for s in (1, -1):
        for leg, role in roles_for(s).items():
            vals = [hoof_state(role, f / 8) for f in range(121)]
            print(s, leg, role, 'c', stance_offset(role),
                  'phi range', round(min(v[0] for v in vals), 1), round(max(v[0] for v in vals), 1),
                  'planted f0/7.5/15', vals[0][4], vals[60][4], vals[120][4])
