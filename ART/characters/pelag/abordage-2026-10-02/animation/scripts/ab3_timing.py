"""Абордаж v3: дописать timing.json (принятые Pull/Punch/Recover не меняются; Throw — новый, + PullShort, Uppercut, Slam).
python ab3_timing.py <anim_dir> <build_dir с _measure3.json> <v6_check.json кадров листа> <measure4.json независимой проверки>"""
import sys, os, json, io
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ab3_timing_clips import P, THROW, THROW_V2, SHORT, UPPER, SLAM, FORMS

anim, build = sys.argv[1], sys.argv[2]
T = json.load(open(os.path.join(anim, "timing.json"), encoding="utf-8"))
M = json.load(open(os.path.join(build, "_measure3.json"), encoding="utf-8"))
V6 = json.load(open(sys.argv[3], encoding="utf-8"))
IND = json.load(open(sys.argv[4], encoding="utf-8"))
NEW = [P + c for c in ("Throw", "PullShort", "Uppercut", "Slam")]


def throw3(W, A):
    return [round(3.0 * k / W, 4) for k in range(W + 1)] + [round(3.0 + 6.0 * j / A, 4) for j in range(1, A + 1)]


def ankles(clip):
    out = []
    for r in M["rows"][clip]:
        row = dict(frame=r["frame"])
        for s in ("Left", "Right"):
            x, y, z = r[s]["ankle"]
            row[s] = dict(f=round(-y, 3), l=round(x, 3), u=round(z, 3),
                          contact=("flat" if z < 0.17 else "toe") if r[s]["toe_z"] < 0.012 else "air")
        out.append(row)
    return out


T["skill"] = ("Абордаж v3 (03.10, решения владельца): замах через плечо +1 тик (3, цель за спиной — 4), свои клипы форм — Гейзер "
              "апперкот из приседа (Uppercut), Обвал кулак в землю (Slam), Пробоина — базовый Punch; короткая тяга P = 4…5 — нырок "
              "вдоль короткой цепи (PullShort). Pull/Punch/Recover — принятые v2 без изменений. | v2: " + T["skill"].split(" (", 1)[-1].rstrip(")"))
T["frame_is_tick"] = "Throw — при W = 3, A = 6; Pull — P = 12; PullShort — P = 5; Punch/Uppercut/Slam/Recover — 1:1; короче — растяжка по таблицам"
T["sim_timeline"] = ("C — тик каста; замах W = 3 (цель за спиной, больше 90° от взгляда, — 4): выпуск якоря C+W; зацеп B = C+W+A, "
                     "A = clamp(ceil((d − 0,5 − r_цели) / 1,5), 1, 6); тяга B+1..B+P, P = clamp(round(L / 0,66), 2, 12); удар B+P; "
                     "удержание 3; выход 6")
T["chain"] = ("Throw (0..9 по W, A) → Pull (P = 2–3, 6–12; 0..12) или PullShort (P = 4–5; 0..5) → база/Пробоина: Punch (0..3) | "
              "Гейзер: Uppercut (0..4, с тика B+P−1) | Обвал: Slam (0..4, с тика B+P−1) → Recover (0..6) → стойка; стыки 0°")
T["throw_retime"] = {str(A): throw3(3, A) for A in range(1, 7)}
T["throw_retime_turn"] = {str(A): throw3(4, A) for A in range(1, 7)}
T["throw_retime_note"] = ("v3: throw_retime — замах 3 тика (кадр 3k/3, выпуск — кадр 3), throw_retime_turn — 4 тика (цель за спиной: "
                          "3k/4); полёт якоря одинаков: тик выпуска + j → кадр 3 + 6j/A; натяг — кадр 9. Таблицы v2 (замах 2, выпуск 2, "
                          "натяг 8) — artifacts/abordage/v3-before/clips/animation/timing.json")
T["pull_short_retime"] = {"4": [0.0, 2.0, 3.0, 4.0, 5.0], "5": [0.0, 1.0, 2.0, 3.0, 4.0, 5.0]}
T["pull_choice"] = dict(N=5, rule="P = 4…5 → " + P + "PullShort (pull_short_retime), иначе (P = 2–3, 6–12) → " + P + "Pull (pull_retime)",
                        why=SHORT["why"], speed="тяга 0,66 м/тик (20 м/с): P = 4 — 2,3–2,6 м, P = 5 — 3,0–3,3 м")
T["contact"] = dict(base=dict(clip=P + "Punch", frame=0, same_as=[P + "Pull кадр 12", P + "PullShort кадр 5"], tick="B+P"),
                    geyser=dict(clip=P + "Uppercut", frame=1, starts="кадр 0 = Pull 11 = PullShort 4 в тик B+P−1", tick="B+P"),
                    quake=dict(clip=P + "Slam", frame=1, starts="кадр 0 = Pull 11 = PullShort 4 в тик B+P−1", tick="B+P"),
                    breach=dict(clip=P + "Punch", frame=0, note="как база"))
pa = T["planted_ankles"]
pa["Throw"] = ankles(P + "Throw")
for c in ("PullShort", "Uppercut", "Slam"): pa[c] = ankles(P + c)
T["planted_frames_v3"] = dict(rule="кадры, где стопа стоит (носок ≤ 1,2 см) — для GroundAbordage2Clips/Abordage2Contacts: [левая, правая]",
                              Throw=[list(range(10)), [0] + list(range(3, 10))], PullShort=[[0, 5], [0, 5]],
                              Uppercut=[[1, 2, 3, 4], [1, 2, 3, 4]], Slam=[[1, 2, 3, 4], [1, 2, 3, 4]])
T["view_requirements_v3"] = [
    "PelagAbordageClipRules: WindupTicks 3, TurnWindupTicks 4; ReleaseFrame 3, BiteFrame 9, LastFrame(Throw) 9; ThrowFrame — замах "
    "[0..3] на W тиков, полёт [3..9] на A (throw_retime / throw_retime_turn); поворот корня вокруг левой лодыжки на все W тиков",
    "новые клипы (значения enum дописать): PullShort (LastFrame 5, при P = 4–5, pull_short_retime), Uppercut и Slam (LastFrame 4, "
    "1:1 с тика B+P−1); стыки IsSeam: Throw→PullShort, PullShort→Punch, Pull/PullShort→Uppercut/Slam (в тик B+P−1), "
    "Uppercut/Slam→Recover",
    "RazlomPelagV5AnimatorBuilder: FBX Pelag_AN_Abordage2_{Throw,PullShort,Uppercut,Slam} в Assets/Resources/Characters/Pelag_v5/Mixamo/ "
    "рядом с принятыми Pull/Punch/Recover/Bind (их не трогать — байт-в-байт по позе те же); HipLimit .5 хватает (максимум 0,433); "
    "Abordage2Contacts — planted_frames_v3",
    "якорь/цепь — общий риг artifacts/anchor-core, путь Абордажа прежний; в замахе якорь висит на цепи за правым плечом (кадры 1–2)"]
T["sheet_pose_map_b2"] = {
    "7": dict(clip=P + "Uppercut", frame=2, contact_frame=1, what="Гейзер: кулак над головой после апперкота из приседа"),
    "8": dict(clip=P + "Slam", frame=1, what="Обвал: кулак вниз в землю, наклон 62°, присед"),
    "9": dict(clip=P + "PullShort", frame=2, what="нырок вдоль короткой цепи: обе стопы в воздухе, левая вперёд по цепи"),
    "10": dict(clip=P + "Throw", frame=2, what="замах через плечо: кисть над и позади правого плеча, якорь на цепи за спиной")}
T["forms"] = FORMS
T["clips"][P + "ThrowV2"] = THROW_V2
T["clips"][P + "Throw"] = THROW
T["clips"][P + "PullShort"] = SHORT
T["clips"][P + "Uppercut"] = UPPER
T["clips"][P + "Slam"] = SLAM
for c in NEW: T["rows"][c] = M["rows"][c]
S = M["check"]["summary"]
IR = IND["clips"]
T["limits_v3"] = dict(
    source="ab3_check.py при сборке (снимки позы; путь кости за тик во всех раскладках) + независимая проверка выгруженных FBX методом "
           "проверяющего (artifacts/abordage/clips-check-v4: measure4.py из clips-check/measure.py, summary4.py)",
    violations_build=len(M["check"]["violations"]),
    seams=[s for s in M["seams"]],
    seams_independent=[dict(frm=s["frm"], to=s["to"], top=s["top"][0], hips=s["hips"]) for s in IND["seams"]],
    per_tick_worst={c[len(P):]: S[c]["worst"] for c in NEW},
    between_frames={c[len(P):]: S[c]["sub"] for c in NEW},
    planted_foot_slide_m={c[len(P):]: S[c]["slide"] for c in NEW},
    pull_short_air_min_m=S[P + "PullShort"]["frac_low"],
    hip_offset=dict(unity_transfer_max=V6["max_hip_offset_ratio"], limit=0.5,
                    independent=max(r["hipoff"] for c in NEW for r in IR[c]["rows"])),
    twist_max=max(abs(r["twist"]) for c in NEW for r in M["rows"][c]),
    lean=dict(throw_windup=[M["rows"][P + "Throw"][f]["lean"] for f in (1, 2)], dive=M["rows"][P + "PullShort"][2]["lean"],
              uppercut_contact=M["rows"][P + "Uppercut"][1]["lean"], slam_contact=M["rows"][P + "Slam"][1]["lean"]),
    head_follows_chest="relP не выше стойки во всех кадрах (голова за грудью)",
    arms_in_thighs="нет ни в одном кадре новых клипов (сетка Pelag_v6, сборка и независимая проверка)",
    slam_fist=dict(hand_z_m=M["rows"][P + "Slam"][1]["hand_R"][2], note="кулак на 0,38 м над землёй: ниже — только через присед глубже "
                   "HipLimit 0,5 (смещение таза уже 0,42); если владелец захочет кулак в землю — поднять HipLimit Slam в виде"),
    accepted_unchanged="Pull/Punch/Recover/Bind пересобраны тем же конвейером — разница с принятыми FBX 0,00000° и 0 м по всем "
                       "костям и кадрам (artifacts/abordage/clips-v3/compare_fbx.py)")
p = os.path.join(anim, "timing.json")
f = open(p, "r+", encoding="utf-8", newline=""); f.seek(0); f.write(json.dumps(T, indent=1, ensure_ascii=False)); f.truncate(); f.close()
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
print("timing.json v3 written; build violations", T["limits_v3"]["violations_build"], "hip", T["limits_v3"]["hip_offset"])
