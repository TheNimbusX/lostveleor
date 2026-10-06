"""Бросок якоря: timing.json — тайминг, раскладки Sim → кадры, ключевые кадры, опорные лодыжки, треки хвата, проверки.
python at_timing.py <anim_dir> <v6_check.json> <indep measure.json> <thrown-check.json>   (читает <anim_dir>/_measure.json от at_author.py)"""
import sys, os, json, math, hashlib

anim = sys.argv[1]
M = json.load(open(os.path.join(anim, "_measure.json"), encoding="utf-8"))
V6 = json.load(open(sys.argv[2], encoding="utf-8"))
IND = json.load(open(sys.argv[3], encoding="utf-8"))
TH = json.load(open(sys.argv[4], encoding="utf-8"))
P = "Pelag_AN_AnchorThrow_"
ROWS, CHK = M["rows"], M["check"]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
G = "C:/Users/d.grab/Desktop/the-game/"


def sha(path):
    h = hashlib.sha256(); h.update(open(path, "rb").read()); return h.hexdigest()


def virtual(F):
    if F >= 3: return [0.0, 1.0, 2.0, 3.0] + [round(3.0 + 3.0 * j / (F - 2), 4) for j in range(1, F - 1)]
    return [0.0, 1.0, 3.5, 6.0] if F == 2 else [0.0, 1.0, 6.0]


def vmap(v):
    return ["Throw", 2.0 + v] if v <= 3.0 else (["Fly", round(v - 3.0, 4)] if v < 6.0 else ["Yank", 0.0])


HAUL = {8: [0, 1, 2, 3, 4, 5, 6], 7: [0, 1, 2, 3, 4.5, 6], 6: [0, 1.5, 3, 4.5, 6], 5: [3, 4, 5, 6], 4: [3, 4.5, 6]}


def layout(W, F, R):
    out = [["Throw", round(2.0 * k / W, 4)] for k in range(W + 1)]
    out += [vmap(v) for v in virtual(F)[1:]]
    out += [["Yank", 1.0]]
    h = HAUL[R]
    out += [["Haul", float(h[0])] if h[0] > 0 else ["Yank", 2.0]]
    out += [["Haul", float(x)] for x in h[1:-1]] + [["Catch", float(j)] for j in range(10)]
    return [[t] + x for t, x in enumerate(out)]


def ankles(clip):
    out = []
    for r in ROWS[clip]:
        row = dict(frame=r["frame"])
        for s in ("Left", "Right"):
            x, y, z = r[s]["ankle"]
            row[s] = dict(f=round(-y, 3), l=round(x, 3), u=round(z, 3),
                          contact=("flat" if z < 0.17 else "toe") if r[s]["toe_z"] < 0.012 else "air")
        out.append(row)
    return out


S = CHK["summary"]
allr = [(c, r) for c, rs in ROWS.items() for r in rs]
tw = max(allr, key=lambda cr: abs(cr[1]["twist"]))
fa = max(allr, key=lambda x: max(abs(v) for v in x[1]["forearm_twist"].values()))
LIM = dict(
    source="at_check.py при сборке (снимки позы, все раскладки W 2–3 × F 1–10 × R 4–8) + независимая проверка выгруженных FBX "
           "методом проверяющего Шквала/Абордажа (artifacts/anchor-throw/clips-check/measure.py, summary.py)",
    violations_build=len(CHK["violations"]),
    violations_independent=sum(1 for k, ticks in IND["retime"].items() for t in ticks for a, b in t["top"]
                               if a > (70 if (t["strike"] and b in ("RightArm", "RightForeArm", "RightHand")) else 35) + 0.05),
    per_tick=dict(rule="ПУТЬ кости за тик (сумма поворотов по всем целым кадрам, которые тик проходит, и через стыки клипов) ≤ 35° "
                       "во всех раскладках; правая рука ≤ 70° в тики удара (выпуск C+W−1→C+W и первый тик полёта); пальцы — кости тела",
                  worst_build=S["_speed"]["worst"], ticks_checked=S["_speed"]["n_ticks"]),
    seam_max_bone_deg=dict(limit=0.0, value=max(s["max_bone_deg"] for s in M["seams"]), seams=M["seams"],
                           independent=[dict(frm=s["frm"], to=s["to"], top=s["top"][0]) for s in IND["seams"]]),
    between_frames={c[len(P):]: s["sub"] for c, s in S.items() if not c.startswith("_")},
    lean_deg=dict(rule="бросок вперёд ≤ 35°; назад только замах (≥ −8°); натяг и тяга НАЗАД 12–25° (спека §4: исключение из правила «назад — никогда»)",
                  ranges={c[len(P):]: [min(r["lean"] for r in rs), max(r["lean"] for r in rs)] for c, rs in ROWS.items()}),
    chest_to_pelvis_twist_deg=dict(limit=45.0, value=abs(tw[1]["twist"]), at=[tw[0], tw[1]["frame"]]),
    forearm_twist_from_rest_deg=dict(limit=70.0, value=max(abs(v) for v in fa[1]["forearm_twist"].values())),
    head=dict(rule="голова за грудью (вбок ≤ 30°, вверх к груди не выше стойки); в натяге и тяге корпус откинут — голова опущена к груди, "
                   "лицо держится на уровне стойки (к Dir)", max_relP_over_stance=max(r["relP_over_stance"] for c, r in allr)),
    feet="левая (опорная, вокруг неё вид крутит корень на замахе) стоит плашмя на месте стойки весь навык и не едет; правая — "
         "шаги только по воздуху (замах, рывок, выход), опорные точки не скользят (между кадрами ≤ 4 мм, дрейф ≤ 6 мм); под землю — нигде",
    arms_in_thighs="нет ни в одном кадре (кисть/предплечье обеих рук против обоих бёдер, голеней, торса и головы, сетка Pelag_v6); "
                   "правая на цепи не входит в левую кисть (подбор посадки at_search.py)",
    unity_transfer=dict(max_hip_offset_ratio=V6["max_hip_offset_ratio"], limit=0.5, unity_default=0.4,
                        independent=max(r["hipoff"] for c, v in IND["clips"].items() if P in c for r in v["rows"]),
                        note="присед ограничен −7,5 см (at_pose.DZ_MIN): сборка проходит и с порогом по умолчанию"),
)
THR = dict(
    tool="at_thrown_check.py по трекам хвата grip/*.grip.json (шаг A anchor-core: anchor_grip_export.py) и формуле головы Sim "
         "(Simulation.AnchorThrow.cs AnchorThrowHead); режимы Thrown/Yank DESIGN §1.2 — цепь прямая хват → кольцо, длина = расстояние",
    limits=TH["limits"], pass_4_to_9_5_m=TH["pass_main"],
    scenarios=[dict(reach=sc["reach"], F=sc["F"], R=sc["R"], taut=sc["taut"], catch=sc["catch"], worst=sc["worst"], passed=sc["pass"])
               for sc in TH["scenarios"]],
    note="стены 1–2 м: голова у ног, цепь круто вниз — правая выходит из прямой на 6–12 см (порог 6 см) и в ловлю на 12 см "
         "(порог 10): вид держит опорную кисть лёгким SolveArm (DESIGN §4.1, ≤ 6 см) или принимает; клип под это не ломаем",
    live_catch_dry_run="anchorbake (artifacts/anchor-core/bake, сборка в artifacts/anchor-throw/obj-anchorbake) на треке Catch, старт rest и "
                       "taut:0 (голова впереди): проникновение головы 0, растяжение 0, скачков нет, провиса на скорости нет — "
                       "artifacts/anchor-throw/clips/bake; проверки 5–6 (точка/скорость маха) к ловле не относятся",
)
grips = {c: dict(file="grip/%s%s.grip.json" % (P, c), sha256=sha(os.path.join(anim, "grip", P + c + ".grip.json")),
                 fbx_sha256=sha(os.path.join(anim, P + c + ".fbx"))) for c in ("Throw", "Fly", "Yank", "Haul", "Catch")}
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "at_timing_text.py"), encoding="utf-8").read())
T["limits"] = LIM
T["thrown_check"] = THR
T["grip_tracks"] = dict(rule="шаг A конвейера запечки anchor-core (DESIGN §3.1): хват (кольцо рукояти в левой), опора правой, капсулы тела, "
                             "Spine2 в осях корня Unity по кадрам (⅛ кадра); замах Броска запечки не требует — голова в правом кулаке "
                             "(support), полёт и возврат ведёт Sim (DESIGN §3.3 п.3)", clips=grips,
                        command="blender -b --factory-startup -P artifacts/anchor-core/bake/anchor_grip_export.py -- --clip <clip.fbx> "
                                "--bind Pelag_AN_AnchorThrowBind.fbx --hip-limit .5 --dump-rig --out grip")
T["layouts"] = dict(rule="тик от нажатия → [тик, клип, кадр]; вид играет кадр по прямой между тиками (время Tick − 2 + Alpha)",
                    examples={"W2 F7 R8 (7 м)": layout(2, 7, 8), "W3 F7 R8 (курсор за спиной)": layout(3, 7, 8),
                              "W2 F4 R4 (стена 4 м)": layout(2, 4, 4), "W2 F1 R4 (стена < 1,5 м)": layout(2, 1, 4),
                              "W2 F9 R8 (9,5 м, талант)": layout(2, 9, 8)})
T["planted_ankles"] = {c[len(P):]: ankles(c) for c in ROWS}
T["rows"] = ROWS
p = os.path.join(anim, "timing.json")
open(p, "a").close()
f = open(p, "r+", encoding="utf-8", newline=""); f.seek(0); f.write(json.dumps(T, indent=1, ensure_ascii=False)); f.truncate(); f.close()
print("timing.json written; build violations", LIM["violations_build"], "independent", LIM["violations_independent"],
      "hip", LIM["unity_transfer"]["max_hip_offset_ratio"], "thrown", THR["pass_4_to_9_5_m"])
