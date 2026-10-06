"""Бросок якоря (Pelag_AN_AnchorThrow_*): клипы по листу B-key-poses-higgsfield.png и видео anchor-throw-kling.mp4 на риге v6.

Запуск: blender -b --factory-startup -P at_author.py -- <out_dir>
  AT_ONLY=<подстрока>  — только клипы с этой подстрокой (без проверки скорости раскладок); AT_NOEXPORT=1 — без выгрузки;
  AT_QUICK=1 — без пересечений; AT_PREVIEW=<папка> — кадры поз на риге сборки; AT_DBG=1 — повороты костей по кадрам.
Конвейер Абордажа v2 (abordage-2026-10-02/animation/scripts/ab_author.py): стойка серии сабли (SaberCombo кадр 2),
перенесённая на v6 как в Unity, таз опущен, стопы на земле; слои таз/скрутка/наклон/ноги/руки/голова (at_pose.py),
позы — at_keys.py, пределы и раскладки Sim — at_check.py. Кадр = тик (30 к/с) при самой длинной раскладке, корневого
хода нет, таз по XY стоит.
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from s_lib import Body
from at_rig import Rig, M, SIDES
import at_pose, at_arm, at_check, at_fingers
from at_pose import apply
from at_keys import clips as clip_keys
from at_measure import sample, measure, bone_deltas

OUT = sys.argv[sys.argv.index("--") + 1:][0]
os.makedirs(OUT, exist_ok=True)
ONLY = os.environ.get("AT_ONLY", "")
P = "Pelag_AN_AnchorThrow_"

rig = Rig()
at_arm.prepare(rig)
at_fingers.prepare(rig)
body = Body(rig.mesh)
B = rig.B
print("BASE pyaw %.1f cyaw %.1f lean %.1f relP %.1f" % (B["pyaw"], B["cyaw"], B["lean"], B["head"]["relP"]))
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

if os.environ.get("AT_DBG"):
    BN = ("RightShoulder", "RightArm", "RightForeArm", "RightHand", "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
          "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot", "Hips", "Spine", "Neck", "Head", "RightHandIndex1")
    for name, ss in snaps.items():
        for f in range(len(ss) - 1):
            d = at_check.deltas(ss[f], ss[f + 1])
            print("DBG %s %2d>%2d %s" % (name[len(P):], f, f + 1, " ".join("%s:%.0f" % (b.replace("Right", "R").replace("Left", "L"), d[b]) for b in BN)))

# ------------------------------------------------------------- стыки
SEAMS = [("stance", P + "Throw"), (P + "Throw", P + "Fly"), (P + "Fly", P + "Yank"), (P + "Yank", P + "Haul"),
         (P + "Haul", P + "Catch"), (P + "Catch", "stance")]
seams = []
for a, b in SEAMS:
    if (a != "stance" and a not in snaps) or (b != "stance" and b not in snaps): continue
    sa = stance_snap if a == "stance" else snaps[a][-1]
    sb = stance_snap if b == "stance" else snaps[b][0]
    w = bone_deltas(sa, sb)
    seams.append(dict(from_clip=a, from_frame="last", to_clip=b, max_bone_deg=round(w[0], 2), bone=w[1]))
    print("seam %s -> %s: %.2f° %s" % (a, b, w[0], w[1]))
if P + "Haul" in snaps:                                # короткий возврат входит в Haul с кадра 3 (= кадру 0)
    w = bone_deltas(snaps[P + "Haul"][0], snaps[P + "Haul"][3])
    seams.append(dict(from_clip=P + "Yank", from_frame="last", to_clip=P + "Haul", to_frame=3, max_bone_deg=round(w[0], 2), bone=w[1]))
    print("seam Yank -> Haul@3: %.2f° %s" % (w[0], w[1]))

# ------------------------------------------------------------- пределы
summary, viol, per_tick = at_check.check(rig, body, dict(snaps, _stance=stance_snap), quick=bool(os.environ.get("AT_QUICK")))
at_check.print_summary(summary, viol)
for c in summary.values():
    c.pop("rows", None)
out = dict(rows=rows, seams=seams, per_tick=per_tick, check=dict(summary=summary, violations=[[v[0], v[1], str(v[2])] for v in viol]))
p = os.path.join(OUT, "_measure.json")
open(p, "a").close()
f = open(p, "r+", encoding="utf-8", newline=""); f.seek(0); f.write(json.dumps(out, indent=1, ensure_ascii=False)); f.truncate(); f.close()
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "at_preview.py"), encoding="utf-8").read())
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "at_export.py"), encoding="utf-8").read())
