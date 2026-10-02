"""Шквал v2 (Pelag_AN_Squall2_*): клипы по листу ключевых поз B-key-poses-chatgpt.png на риге v6.

Запуск: blender -b --factory-startup -P sq_author.py -- <out_dir>
Конвейер рывка (dash-2026-10-02/animation/scripts/d_author.py): стойка серии сабли (SaberCombo кадр 2 =
Pelag_AN_Sabre1 кадр 0), перенесённая на v6 как в Unity, стопы на земле; слои таз/скрутка/наклон/ноги/руки/
клинок/голова (sq_pose.py), позы — sq_keys.py. Кадр = тик (30 к/с), корневого хода нет, таз по XY стоит.
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from s_lib import Body
from sq_rig import Rig, M, SIDES
import sq_pose
from sq_pose import apply
from sq_keys import clips as clip_keys
from sq_measure import sample, measure, bone_deltas, big_moves

OUT = sys.argv[sys.argv.index("--") + 1:][0]
os.makedirs(OUT, exist_ok=True)
ONLY = os.environ.get("SQ_ONLY", "")          # для быстрых проб: подстрока имени клипа

rig = Rig()
import sq_arm
sq_arm.prepare(rig)
body = Body(rig.mesh)
B = rig.B
print("BASE pyaw %.1f cyaw %.1f lean %.1f relP %.1f foot yaw %s hands %s" % (
    B["pyaw"], B["cyaw"], B["lean"], B["head"]["relP"], {s: round(v, 1) for s, v in B["foot_yaw"].items()},
    {s: tuple(round(c, 3) for c in v) for s, v in B["hand"].items()}))
CLIPS = clip_keys(B)
stance_snap = rig.snapshot()
snaps, rows, infos = {}, {}, {}
CACHE = {}
for name, keys in CLIPS.items():
    if ONLY and ONLY not in name: continue
    last = max(keys)
    # Ключи: перебор руки с клинком (s, t) один раз на ключ, по порядку — соседние ключи тянутся друг к другу;
    # кадры между ключами берут s, t интерполяцией (сабля и предплечье не перескакивают между решениями).
    sq_arm.LAST.clear()
    for kf in sorted(keys):
        k = keys[kf]
        if k.get("fixed"): sq_arm.LAST["Right"] = (k["sv"], k["tv"]); continue
        if k["W"] <= 1e-6: k["fixed"] = True; continue
        info = apply(rig, k)
        k["sv"], k["tv"] = info["swivel_twist"]; k["fixed"] = True
        from b_common import blade as _bl
        _r, _t = _bl(rig.dst); _d = (_t - _r).normalized(); _S = rig.P("RightArm"); _H = rig.P("RightHand")
        print("key", name[16:], kf, "s/t", info["swivel_twist"], "miss", info["blade_miss"], "got f/l/u", (round(-_d.y, 2), round(_d.x, 2), round(_d.z, 2)),
              "want", k["blade"], "reach %.2f" % ((_H - _S).length / sum(rig.ARM["Right"][x] for x in ("L1", "L2"))),
              "hand f/l/u", (round(-_H.y, 2), round(_H.x, 2), round(_H.z, 2)), "sh", (round(-_S.y, 2), round(_S.x, 2), round(_S.z, 2)))
    snaps[name], rows[name] = [], []
    for f in range(last + 1):
        p = sample(keys, f)
        k = keys.get(f)
        if k is not None and id(k) in CACHE:          # общая стыковая/контактная поза — ровно тот же снимок
            snap, info, sw = CACHE[id(k)]
            rig.restore(snap); sq_arm.LAST.update(sw)
        elif p["W"] <= 1e-6:
            rig.reset(); info = {}
        else:
            info = apply(rig, p)
        if k is not None and id(k) not in CACHE:
            CACHE[id(k)] = (rig.snapshot(), info, dict(sq_arm.LAST))
        snaps[name].append(rig.snapshot())
        row = measure(rig, body); row.update(frame=f, W=round(p["W"], 3), **info)
        rows[name].append(row)
        print("%s f%02d W%.2f lean %5.1f tw %5.1f pY %6.1f cY %6.1f relP+ %5.1f faceY %6.1f faT %s toe L %.3f R %.3f thigh %s wrist %s" % (
            name[16:], f, p["W"], row["lean"], row["twist"], row["pelvis_yaw"], row["chest_yaw"], row["relP_over_stance"],
            row["face_yaw"], row["forearm_twist"], row["Left"]["toe_z"], row["Right"]["toe_z"],
            {s: row["arm_in_thigh"][s][:2] for s in SIDES}, (info.get("blade_miss"), info.get("swivel_twist"))))

# ------------------------------------------------------------- стыки и скорость костей
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


def end_snap(a, at):
    if a == "stance": return stance_snap
    return snaps[a][at if at is not None else -1]


seams = []
for a, b, at in SEAMS:
    if (a != "stance" and a not in snaps) or (b != "stance" and b not in snaps): continue
    sb = stance_snap if b == "stance" else snaps[b][0]
    w = bone_deltas(end_snap(a, at), sb)
    seams.append(dict(from_clip=a, from_frame=("last" if at is None else at), to_clip=b, max_bone_deg=round(w[0], 2), bone=w[1]))
    print("seam %s[%s] -> %s: %.2f° %s" % (a, at, b, w[0], w[1]))
per_tick = {}
for name, ss in snaps.items():
    per_tick[name] = []
    for f in range(1, len(ss)):
        w = bone_deltas(ss[f - 1], ss[f])
        per_tick[name].append([f, round(w[0], 1), w[1]])
        bm = big_moves(ss[f - 1], ss[f])
        if bm: print('  big', name[16:], f, bm)
    print("per-tick max", name[16:], max(per_tick[name], key=lambda r: r[1]))
json.dump(dict(rows=rows, seams=seams, per_tick=per_tick), open(os.path.join(OUT, "_measure.json"), "w", encoding="utf-8"),
          indent=1, ensure_ascii=False)
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "sq_export.py"), encoding="utf-8").read())
