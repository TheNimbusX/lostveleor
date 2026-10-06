"""План для wreck4anchor (правка якоря 06.10): python mkplan_v5.py <clips_dir> <out.json> full|turn [yaw2_deg] [key=value ...]
key=value переопределяет числа слоёв: mace.omega=16 draw.spin=6 stow.land=5 ..."""
import json, sys, math
A, OUT, VAR = sys.argv[1], sys.argv[2], sys.argv[3]
rest = sys.argv[4:]
yaw2 = math.radians(float(rest.pop(0))) if rest and "=" not in rest[0] else 0.0
T = lambda c: "%s/Pelag_AN_Wreck4_%s.track.json" % (A, c)
seg = lambda c, a, b, **k: dict(track=T(c), **{"from": a, "to": b}, **k)
turn = dict(yaw=yaw2, turn=3) if VAR == "turn" else {}
zone = dict(fwd=1.25, side=0.0, h=0.85, aimIn=6, aimOut=4)
p = {"repo": "C:/Users/d.grab/Desktop/the-game", "chain": 0.45, "tail": 0.3, "passes": 14,
     "segments": [seg("Draw", 0, 8), seg("Swing1", 0, 9), seg("Swing2", 0, 9, **turn), seg("Lunge", 0, 16), seg("Stow", 0, 8)],
     "mace": {"omega": 16, "zeta": 0.6, "droop": 0.45, "maxW": 19, "impactMaxW": 30, "recoil": 0.35, "soft": 0.55, "softEnd": 3, "rollRate": 3, "feed": 0.6},
     "draw": {"seg": 0, "grab": 3.25, "end": 8, "lift": 0.4, "back": 0.1, "out": 0.4, "peel": 3, "spin": 6, "pitch": 25},
     "contacts": [dict(seg=1, frame=5, **zone), dict(seg=2, frame=5, **dict(zone, aimIn=7))],
     "lunge": {"seg": 3, "release": 4.5, "contact": 8, "hold": 10.5, "short": 14.5, "reach": 2.2, "bounce": 0.12, "windup": -2, "relSide": -0.25, "relUp": 0.4, "relFwd": 0.92, "apexBack": 0.1},
     "stow": {"seg": 4, "start": 0, "lay0": 3.0, "lay1": 4.0, "land": 5.5, "landSpeed": 1.5, "up": 0.45, "out": 0.4, "back": 0.15}}
for kv in rest:
    k, v = kv.split("="); a, b = k.split(".")
    p[a][b] = float(v)
json.dump(p, open(OUT, "w"), indent=1)
