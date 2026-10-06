"""Крушение v2 (Pelag_AN_Wreck2_*): клипы по листам B (позы 1–7, wk_clipkeys) и B2 (формы, позы 8–11, wk_formkeys)
на риге v6, кадр = тик, корень стоит.

Запуск: blender -b --factory-startup -P wk_author.py -- <out_dir>
  WK_ONLY=<подстроки через запятую> — только эти клипы; WK_NOEXPORT=1 — без выгрузки; WK_QUICK=1 — без пересечений;
  WK_PREVIEW=<папка> — кадры поз прямо с рига (WK_PREVIEW_FRAMES=key|all, WK_PREVIEW_VIEWS=side,q34,front,top,game).
Конвейер Абордажа v2 (abordage-2026-10-02/animation/scripts): стойка серии сабли (SaberCombo кадр 2) на v6, слои
таз/скрутка/наклон/ноги/голова (wk_pose.py), обе кисти на рукояти цепи (wk_grip.py), ключи — wk_clips.py,
пределы — wk_check.py, кистень для проверки хвата — wk_flail.py, timing.json — wk_timing.py.
"""
import bpy, sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from s_lib import Body
from wk_rig import Rig, M, SIDES
import wk_grip, wk_pose
from wk_clipkeys import clips as clip_keys
from wk_formkeys import forms as form_keys, COPY_FRAMES
import copy as _copy
from wk_sample import sample, measure

OUT = sys.argv[sys.argv.index("--") + 1:][0]
os.makedirs(OUT, exist_ok=True)
ONLY = os.environ.get("WK_ONLY", "")
P = "Pelag_AN_Wreck2_"
HERE = os.path.dirname(os.path.abspath(__file__))

rig = Rig()
print("FIST", wk_grip.prepare(rig))
body = Body(rig.mesh)
B = rig.B
SF = rig.STANCE_FIST
CLIPS = clip_keys(B, SF)
CLIPS.update(form_keys(B, SF, CLIPS))         # формы (лист B2) — после базы: их ключи берут решения рук базы подсказкой
stance_snap = rig.snapshot()
snaps, rows, keyinfo = {}, {}, {}
CACHE = {}
for name, keys in CLIPS.items():
    if ONLY and not any(o in name for o in ONLY.split(",")): continue
    last = max(keys)
    keyinfo[name] = {}
    prev_sol = None
    for kf in sorted(keys):                 # ключи: руки перебором рядом с решением прошлого ключа (без перескоков)
        k = keys[kf]
        hf = k.get("hint_from")
        if k["sol"] is None and hf is not None and hf.get("sol") is not None and not k["hint"]:
            k["hint"] = dict(hf["sol"])            # копия ключа базы (Панцирь): руки рядом с решением базы
        hm = k.get("hint_mix")
        if k["sol"] is None and hm is not None and hm[0].get("sol") is not None and hm[1].get("sol") is not None and not k["hint"]:
            k["hint"] = {s_: tuple(a_ + (b_ - a_) * hm[2] for a_, b_ in zip(hm[0]["sol"][s_], hm[1]["sol"][s_])) for s_ in SIDES}
        if k["sol"] is None and prev_sol is not None and not k["hint"]:
            k["hint"] = dict(prev_sol)
        if k["sol"] is None:
            info = wk_pose.apply(rig, k, search=True)
            keyinfo[name][kf] = info
        prev_sol = k["sol"]
    if name.endswith("Charge") and keys[last] is keys[0]:   # цикл заряда: второй проход по кругу — кадр 0 в ветке кадра 11
        order = [kf for kf in sorted(keys) if kf != last]
        for i_, kf in enumerate(order * int(os.environ.get("WK_CYCLE_PASSES", "1"))):
            k = keys[kf]; k["hint"] = dict(keys[order[(i_ - 1) % len(order)]]["sol"]); k["sol"] = None
            keyinfo[name][kf] = wk_pose.apply(rig, k, search=True)
    snaps[name], rows[name] = [], []
    for f in range(last + 1):
        k = keys.get(f)
        if k is not None and id(k) in CACHE:
            rig.restore(CACHE[id(k)]); info = None
        else:
            info = wk_pose.apply(rig, k if k is not None else sample(keys, f))
            if k is not None: CACHE[id(k)] = rig.snapshot()
        snaps[name].append(rig.snapshot())
        row = measure(rig, body); row.update(frame=f, key=k is not None)
        pk = k if k is not None else sample(keys, f)
        dch = pk["chain"]; n_ = math.sqrt(sum(c * c for c in dch)) or 1.0
        row["chain_dir"] = [round(c / n_, 4) for c in dch]          # ось цепи от правого кулака (плановая, к голове)
        row["gL"], row["gR"] = round(pk["gL"] * pk["W"], 3), round(pk["gR"] * pk["W"], 3)
        row["on_grip"] = row["gL"] >= 0.9 and row["gR"] >= 0.9
        if info: row.update(miss={s: round(info["miss"][s], 3) for s in SIDES}, head_fix=info["head_fix"])
        row["sol"] = {s_: [round(x, 1) for x in pk["sol"][s_]] for s_ in SIDES}
        row["elbow"] = {s_: [round(-rig.P(s_ + "ForeArm").y, 3), round(rig.P(s_ + "ForeArm").x, 3), round(rig.P(s_ + "ForeArm").z, 3)] for s_ in SIDES}
        rows[name].append(row)
        if os.environ.get("WK_DBG") and len(snaps[name]) > 1:
            import wk_check as _wc
            dd = _wc.deltas(snaps[name][-2], snaps[name][-1])
            top = sorted(dd.items(), key=lambda kv: -kv[1])[:5]
            print("DBG %s %2d sol %s elbow %s top %s" % (name[len(P):], f, row["sol"], row["elbow"], [(b_, round(v_)) for b_, v_ in top]))
        print("ROW %s %2d lean %5.1f pY %6.1f cY %6.1f tw %5.1f gap %.3f axerr %s miss %s ring %s fore %s feet L %.3f R %.3f" % (
            name[len(P):], f, row["lean"], row["pelvis_yaw"], row["chest_yaw"], row["twist"], row["hand_gap"], row["axis_err"],
            row.get("miss"), row["ring"], row["forearm_twist"], row["Left"]["toe_z"], row["Right"]["toe_z"]))

    if name in COPY_FRAMES and COPY_FRAMES[name][0] in snaps:     # точная копия кадров другого клипа (Slam_Drag 0–12 = Slam)
        src, a_, b_ = COPY_FRAMES[name]
        for f in range(a_, b_ + 1):
            snaps[name][f] = snaps[src][f]; rows[name][f] = _copy.deepcopy(rows[src][f])
        print("COPY %s %d..%d <- %s" % (name, a_, b_, src))

exec(open(os.path.join(HERE, "wk_preview.py"), encoding="utf-8").read())
if os.path.exists(os.path.join(HERE, "wk_finish.py")):
    exec(open(os.path.join(HERE, "wk_finish.py"), encoding="utf-8").read())
