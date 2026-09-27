"""LineCast, Burst, Shot takes (keyed, start/end on Idle frame 0)."""
from anim_keys import Track
from anim_pose import DIRS
from takes_common import K, add, idle, norm

LINE_CONTACT = 24
BURST_RELEASE = 21
SHOT_RELEASE = 21
# world spike tip (muzzle) and throw direction at release: straight ahead (-Y), shoulder height
SHOT_MUZZLE = (-0.12, -1.42, 1.90)
SHOT_DIR = norm((0.0, -1.0, -0.06))

# spike tips stab the ground in front, 0.10 m deep after contact
LINE_TIP = {"L": (0.40, -0.84, 0.0), "R": (-0.37, -0.80, 0.0)}
LINE_DIR = {"L": norm((0.08, -0.42, -1.0)), "R": norm((-0.08, -0.42, -1.0))}
BURY = 0.10
# elbow pole for the raise: back-out-down stays off the shoulder->wrist axis for the whole sweep (down -> out -> up)
RAISE_POLE = {"L": norm((0.5, 0.6, -0.6)), "R": norm((-0.5, 0.6, -0.6))}


def _line_lock(tipdepth, extra=None):
    o = {}
    for s in "LR":
        o[s + "_lock"] = 1.0
        o[s + "_wtip"] = add(LINE_TIP[s], (0, 0, -tipdepth))
        o[s + "_wdir"] = LINE_DIR[s]
    if extra:
        lock = {k: v for k, v in o.items()}
        o = dict(extra)
        for k, v in lock.items():
            o.setdefault(k, v)
    return o


def _arc_lock(body, ltip, ldir, rtip, rdir):
    """Both spikes world-locked at the given tips/directions (strike frames between coil and contact)."""
    o = dict(body)
    o.update(L_lock=1.0, L_wtip=ltip, L_wdir=norm(ldir), R_lock=1.0, R_wtip=rtip, R_wdir=norm(rdir))
    return o


def linecast_take(D):
    I = idle(D, 0)
    crouch = dict(hip=(0.0, 0.12, -0.56), hip_rot=(30.0, 0.0, 0.0), spine=(36.0, 0.0, 0.0), neck=(-10.0, 0, 0), head=(-38.0, 0, 0),
                  L_knee=norm((0.45, -1, 0)), R_knee=norm((-0.45, -1, 0)),
                  L_pole=norm((1.0, 0.6, 0.2)), R_pole=norm((-1.0, 0.6, 0.2)), L_shrug=6.0, R_shrug=6.0)
    up = dict(hip=(0.0, 0.03, -0.015), hip_rot=(-2.0, 0, 0), spine=(-6.0, 0, 0), neck=(-4.0, 0, 0), head=(-14.0, 0, 0),
              L_tip=(0.36, -0.10, 3.46), L_dir=norm((-0.22, 0.0, 1.0)), L_pole=norm((1.0, 0.35, -0.1)), L_shrug=22.0,
              R_tip=(-0.37, -0.10, 3.42), R_dir=norm((0.22, 0.0, 1.0)), R_pole=norm((-1.0, 0.35, -0.1)), R_shrug=22.0)
    keys = [
        K(I, 0),
        # r02 raise (ref 0.5-1.33 s): elbows bend out-down, hands come up by the head ("flex"), then press overhead.
        # Wrists stay inside reach and the elbow pole never crosses the shoulder->wrist axis (no straight-arm pop,
        # no elbow flip that r01 had at frames 4-10).
        K(I, 5, hip=(0.0, 0.02, -0.08), hip_rot=(3.0, 0, 0), spine=(6.0, 0, 0), head=(3.0, 0, 0),
          L_tip=(1.152, -0.51, 1.386), L_dir=norm((0.7, -0.45, -0.3)), L_pole=RAISE_POLE["L"], L_shrug=4.0,
          R_tip=(-1.096, -0.474, 1.39), R_dir=norm((-0.7, -0.45, -0.3)), R_pole=RAISE_POLE["R"], R_shrug=4.0),
        K(I, 10, hip=(0.0, 0.02, -0.06), spine=(0.0, 0, 0), head=(-4.0, 0, 0),
          L_tip=(0.563, -0.331, 2.642), L_dir=norm((-0.1, -0.3, 0.95)), L_pole=RAISE_POLE["L"], L_shrug=10.0,
          R_tip=(-0.57, -0.31, 2.554), R_dir=norm((0.1, -0.3, 0.95)), R_pole=RAISE_POLE["R"], R_shrug=10.0),
        K(I, 14, hip=(0.0, 0.025, -0.03), hip_rot=(-1.0, 0, 0), spine=(-4.0, 0, 0), neck=(-3.0, 0, 0), head=(-10.0, 0, 0),
          L_tip=(0.496, -0.116, 3.182), L_dir=norm((-0.15, -0.1, 1.0)), L_pole=RAISE_POLE["L"], L_shrug=16.0,
          R_tip=(-0.506, -0.109, 3.092), R_dir=norm((0.15, -0.1, 1.0)), R_pole=RAISE_POLE["R"], R_shrug=16.0),
        K(I, 18, **up),
        # coil: spikes tip back a touch before the drive
        K(I, 20, "auto", **dict(up, spine=(-15.0, 0, 0), head=(-18.0, 0, 0), hip=(0.0, 0.04, 0.01),
                                L_dir=norm((-0.15, 0.2, 1.0)), R_dir=norm((0.15, 0.2, 1.0)))),
        # drive down (r03, verification): the old path chained two ease-in segments (20->22->24), so the tips stopped
        # at frame 22 (0.75 m up) and crept to the ground (tip speed 68 -> 13 -> 19 m/s): the slam hovered before contact.
        # Now every strike frame is keyed in world space along the arc, tip speed 21-24: 24 / 32 / 34 / 37 m/s,
        # fastest on the contact frame.
        K(I, 21, "in", **_arc_lock(dict(crouch, hip=(0.0, 0.05, -0.05), hip_rot=(2.0, 0, 0), spine=(-8.0, 0, 0), head=(-22.0, 0, 0)),
                                    (0.44, -0.40, 3.20), (0.0, -0.3, 0.95), (-0.44, -0.38, 3.15), (0.0, -0.3, 0.95))),
        K(I, 22, "lin", **_arc_lock(dict(crouch, hip=(0.0, 0.06, -0.20), hip_rot=(8.0, 0, 0), spine=(8.0, 0, 0), head=(-26.0, 0, 0)),
                                    (0.55, -1.02, 2.38), (0.18, -0.95, 0.25), (-0.55, -1.00, 2.35), (-0.18, -0.95, 0.25))),
        K(I, 23, "lin", **_arc_lock(dict(crouch, hip=(0.0, 0.08, -0.38), hip_rot=(18.0, 0, 0), spine=(24.0, 0, 0), head=(-32.0, 0, 0)),
                                    (0.50, -1.30, 1.15), (0.2, -0.8, -0.55), (-0.50, -1.27, 1.12), (-0.2, -0.8, -0.55))),
        K(I, LINE_CONTACT, "lin", **_line_lock(0.0, crouch)),
        # spikes sink in, body settles a little lower (impact)
        K(I, 26, "out", **_line_lock(BURY, dict(crouch, hip=(0.0, 0.13, -0.66), spine=(40.0, 0, 0), head=(-40.0, 0, 0)))),
        K(I, 30, "auto", **_line_lock(BURY, dict(crouch, hip=(0.0, 0.13, -0.64), spine=(40.0, 0, 0)))),
        # hold: straining breath, spikes stay buried (world-locked) while spikes run along the line
        K(I, 45, "auto", **_line_lock(BURY, dict(crouch, hip=(0.0, 0.13, -0.67), spine=(41.0, 0, 0)))),
        K(I, 60, "auto", **_line_lock(BURY, dict(crouch, hip=(0.0, 0.13, -0.64), spine=(40.0, 0, 0)))),
        # pull out and straighten
        K(I, 65, "auto", **dict(crouch, hip=(0.0, 0.07, -0.34), hip_rot=(12.0, 0, 0), spine=(18.0, 0, 0), head=(-18.0, 0, 0),
                                L_tip=(0.66, -0.46, 0.98), L_dir=norm((0.1, -0.5, -1.0)),
                                R_tip=(-0.64, -0.46, 0.98), R_dir=norm((-0.1, -0.5, -1.0)))),
        K(I, 75, "auto"),
    ]
    return {"frames": 75, "loop": False, "contact": LINE_CONTACT, "params": Track(keys, D, DIRS),
            "events": {"raise_end": 18, "contact": LINE_CONTACT, "buried_hold": [26, 60], "pull_out": [60, 75]}}


def burst_take(D):
    I = idle(D, 0)
    curl = dict(hip=(0.0, 0.05, -0.30), hip_rot=(12.0, 0, 0), spine=(32.0, 0, 0), neck=(12.0, 0, 0), head=(18.0, 0, 0),
                L_tip=(-0.10, -0.52, 2.30), L_dir=norm((-0.45, -0.15, 1.0)), L_pole=norm((1.0, 0.1, -1.0)), L_shrug=-4.0,
                R_tip=(0.10, -0.55, 2.25), R_dir=norm((0.45, -0.15, 1.0)), R_pole=norm((-1.0, 0.1, -1.0)), R_shrug=-4.0,
                L_knee=norm((0.4, -1, 0)), R_knee=norm((-0.4, -1, 0)))
    star = dict(hip=(0.0, 0.0, -0.01), hip_rot=(-5.0, 0, 0), spine=(-12.0, 0, 0), neck=(-6.0, 0, 0), head=(-20.0, 0, 0),
                L_tip=(1.36, -0.22, 2.78), L_dir=norm((0.85, -0.12, 0.52)), L_pole=norm((0.0, 0.3, 1.0)), L_shrug=18.0,
                R_tip=(-1.32, -0.22, 2.72), R_dir=norm((-0.85, -0.12, 0.52)), R_pole=norm((0.0, 0.3, 1.0)), R_shrug=18.0)
    keys = [
        K(I, 0),
        # r02: the spikes swing in-and-forward to the chest (wrist stays in front, no backward sweep / straight-arm pop)
        K(I, 4, **dict(curl, hip=(0.0, 0.02, -0.08), hip_rot=(5.0, 0, 0), spine=(9.0, 0, 0), neck=(4.0, 0, 0), head=(3.0, 0, 0),
                       L_tip=(0.443, -0.916, 1.478), L_dir=norm((-0.2, -0.95, 0.1)), L_pole=norm((1.0, 0.3, -0.6)), L_shrug=0.0,
                       R_tip=(-0.438, -0.817, 1.451), R_dir=norm((0.2, -0.95, 0.1)), R_pole=norm((-1.0, 0.3, -0.6)), R_shrug=0.0)),
        K(I, 8, **dict(curl, hip=(0.0, 0.04, -0.18), spine=(20.0, 0, 0), head=(8.0, 0, 0),
                       L_tip=(0.25, -0.72, 1.90), L_dir=norm((-0.3, -0.75, 0.75)),
                       R_tip=(-0.25, -0.72, 1.87), R_dir=norm((0.3, -0.75, 0.75)))),   # r03: less wrist fold on the way in
        K(I, 15, **curl),
        # tighter squeeze just before release
        K(I, 18, **dict(curl, hip=(0.0, 0.06, -0.34), spine=(36.0, 0, 0), head=(22.0, 0, 0))),
        # r03 (verification): the in-between before the release folded the spikes 100 deg back against the forearms
        # (wrist skin stretched 17 cm); the whip frame now leads with the spikes pointing out-forward
        K(I, 20, "in", **dict(star, hip=(0.0, 0.034, -0.195), hip_rot=(4.5, 0, 0), spine=(15.0, 0, 0), neck=(4.0, 0, 0), head=(3.5, 0, 0),
                              L_tip=(0.92, -0.50, 2.50), L_dir=norm((0.75, -0.5, 0.45)), L_pole=norm((0.4, 0.3, -0.6)), L_shrug=6.0,
                              R_tip=(-0.90, -0.50, 2.45), R_dir=norm((-0.75, -0.5, 0.45)), R_pole=norm((-0.4, 0.3, -0.6)), R_shrug=6.0)),
        K(I, BURST_RELEASE, "lin", **star),
        # r03 (verification): the tips used to keep spreading after the release (widest at frame 23, 97% at 21);
        # now frame 21 is the widest star and only the chest keeps arching while the spikes hold
        K(I, 23, "out", **dict(star, spine=(-15.0, 0, 0), head=(-24.0, 0, 0))),
        K(I, 27, "auto", **dict(star, spine=(-12.0, 0, 0), hip=(0.0, 0.0, 0.0))),
        K(I, 36, "auto"),
    ]
    return {"frames": 36, "loop": False, "contact": BURST_RELEASE, "params": Track(keys, D, DIRS),
            "events": {"curl": [0, 18], "release": BURST_RELEASE, "hold": [21, 27], "recover": [27, 36]}}


def shot_take(D):
    I = idle(D, 0)
    cock = dict(hip=(0.0, 0.07, -0.08), hip_rot=(-2.0, 0, -10.0), spine=(-8.0, 3.0, -30.0), neck=(0, 0, 8.0), head=(-6.0, 0, 16.0),
                R_tip=(-0.35, -0.05, 2.62), R_dir=norm((0.1, -1.0, 0.3)), R_pole=norm((-1.0, 0.5, -0.5)), R_shrug=14.0,
                L_tip=(0.95, -1.05, 1.95), L_dir=norm((0.45, -1.0, 0.05)), L_pole=norm((1.0, 0.2, -0.6)), L_shrug=4.0)
    release = dict(hip=(0.0, -0.06, -0.10), hip_rot=(6.0, 0, 8.0), spine=(12.0, -2.0, 18.0), neck=(0, 0, -4.0), head=(-10.0, 0, -10.0),
                   R_tip=(-0.14, -1.40, 1.98), R_dir=norm((0.04, -1.0, -0.05)), R_pole=norm((-1.0, 0.2, -0.6)), R_shrug=6.0,
                   R_lock=1.0, R_wtip=SHOT_MUZZLE, R_wdir=SHOT_DIR,
                   L_tip=(0.78, 0.20, 1.20), L_dir=norm((0.25, 0.45, -1.0)), L_pole=norm((1.0, 0.3, 0.0)), L_shrug=0.0)
    keys = [
        K(I, 0),
        # r03 (verification): the old frame-8 key put the wrist below the shoulder with the spike already pointing up,
        # so the spike folded back into the forearm (wrist fold 165 deg at frames 6-9, spike hidden, wrist skin torn).
        # Now the arm swings back like a pendulum (spike follows the forearm), then the wrist turns the spike over the top.
        K(I, 4, **dict(cock, spine=(-2.0, 1.0, -8.0), hip=(0.0, 0.02, -0.04), hip_rot=(-1.0, 0, -4.0), head=(-3.0, 0, 6.0),
                       R_tip=(-0.87, 0.30, 1.10), R_dir=norm((-0.3, 0.3, -0.9)), R_shrug=6.0, R_pole=norm((-1.0, -0.1, -0.3)))),
        K(I, 8, **dict(cock, spine=(-4.0, 2.0, -18.0), hip=(0.0, 0.04, -0.06),
                       R_tip=(-0.72, 0.60, 2.42), R_dir=norm((-0.2, 0.5, 0.85)), R_pole=norm((-1.0, 0.0, -0.3)))),
        K(I, 15, **cock),
        # hold a beat, spike trembling back (telegraph)
        K(I, 18, **dict(cock, spine=(-9.0, 3.0, -33.0), R_tip=(-0.37, 0.0, 2.64), R_wtip=SHOT_MUZZLE, R_wdir=SHOT_DIR)),
        K(I, SHOT_RELEASE, "in", **release),
        K(I, 25, "out", **dict(release, spine=(18.0, -3.0, 26.0), hip_rot=(8.0, 0, 11.0),
                               R_tip=(0.20, -1.10, 1.20), R_dir=norm((0.35, -0.7, -0.65)), R_lock=0.0)),
        K(I, 33, "auto"),
    ]
    return {"frames": 33, "loop": False, "contact": SHOT_RELEASE, "params": Track(keys, D, DIRS),
            "events": {"cock": [0, 15], "hold": [15, 18], "release": SHOT_RELEASE, "follow": [21, 25], "recover": [25, 33]}}
