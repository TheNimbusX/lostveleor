"""Абордаж v2 (Pelag_AN_Abordage2_*): клипы по листу ключевых поз B-key-poses-chatgpt.png на риге v6.

Запуск: blender -b --factory-startup -P ab_author.py -- <out_dir>
  AB_ONLY=<подстрока>  — только клипы с этой подстрокой; AB_NOEXPORT=1 — без выгрузки; AB_QUICK=1 — без пересечений.
Конвейер Шквала v2 (squall-forms-2026-10-02/animation/scripts/sq_author.py): стойка серии сабли (SaberCombo кадр 2),
перенесённая на v6 как в Unity, таз опущен, стопы на земле; слои таз/скрутка/наклон/ноги/руки/голова (ab_pose.py),
позы — ab_keys.py, пределы — ab_check.py. Кадр = тик (30 к/с) при самой длинной раскладке, корневого хода нет,
таз по XY стоит.
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from s_lib import Body
from ab_rig import Rig, M, SIDES
import ab_pose, ab_arm, ab_check, ab_fingers
from ab_pose import apply
from ab_keys import clips as clip_keys
from ab_measure import sample, measure, bone_deltas

OUT = sys.argv[sys.argv.index("--") + 1:][0]
os.makedirs(OUT, exist_ok=True)
ONLY = os.environ.get("AB_ONLY", "")
P = "Pelag_AN_Abordage2_"

rig = Rig()
ab_arm.prepare(rig)
ab_fingers.prepare(rig)
print("FINGERS", ab_fingers.report())
body = Body(rig.mesh)
B = rig.B
print("BASE pyaw %.1f cyaw %.1f lean %.1f relP %.1f" % (B["pyaw"], B["cyaw"], B["lean"], B["head"]["relP"]))
print("BASE hand_rootL %s hand_rootR %s" % ([round(c, 3) for c in B["hand_rootL"]], [round(c, 3) for c in B["hand_rootR"]]))
CLIPS = clip_keys(B)
stance_snap = rig.snapshot()
snaps, rows = {}, {}
CACHE = {}
for name, keys in CLIPS.items():
    if ONLY and not any(o in name for o in ONLY.split(",")): continue
    last = max(keys)
    for kf in sorted(keys):
        k = keys[kf]
        if k.get("fixed"): continue
        if k["W"] <= 1e-6: k["fixed"] = True; continue
        info = apply(rig, k)
        k["sv"], k["tv"], k["wd"], k["wf"] = info["swivel_twist"][0], float(info["swivel_twist"][1]), info["wd"], info["wf"]; k["fixed"] = True
    snaps[name], rows[name] = [], []
    for f in range(last + 1):
        p = sample(keys, f)
        k = keys.get(f)
        if k is not None and id(k) in CACHE:          # общая стыковая поза — ровно тот же снимок
            rig.restore(CACHE[id(k)])
        elif p["W"] <= 1e-6:
            rig.reset()
        else:
            apply(rig, p)
        if k is not None and id(k) not in CACHE:
            CACHE[id(k)] = rig.snapshot()
        snaps[name].append(rig.snapshot())
        row = measure(rig, body); row.update(frame=f, W=round(p["W"], 3))
        rows[name].append(row)

# ------------------------------------------------------------- отладка: повороты костей по кадрам
if os.environ.get("AB_DBG"):
    BN = ("RightShoulder", "RightArm", "RightForeArm", "RightHand", "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
          "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot", "Hips", "Spine", "Neck", "Head")
    for name, ss in snaps.items():
        for f in range(len(ss) - 1):
            d = ab_check.deltas(ss[f], ss[f + 1])
            print("DBG %s %2d>%2d %s" % (name[len(P):], f, f + 1, " ".join("%s:%.0f" % (b.replace("Right", "R").replace("Left", "L"), d[b]) for b in BN)))
        d = ab_check.deltas(ss[0], ss[-1])
        print("DBG %s  0>%2d %s" % (name[len(P):], len(ss) - 1, " ".join("%s:%.0f" % (b.replace("Right", "R").replace("Left", "L"), d[b]) for b in BN)))
    for name, ss in snaps.items():
        for lab, a, b, strike in ab_check.ticks(name, len(ss)):
            if not any(lab.startswith(x) for x in ("A1 ", "A2 ", "A3 ", "P2 ", "P3 ", "P4 ", "P5 ", "P6 ")): continue
            pts = [a] + [float(k) for k in range(int(math.floor(a)) + 1, int(math.ceil(b - 1e-9)))] + [b]
            sx = [ab_check.interp(ss, x) for x in pts]; d = {}
            for j in range(len(sx) - 1):
                for bn, ang in ab_check.deltas(sx[j], sx[j + 1]).items(): d[bn] = d.get(bn, 0.0) + ang
            arm_ = max((d[x], x) for x in ab_check.ARM_R); bod_ = max((v, k) for k, v in d.items() if k not in ab_check.ARM_R)
            print("DBGT %s %-7s %5.2f→%5.2f %s arm %.1f %s body %.1f %s" % (name[len(P):], lab, a, b, "S" if strike else "-", arm_[0], arm_[1], bod_[0], bod_[1]))
    for name, ks in CLIPS.items():
        for f in sorted(ks):
            r = rows[name][f]
            print("DBGF %s %2d lean %.1f pY %.1f cY %.1f tw %.1f hipz %.3f R flex %.0f up %s L flex %.0f up %s feetz L %.3f R %.3f" % (
                name[len(P):], f, r["lean"], r["pelvis_yaw"], r["chest_yaw"], r["twist"], r["hips_z"], r["armRight"]["flex"],
                r["armRight"]["up"], r["armLeft"]["flex"], r["armLeft"]["up"], r["Left"]["toe_z"], r["Right"]["toe_z"]))

# ------------------------------------------------------------- стыки
SEAMS = [("stance", P + "Throw"), (P + "Throw", P + "Pull"), (P + "Pull", P + "Punch"),
         (P + "Punch", P + "Recover"), (P + "Recover", "stance")]
seams = []
for a, b in SEAMS:
    if (a != "stance" and a not in snaps) or (b != "stance" and b not in snaps): continue
    sa = stance_snap if a == "stance" else snaps[a][-1]
    sb = stance_snap if b == "stance" else snaps[b][0]
    w = bone_deltas(sa, sb)
    seams.append(dict(from_clip=a, from_frame="last", to_clip=b, max_bone_deg=round(w[0], 2), bone=w[1]))
    print("seam %s -> %s: %.2f° %s" % (a, b, w[0], w[1]))

# ------------------------------------------------------------- пределы
summary, viol = ab_check.check(rig, body, dict(snaps, _stance=stance_snap), quick=bool(os.environ.get("AB_QUICK")))
ab_check.print_summary(summary, viol)
per_tick = {}
for name, ss in snaps.items():
    per_tick[name] = [[lab, a, b, strike, max(ab_check.deltas(ab_check.interp(ss, a), ab_check.interp(ss, b)).items(), key=lambda kv: kv[1])]
                      for lab, a, b, strike in ab_check.ticks(name, len(ss))]
for c in summary.values():
    c.pop("rows", None)
out = dict(rows=rows, seams=seams, per_tick=per_tick, check=dict(summary=summary, violations=[[v[0], v[1], str(v[2])] for v in viol]))
p = os.path.join(OUT, "_measure.json")
open(p, "a").close()
f = open(p, "r+", encoding="utf-8", newline=""); f.seek(0); f.write(json.dumps(out, indent=1, ensure_ascii=False)); f.truncate(); f.close()
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ab_preview.py"), encoding="utf-8").read())
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "ab_export.py"), encoding="utf-8").read())
