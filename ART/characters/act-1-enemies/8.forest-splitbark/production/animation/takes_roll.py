"""27.09 roll attack takes (r02). The shells are fused to the body (rig: ~15 deg of opening at most), so the
ball is NOT made by opening the shells: the shells clamp shut (tops together, lower rims flare = squash),
the body sinks until the rims touch the ground, the legs fold up under the rims and the head pulls back
between the shell fronts. No bone scale anywhere (the package convention is loc/rot only).

Sim contract (Splitter roll): t0 curl starts (body squashes, the view turns it to the hero), t12 direction
locks, t24-30 end shake, t30 launch (0.40 m/tick for up to 16 ticks; the VIEW spins the whole body about its
lateral axis and moves it), then uncurl 30 ticks (punish) or, after a wall hit, dizzy 45.

  ForestSplitter_RollCurl    30 f  Idle f0 -> ball; tucked by f12 (lock), tension, f24-30 shake, ends = BALL
  ForestSplitter_RollLoop    12 f  seamless ball hold with a tiny squash wobble (f0 = f12 = BALL)
  ForestSplitter_RollUncurl  30 f  BALL -> Idle f0 (slow, vulnerable unfold)
  ForestSplitter_RollDizzy   45 f  BALL -> wall bump, dizzy sway, head shake -> Idle f0
"""
import math

from curves import track
import takes_loops as tl

LEGS = ("front_L", "front_R", "hind_L", "hind_R")
FKB = ("body", "spine", "neck", "head")
IDLE0 = tl.idle(0.0)

# ---------------- the ball ----------------
BALL_Z = -0.175          # body sink: shell lower rims come down to the ground
BALL_SHELL = -6.0        # shells clamped (tops together, rims flare) - the rig's validated clap
BALL = {
    "body": {"loc": (0.0, 0.0, BALL_Z), "rot": (0.0, 0.0, 0.0)},
    "spine": {"rot": (3.0, 0.0, 0.0)},
    "neck": {"loc": (0.0, 0.11, -0.05), "rot": (14.0, 0.0, 0.0)},
    "head": {"rot": (30.0, 0.0, 0.0)},
    "shell_L": {"open": BALL_SHELL},
    "shell_R": {"open": BALL_SHELL},
    # feet pulled in under the belly and lifted: the knees fold up inside the shell rims
    "legs": {"front_L": {"foot": (-0.12, 0.12, 0.09), "pitch": 20.0},
             "front_R": {"foot": (0.12, 0.12, 0.09), "pitch": 20.0},
             "hind_L": {"foot": (-0.12, -0.14, 0.08), "pitch": 15.0},
             "hind_R": {"foot": (0.12, -0.14, 0.08), "pitch": 15.0}},
}


# ---------------- pose algebra (full dicts, exact at w = 0 / 1) ----------------
def full(p):
    """Every field present, so two poses can be mixed."""
    out = {}
    for n in FKB:
        s = p.get(n, {})
        out[n] = {"loc": tuple(s.get("loc", (0.0, 0.0, 0.0))), "rot": tuple(s.get("rot", (0.0, 0.0, 0.0)))}
    for side in ("L", "R"):
        s = p.get("shell_" + side, {})
        out["shell_" + side] = {"open": s.get("open", 0.0), "tilt": s.get("tilt", 0.0)}
    out["legs"] = {}
    for leg in LEGS:
        s = p.get("legs", {}).get(leg, {})
        out["legs"][leg] = {"ik": s.get("ik", 1.0), "foot": tuple(s.get("foot", (0.0, 0.0, 0.0))),
                            "pitch": s.get("pitch", 0.0)}
    return out


def _m(a, b, w):
    if isinstance(a, tuple):
        return tuple(x * (1.0 - w) + y * w for x, y in zip(a, b))
    return a * (1.0 - w) + b * w


def mix_parts(a, b, w):
    """Per-part blend a -> b. w = {"body", "spine", "neck", "head", "shells", "legs"} (0 = a, 1 = b exactly)."""
    a, b = full(a), full(b)
    out = {}
    for k in FKB:
        out[k] = {f: _m(a[k][f], b[k][f], w[k]) for f in a[k]}
    for k in ("shell_L", "shell_R"):
        out[k] = {f: _m(a[k][f], b[k][f], w["shells"]) for f in a[k]}
    out["legs"] = {leg: {f: _m(a["legs"][leg][f], b["legs"][leg][f], w["legs"]) for f in a["legs"][leg]}
                   for leg in LEGS}
    return out


def add(p, part, field, delta):
    """In-place offset of one field of a full pose (tuple or scalar)."""
    v = p[part][field]
    p[part][field] = tuple(x + d for x, d in zip(v, delta)) if isinstance(v, tuple) else v + delta
    return p


def add_leg(p, leg, field, delta):
    v = p["legs"][leg][field]
    p["legs"][leg][field] = tuple(x + d for x, d in zip(v, delta)) if isinstance(v, tuple) else v + delta
    return p


def ball(t=0.0):
    return full(BALL)


def _parts(body, spine, neck, head, shells, legs):
    return {"body": body, "spine": spine, "neck": neck, "head": head, "shells": shells, "legs": legs}


# ---------------- RollCurl: 30 f ----------------
CURL_N = 30
LOCK = 12                # sim: direction locks - the ball is fully formed
SHAKE0 = 24              # sim: end shake 24-30, launch on 30
# progress Idle f0 -> BALL per part (negative = small anticipation away from the ball)
_c_body = track([(0, 0.0), (2, 0.12, "in"), (8, 1.0, "out"), (10, 1.0), (11, 0.94, "out"), (14, 1.0), (30, 1.0)])
_c_legs = track([(0, 0.0), (3, 0.08), (11, 1.0), (30, 1.0)])
_c_head = track([(0, 0.0), (2, -0.25, "out"), (7, 1.0, "in"), (30, 1.0)])
_c_shell = track([(0, 0.0), (2, -0.35, "out"), (8, 1.0, "in"), (10, 1.13, "snap"), (13, 0.97), (16, 1.0), (30, 1.0)])
_c_trem = track([(0, 0.0), (13, 0.0), (20, 0.45), (24, 0.8), (26, 1.8), (28, 1.4), (30, 0.0, "in")])
_c_yaw = track([(0, 0.0), (SHAKE0, 0.0), (26, 5.0, "out"), (28, 4.0), (30, 0.0, "in")])
_c_lift = track([(0, 0.0), (SHAKE0, 0.0), (26, 0.007), (28, 0.005), (30, 0.0, "in")])


def roll_curl(t):
    if t >= CURL_N:
        return ball()
    wb, wl, wh, ws = _c_body(t), _c_legs(t), _c_head(t), _c_shell(t)
    p = mix_parts(IDLE0, BALL, _parts(wb, wb, wh, wh, ws, wl))
    trem = _c_trem(t) * math.sin(t * 2.6)                  # loaded shells tremble, grows into the shake
    add(p, "shell_L", "open", trem)
    add(p, "shell_R", "open", -trem)
    if t > SHAKE0:
        add(p, "body", "rot", (0.0, 0.0, _c_yaw(t) * math.sin(2 * math.pi * (t - SHAKE0) / 3.0)))
        add(p, "body", "loc", (0.0, 0.0, _c_lift(t) * (0.5 + 0.5 * math.sin(2 * math.pi * (t - SHAKE0) / 3.0))))
    return p


# ---------------- RollLoop: 12 f ----------------
LOOP_N = 12


def roll_loop(t):
    u = (t % LOOP_N) / LOOP_N
    if abs(u) < 1e-9:
        return ball()
    squash = 0.5 - 0.5 * math.cos(2 * math.pi * 2 * u)    # 2 squash beats per loop, 0 at the seam
    rock = math.sin(2 * math.pi * u)                       # one slow left/right shell rock per loop
    p = ball()
    add(p, "body", "loc", (0.0, 0.0, -0.004 * squash))
    add(p, "shell_L", "open", -1.2 * squash + 0.8 * rock)
    add(p, "shell_R", "open", -1.2 * squash - 0.8 * rock)
    add(p, "head", "rot", (2.0 * squash, 0.0, 0.0))
    return p


def plant(p, a, b, wz):
    """Foot HEIGHT on its own weight (a -> b), so the paws swing out in the air and come down last.
    27.09 independent check: with one weight for XY and height the body rose off its rims while all four paws
    still hung 1-2 cm up (Uncurl 10-14, Dizzy 6-10) and the paws then skated 7-10 cm into place near the ground."""
    a, b = full(a), full(b)
    for leg in LEGS:
        x, y, _ = p["legs"][leg]["foot"]
        za, zb = a["legs"][leg]["foot"][2], b["legs"][leg]["foot"][2]
        p["legs"][leg]["foot"] = (x, y, za + (zb - za) * wz)
    return p


# ---------------- RollUncurl: 30 f ----------------
UNCURL_N = 30
# plant, then push: paws swing out 7-13 and land on 13, the body leaves its rims only from 12 (was 8-20 / 7-17)
_u_body = track([(0, 0.0), (12, 0.0), (21, 1.0), (30, 1.0)])
_u_legs = track([(0, 0.0), (7, 0.0), (13, 1.0), (30, 1.0)])
_u_feet_z = track([(0, 0.0), (7, 0.0), (10, 0.2), (13, 1.0, "in"), (30, 1.0)])
_u_head = track([(0, 0.0), (6, 0.0), (9, 0.35, "out"), (11, 0.3), (15, 1.0), (30, 1.0)])   # peek, then out
_u_shell = track([(0, 0.0), (9, 0.0), (19, 1.0), (22, 1.45, "out"), (26, 0.95), (30, 1.0)])  # exhale overshoot
_u_rattle = track([(0, 0.0), (1, 1.0, "out"), (5, 0.0)])     # stop jolt: the rolling ball comes to rest
_u_shake = track([(0, 0.0), (16, 0.0), (18, 9.0, "out"), (22, 6.0), (25, 0.0, "in"), (30, 0.0)])


def roll_uncurl(t):
    if t >= UNCURL_N:
        return full(IDLE0)
    wb, wl, wh, ws = _u_body(t), _u_legs(t), _u_head(t), _u_shell(t)
    p = mix_parts(BALL, IDLE0, _parts(wb, wb, wh, wh, ws, wl))
    plant(p, BALL, IDLE0, _u_feet_z(t))
    r = _u_rattle(t) * math.sin(2 * math.pi * t / 2.5)
    add(p, "shell_L", "open", 1.5 * r)
    add(p, "shell_R", "open", -1.5 * r)
    add(p, "body", "rot", (0.0, 0.0, 3.0 * r))
    add(p, "head", "rot", (0.0, 0.0, _u_shake(t) * math.sin(2 * math.pi * (t - 16) / 4.5)))   # shakes it off
    return p


# ---------------- RollDizzy: 45 f ----------------
DIZZY_N = 45
DIZZY_Z = -0.095                                  # wobbly crouch (keeps the hind legs off full stretch)
_SPLAY = {"front_L": (0.045, -0.02), "front_R": (-0.035, -0.03), "hind_L": (0.03, 0.02), "hind_R": (-0.035, 0.015)}
DIZZY = full(IDLE0)
DIZZY["body"]["loc"] = (0.0, 0.0, DIZZY_Z)
for _leg, (_dx, _dy) in _SPLAY.items():
    DIZZY["legs"][_leg]["foot"] = (_dx, _dy, 0.0)
# wall bump 0-5: the ball bounces back off the wall in front (+Y = back), shells clap
_z_bump_y = track([(0, 0.0), (2, 0.035, "out"), (5, 0.02), (9, 0.0)])
_z_bump_z = track([(0, 0.0), (2, 0.018, "out"), (4, 0.0, "in"), (7, 0.0)])
_z_bump_pitch = track([(0, 0.0), (2, -3.0, "out"), (4, 0.0, "in"), (8, 0.0)])
_z_clap = track([(0, 0.0), (1, -2.0, "snap"), (3, 1.5), (6, 0.0)])
# unfold (fast, graceless): paws swing out 5-10 and plant on 10, then the body pushes up 10-15 (was 5-13 / 5-12);
# stand on splayed feet, back to Idle f0 over 36-45
_z_body = track([(0, 0.0), (10, 0.0), (15, 1.0), (45, 1.0)])
_z_legs = track([(0, 0.0), (5, 0.0), (10, 1.0), (45, 1.0)])
_z_feet_z = track([(0, 0.0), (5, 0.0), (7, 0.2), (10, 1.0, "in"), (45, 1.0)])
_z_head = track([(0, 0.0), (4, 0.0), (9, 1.0, "out"), (45, 1.0)])
_z_shell = track([(0, 0.0), (6, 0.0), (13, 1.0), (45, 1.0)])
_z_env = track([(0, 0.0), (9, 0.0), (15, 1.0), (32, 1.0), (40, 0.0), (45, 0.0)])   # dizzy sway amount
_z_home = track([(0, 0.0), (36, 0.0), (44, 1.0), (45, 1.0)])                        # DIZZY -> Idle f0
_z_shakeoff = track([(0, 0.0), (35, 0.0), (37, 12.0, "out"), (41, 8.0), (43, 0.0, "in"), (45, 0.0)])
# feet step home one by one (lift arcs) instead of sliding: (start, end)
_STEP_HOME = {"front_L": (36, 40), "hind_R": (37, 41), "front_R": (39, 43), "hind_L": (40, 44)}


def roll_dizzy(t):
    if t >= DIZZY_N:
        return full(IDLE0)
    wb, wl, wh, ws = _z_body(t), _z_legs(t), _z_head(t), _z_shell(t)
    p = mix_parts(BALL, DIZZY, _parts(wb, wb, wh, wh, ws, wl))
    plant(p, BALL, DIZZY, _z_feet_z(t))
    # home: the DIZZY stance blends into Idle f0 (body/head/shells); the feet step instead of sliding
    h = _z_home(t)
    if h > 0:
        q = mix_parts(DIZZY, IDLE0, _parts(h, h, h, h, h, 0.0))
        for k in FKB + ("shell_L", "shell_R"):
            p[k] = q[k]
    for leg, (s0, s1) in _STEP_HOME.items():
        if t > s0:
            s = min(1.0, (t - s0) / (s1 - s0))
            e = s * s * (3 - 2 * s)
            dx, dy = _SPLAY[leg]
            p["legs"][leg]["foot"] = (dx * (1 - e), dy * (1 - e), 0.05 * math.sin(math.pi * s))
    # wall bump
    add(p, "body", "loc", (0.0, _z_bump_y(t), _z_bump_z(t)))
    add(p, "body", "rot", (_z_bump_pitch(t), 0.0, 0.0))
    add(p, "shell_L", "open", _z_clap(t))
    add(p, "shell_R", "open", _z_clap(t))
    # dizzy: the body sways in a slow lopsided circle, the head circles against it, shells flap unevenly
    env = _z_env(t)
    if env > 0:
        a = 2 * math.pi * (t - 9) / 14.0
        b = 2 * math.pi * (t - 9) / 10.0
        add(p, "body", "rot", (2.0 * env * math.sin(a + 1.2), 6.5 * env * math.sin(a), 6.0 * env * math.sin(0.5 * a)))
        add(p, "spine", "rot", (0.0, -2.0 * env * math.sin(a - 0.6), -4.0 * env * math.sin(0.5 * a + 0.5)))
        add(p, "neck", "loc", (0.025 * env * math.sin(b), 0.0, 0.012 * env * math.cos(b)))
        add(p, "head", "rot", (9.0 * env * math.cos(b), 7.0 * env * math.sin(b), -20.0 * env * math.sin(b)))
        add(p, "shell_L", "open", 4.5 * env * math.sin(a + 0.4))
        add(p, "shell_R", "open", 3.5 * env * math.sin(a + 2.0))
    add(p, "head", "rot", (0.0, 0.0, _z_shakeoff(t) * math.sin(2 * math.pi * (t - 35) / 4.0)))
    return p
