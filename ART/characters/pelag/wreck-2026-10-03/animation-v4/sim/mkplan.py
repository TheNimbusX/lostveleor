"""План серии для wreck4sim: python mkplan.py <author_dir> <L> <out.json> <variant> [yaw2_deg]
variant: series (Swing1, Swing2, Lunge 0–24) | full (Draw, Swing1, Swing2, Lunge 0–16, Stow) | turn (как full, удар 2 и выпад — под yaw2)"""
import json, sys, math
A, L, OUT, VAR = sys.argv[1], float(sys.argv[2]), sys.argv[3], sys.argv[4]
yaw2 = math.radians(float(sys.argv[5])) if len(sys.argv) > 5 else 0.0
T = lambda c: "%s/Pelag_AN_Wreck4_%s.track.json" % (A, c)
zone = dict(fwd=1.25, side=0.0, h=0.85, deg=25, rMin=1.0, rMax=1.5, hMin=0.6, hMax=1.0)
lunge = {"release": 4.5, "contact": 8, "hold": 10.5, "reach": 2.2, "landPitch": 50, "landSpeed": 14, "arcUp": 0.9, "arcFwd": 0.2, "arcBack": 0.8, "arcHigh": 1.5}
seg = lambda c, a, b, **k: dict(track=T(c), **{"from": a, "to": b}, **k)
if VAR == "series":
    segs = [seg("Swing1", 0, 9), seg("Swing2", 0, 9), seg("Lunge", 0, 24)]; o = 0
else:
    turn = dict(yaw=yaw2, turn=3) if VAR == "turn" else {}
    segs = [seg("Draw", 0, 8), seg("Swing1", 0, 9), seg("Swing2", 0, 9, **turn), seg("Lunge", 0, 16), seg("Stow", 0, 8)]; o = 1
p = {"repo": "C:/Users/d.grab/Desktop/the-game", "chainSwing": L, "reelSpeed": 9, "fps": 60, "preroll": 1.5, "segments": segs,
     "follow": [{"seg": o, "from": 0.0, "to": 5.5, "omega": 16}, {"seg": o + 1, "from": 0.0, "to": 5.5, "omega": 16},
                {"seg": o + 2, "from": 0.0, "to": 4.5, "omega": 16}],
     "contacts": [dict(seg=o, frame=5, **zone), dict(seg=o + 1, frame=5, **zone)],
     "lunge": dict(seg=o + 2, **lunge)}
if VAR != "series":
    p["draw"] = {"seg": 0, "grab": 3.25, "end": 8, "lift": 0.9}
    p["stow"] = {"seg": 4, "reel": 0.0, "mount": 3.5, "length": 0.3, "speed": 5, "omega": 30}
    p["tail"] = 0.3
json.dump(p, open(OUT, "w"), indent=1)
