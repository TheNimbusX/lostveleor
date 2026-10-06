"""План для wreck4anchor со слоем «цепь-хлыст» (06.10): python mkplan_w.py <clips_dir> <out.json> full|turn|pause|before [yaw2_deg] [key=value ...]
full/turn — серия v5 + "whip" (вторичный поворот головы и живая цепь); before — та же серия без него (прошлое, регрессия);
pause — снятие → мах 1 → мах 2 до конца клипа (тик 15) → пауза 1,6 с (тело стоит, окно нажатия прошло): якорь висит.
key=value: whip.swing=0.53 whip.drag=1.2 hang.air=0.8 mace.omega=16 ..."""
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
     "stow": {"seg": 4, "start": 0, "lay0": 3.0, "lay1": 4.0, "land": 5.5, "landSpeed": 1.5, "up": 0.45, "out": 0.4, "back": 0.15},
     "whip": {"links": 16, "sub": 4, "iters": 8, "swing": 0.53, "flightSlack": 0.3, "biteSlack": 0.45, "take": 0.06, "snap": 0.85, "release": 0.025, "drag": 1.2,
              "fold": 0.55, "gripAim": 0.3, "friction": 18, "secW": 20, "secZ": 0.5, "secF": 0.85, "rollW": 14, "rollZ": 0.75, "rollF": 0.8, "maxDev": 22, "secMaxW": 22}}
if VAR == "pause":
    p["segments"] = [seg("Draw", 0, 8), seg("Swing1", 0, 9), seg("Swing2", 0, 15), seg("Swing2", 15, 63)]
    p["tail"] = 0.0
    for k in ("lunge", "stow"): del p[k]
    p["hang"] = {"seg": 2, "start": 9.5, "blend": 0.2, "air": 0.8, "kill": 7, "slack": 0.015}
if VAR == "before": del p["whip"]
for kv in rest:
    k, v = kv.split("="); a, b = k.split(".")
    p.setdefault(a, {})[b] = float(v)
json.dump(p, open(OUT, "w"), indent=1)
