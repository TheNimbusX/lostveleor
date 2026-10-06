"""Шквал v2 (Pelag_AN_Squall2_*): клипы по листу ключевых поз B-key-poses-chatgpt.png на риге v6.

Запуск: blender -b --factory-startup -P sq_author.py -- <out_dir>
  SQ_ONLY=<подстрока>  — только клипы с этой подстрокой (пробы); SQ_NOEXPORT=1 — без выгрузки.
Конвейер рывка (dash-2026-10-02/animation/scripts/d_author.py): стойка серии сабли (SaberCombo кадр 2),
перенесённая на v6 как в Unity, таз опущен, стопы на земле; слои таз/скрутка/наклон/ноги/руки/клинок/голова
(sq_pose.py), позы — sq_keys.py, проверка пределов — sq_check.py. Кадр = тик (30 к/с), корневого хода нет,
таз по XY стоит.
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from s_lib import Body
from sq_rig import Rig, M, SIDES
import sq_pose, sq_arm, sq_check
from sq_pose import apply
from sq_keys import clips as clip_keys
from sq_measure import sample, measure, bone_deltas

OUT = sys.argv[sys.argv.index("--") + 1:][0]
os.makedirs(OUT, exist_ok=True)
ONLY = os.environ.get("SQ_ONLY", "")

rig = Rig()
sq_arm.prepare(rig)
body = Body(rig.mesh)
B = rig.B
print("BASE pyaw %.1f cyaw %.1f lean %.1f relP %.1f foot yaw %s hands %s" % (
    B["pyaw"], B["cyaw"], B["lean"], B["head"]["relP"], {s: round(v, 1) for s, v in B["foot_yaw"].items()},
    {s: tuple(round(c, 3) for c in v) for s, v in B["hand"].items()}))
CLIPS = clip_keys(B)
stance_snap = rig.snapshot()
snaps, rows = {}, {}
CACHE = {}
for name, keys in CLIPS.items():
    if ONLY and not any(o in name for o in ONLY.split(",")): continue
    last = max(keys)
    # Ключи: решение руки с клинком (пронация в окне) один раз на ключ; кадры между ключами берут его интерполяцией.
    for kf in sorted(keys):
        k = keys[kf]
        if k.get("fixed"): continue
        if k["W"] <= 1e-6: k["fixed"] = True; continue
        info = apply(rig, k)
        k["sv"], k["tv"], k["wd"], k["wf"] = info["swivel_twist"][0], float(info["swivel_twist"][1]), info["wd"], info["wf"]; k["fixed"] = True
        print("key %-12s f%d sw %5.1f tv %5.1f wd %5.1f wf %5.1f miss %5.1f" % (name[16:], kf, k["sv"], k["tv"], k["wd"], k["wf"], info["blade_miss"]))
    snaps[name], rows[name] = [], []
    for f in range(last + 1):
        p = sample(keys, f)
        k = keys.get(f)
        if k is not None and id(k) in CACHE:          # общая стыковая/контактная поза — ровно тот же снимок
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

# ------------------------------------------------------------- стыки
SEAMS = [("stance", "Pelag_AN_Squall2_Load", None),
         ("Pelag_AN_Squall2_Load", "Pelag_AN_Squall2_Forehand", None),
         ("Pelag_AN_Squall2_Forehand", "Pelag_AN_Squall2_Backhand", None),
         ("Pelag_AN_Squall2_Backhand", "Pelag_AN_Squall2_Forehand", None),
         ("Pelag_AN_Squall2_Forehand", "Pelag_AN_Squall2_FinishFore", 6),
         ("Pelag_AN_Squall2_Backhand", "Pelag_AN_Squall2_FinishBack", 6),
         ("Pelag_AN_Squall2_Forehand", "Pelag_AN_Squall2_ReturnFore", None),
         ("Pelag_AN_Squall2_Backhand", "Pelag_AN_Squall2_ReturnBack", None),
         ("Pelag_AN_Squall2_FinishFore", "stance", None), ("Pelag_AN_Squall2_FinishBack", "stance", None),
         ("Pelag_AN_Squall2_ReturnFore", "stance", None), ("Pelag_AN_Squall2_ReturnBack", "stance", None)]
seams = []
for a, b, at in SEAMS:
    if (a != "stance" and a not in snaps) or (b != "stance" and b not in snaps): continue
    sa = stance_snap if a == "stance" else snaps[a][at if at is not None else -1]
    sb = stance_snap if b == "stance" else snaps[b][0]
    w = bone_deltas(sa, sb)
    seams.append(dict(from_clip=a, from_frame=("last" if at is None else at), to_clip=b, max_bone_deg=round(w[0], 2), bone=w[1]))
    print("seam %s[%s] -> %s: %.2f° %s" % (a, at, b, w[0], w[1]))

# ------------------------------------------------------------- пределы
summary, viol = sq_check.check(rig, body, dict(snaps, _stance=stance_snap), quick=bool(os.environ.get("SQ_QUICK")))
sq_check.print_summary(summary, viol)
per_tick = {}
for name, ss in snaps.items():
    per_tick[name] = [[lab, a, b, strike, max(sq_check.deltas(sq_check.interp(ss, a), sq_check.interp(ss, b)).items(), key=lambda kv: kv[1])]
                      for lab, a, b, strike in sq_check.ticks(name, len(ss))]
for c in summary.values():
    c.pop("rows", None)
out = dict(rows=rows, seams=seams, per_tick=per_tick, check=dict(summary=summary, violations=[[v[0], v[1], str(v[2])] for v in viol]))
p = os.path.join(OUT, "_measure.json")
open(p, "a").close()
f = open(p, "r+", encoding="utf-8", newline=""); f.seek(0); f.write(json.dumps(out, indent=1, ensure_ascii=False)); f.truncate(); f.close()
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "sq_export.py"), encoding="utf-8").read())
