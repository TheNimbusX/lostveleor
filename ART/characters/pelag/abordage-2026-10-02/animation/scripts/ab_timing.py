"""Абордаж v2: timing.json — тайминг, участки и таблицы растяжки, контакт, опорные лодыжки, позы листа, пределы.
python ab_timing.py <anim_dir> <v6_check.json> <indep measure.json>   (читает <anim_dir>/_measure.json от ab_author.py)"""
import sys, os, json, math

anim = sys.argv[1]
M = json.load(open(os.path.join(anim, "_measure.json"), encoding="utf-8"))
V6 = json.load(open(sys.argv[2], encoding="utf-8"))
IND = json.load(open(sys.argv[3], encoding="utf-8"))
P = "Pelag_AN_Abordage2_"
ROWS, CHK = M["rows"], M["check"]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
THROW_END, PULL_END = 8, 12


def throw_retime(A):
    return [0.0, 1.0, 2.0] + [round(2.0 + 6.0 * k / A, 4) for k in range(1, A + 1)]


def pull_retime(P_):
    if P_ == 2: return [0.0, 11.0, 12.0]
    if P_ == 3: return [0.0, 1.0, 11.0, 12.0]
    if P_ == 4: return [0.0, 1.0, 10.0, 11.0, 12.0]
    return [0.0, 1.0] + [round(1.0 + 8.0 * j / (P_ - 4), 4) for j in range(1, P_ - 3)] + [10.0, 11.0, 12.0]


def ankles(clip):
    """Лодыжки по кадрам в осях корня: f — вперёд, l — влево, u — вверх (м рига v6, тело 1,80);
    contact: flat — стопа плашмя на земле (лодыжка ≤ 0,17, как PlantedAnkleHeight Шквала), toe — на носке, air — в воздухе."""
    out = []
    for r in ROWS[clip]:
        row = dict(frame=r["frame"])
        for s in ("Left", "Right"):
            x, y, z = r[s]["ankle"]
            row[s] = dict(f=round(-y, 3), l=round(x, 3), u=round(z, 3),
                          contact=("flat" if z < 0.17 else "toe") if r[s]["toe_z"] < 0.012 else "air")
        out.append(row)
    return out


CLIPS = {
    P + "Throw": dict(frames=9, ticks="2 замаха + A (1–6) полёта якоря", authored_at="A = 6 (кадр = тик)",
                      segments=dict(windup=[0, 1], release=[1, 2], anchor_flight=[2, 8], bite=8),
                      retime="замах 0→1→2 — всегда 2 тика (тик каста C = кадр 0, C+1 = 1, выпуск C+2 = 2); полёт якоря [2..8] → A тиков "
                             "равномерно: тик C+2+k = кадр 2 + 6k/A; тик зацепа B = C+2+A = кадр 8 (натяг) = кадр 0 Pull",
                      strike_ticks="правая рука (RightArm/ForeArm/Hand) ≤ 70°: выпуск C+1→C+2 и первый тик полёта якоря; замах C→C+1 — "
                                   "обычный тик (≤ 35°, как Load Шквала); путь кости за тик — по всем кадрам, которые тик проходит",
                      feet="левая стоит плашмя на месте стойки весь клип и не едет; правая в замахе (кадр 1) оторвана на 7 см — вид "
                           "доворачивает корень вокруг левой лодыжки, правая в воздухе; на выпуске (2) встаёт на 10 см назад на носок и стоит",
                      hands="замах 1 тик: корпус закручен назад (грудь −62° против −30° стойки), правая подбирает якорь к правому бедру-плечу "
                            "(лист 1 в пределах 35°/тик); выпуск 2 — рука выброшена вбок-вперёд, ладонь раскрыта (С-хват −32°); 3 — проводка; "
                            "3→8 рука возвращается к груди, пальцы снова в хват; левая сжимает рукоять цепи в кулак за тики 0→3 и тянется по цепи",
                      fingers="хват gL: 0 → 0,34 → 0,68 → 1 (кадры 0–3, ≤ 33°/тик), gR: 0, 0, −1 (выпуск), −0,85 → 0 к натягу",
                      frame_marks={"0": "стойка", "1": "замах", "2": "ВЫПУСК", "3": "проводка", "8": "натяг"}),
    P + "Pull": dict(frames=13, ticks="P = clamp(round(L / 0,66), 2, 12)", authored_at="P = 12 (кадр = тик)",
                     segments=dict(bite=0, snap=[0, 1], flight=[1, 10], fist_cock=10, punch_starts=11, contact=12),
                     retime="тик тяги k (0..P) → кадр по pull_retime: срыв [0→1] — 1 тик; полёт [1→9] — P − 4 тиков равномерно; "
                            "[9→10→11→12] — последние 3 тика; P = 4 — 0, 1, 10, 11, 12; P = 3 — 0, 1, 11, 12; P = 2 — 0, 11, 12. "
                            "Контакт — всегда кадр 12 в тик прибытия B+P",
                     strike_ticks="два последних тика тяги: правая рука ≤ 70°, остальное ≤ 35°. Короткие тяги проходят кадры 0→11 (P = 2), "
                                  "1→11 (P = 3), 1→10 (P = 4), 1→9 (P = 5) за ОДИН тик, поэтому каждая кость тела идёт от 0 до 11 "
                                  "в одну сторону и не дальше 35° (путь, не только концы): позы полёта не мелькают",
                     feet="кадр 0 — обе на земле (натяг, корень ещё стоит); 1–11 таз поднят на 10–12 см, обе стопы над землёй 8–15 см, "
                          "правая волочится сзади, левое колено впереди; 12 — левая плашмя на месте стойки, правая далеко сзади на носке",
                     hands="левая с кулаком на цепи вытянута к цели и за полёт выбирает цепь к поясу (к 12 — у левого бедра); "
                           "правый кулак взводится с 1 до 10 (локоть вниз-назад, кулак у правых рёбер), 11 — кулак пошёл, 12 — прямой правой",
                     fingers="gL = 1 (кулак на цепи) весь клип; gR: 0 → 0,5 → 0,95 → 1 — кулак собирается за срыв и полёт",
                     frame_marks={"0": "натяг", "1": "срыв (лист 3)", "10": "взвод (лист 4)", "11": "кулак пошёл", "12": "КОНТАКТ"}),
    P + "Punch": dict(frames=4, ticks="тик прибытия + 3 удержания", authored_at="1:1",
                      segments=dict(contact=0, hold=[0, 3]), contact_frame=0,
                      note="кадр 0 = кадр 12 Pull = тик прибытия B+P (событие AbordagePunch); левый кулак с рукоятью у левого бедра "
                           "(лист 5), грудь довёрнута в удар; 1–3 — удержание, кулак остаётся. Левая стоит, правая на носке — обе не едут",
                      frame_marks={"0": "КОНТАКТ", "1": "удержание", "3": "удержание"}),
    P + "Recover": dict(frames=7, ticks="6 тиков выхода", authored_at="1:1",
                        segments=dict(step=[0, 3], sheet6=3, to_stance=[3, 6]),
                        walk="первые 2 тика выхода (кадры 0→2) ходьба не идёт (ExitLocked 2), с 3-го тика ходьба срывает выход",
                        note="кадр 0 = кадр 3 Punch; 1 — правая сначала отрывает носок вверх (носок не волочится), 2 — шаг, 3 — стойка листа 6 "
                             "(правый кулак в защите); 3→6 кулаки разжимаются в хват стойки; кадр 6 = конец Pelag_AN_Dash и "
                             "Pelag_AN_Squall2_Finish* (0°)",
                        frame_marks={"0": "удержание", "2": "шаг", "3": "лист 6", "6": "стойка"}),
}


def limits():
    S = CHK["summary"]
    L = dict(source="ab_check.py при сборке (снимки позы) + независимая проверка выгруженных FBX методом проверяющего Шквала "
                    "(artifacts/abordage/clips-check/measure.py — копия clips-adv/measure.py, summary.py)")
    L["violations_build"] = len(CHK["violations"])
    L["seam_max_bone_deg"] = dict(limit=0.0, value=max(s["max_bone_deg"] for s in M["seams"]), seams=M["seams"],
                                  independent=[dict(frm=s["frm"], to=s["to"], top=s["top"][0], hips=s["hips"]) for s in IND["seams"]])
    L["per_tick"] = dict(rule="ПУТЬ кости за тик (сумма поворотов по всем целым кадрам, которые тик проходит, а не только концы) ≤ 35° "
                              "во всех раскладках A = 1–6 и P = 2–12; правая рука (RightArm/ForeArm/Hand) ≤ 70° в тики удара: бросок — "
                              "выпуск 1→2 и первый тик полёта якоря (замах 0→1 — обычный тик), кулак — два последних тика тяги; "
                              "пальцы — кости тела (≤ 35°)",
                         worst_build={c[len(P):]: s["worst"] for c, s in S.items()})
    L["between_frames"] = dict(rule="сэмплы через ¼ кадра (вид на 60+ к/с): стопы не ниже −3 мм, точки на земле не едут, пока корень стоит",
                               build={c[len(P):]: s["sub"] for c, s in S.items()})
    L["fingers"] = ("левая: кулак на рукояти цепи с выпуска (кадры 0→3 Throw, ≤ 33°/тик) до Recover 3, разжим 3→6; правая: С-хват стойки → "
                    "раскрытая ладонь на выпуске (−32°) → хват к натягу → кулак за тягу (+33°) → кулак до Recover 3 → С-хват к 6")
    allr = [(c, r) for c, rs in ROWS.items() for r in rs]
    tw = max(allr, key=lambda cr: abs(cr[1]["twist"]))
    L["chest_to_pelvis_twist_deg"] = dict(limit=45.0, value=abs(tw[1]["twist"]), at=[tw[0], tw[1]["frame"]])
    L["forward_lean_deg"] = dict(rule="назад — только замах Throw 1 (отклон от цели, как лист 1, не глубже 8°); в тяге 25–40°; на ударе ≥ 10°, назад никогда", windup_throw1=ROWS[P + "Throw"][1]["lean"], min_any=min(r["lean"] for c, r in allr),
                                 pull_flight_1_9=[r["lean"] for r in ROWS[P + "Pull"][1:10]], contact=ROWS[P + "Punch"][0]["lean"])
    fa = max(allr, key=lambda x: max(abs(v) for v in x[1]["forearm_twist"].values()))
    L["forearm_twist_from_rest_deg"] = dict(limit=70.0, value=max(abs(v) for v in fa[1]["forearm_twist"].values()), at=[fa[0], fa[1]["frame"]])
    L["head_follows_chest"] = dict(max_relP_over_stance=max(r["relP_over_stance"] for c, r in allr),
                                   face_yaw_at_contact=ROWS[P + "Punch"][0]["face_yaw"])
    L["feet"] = dict(rule="Pull кадры 1–11 и дробные кадры раскладок: обе стопы по сетке ≥ 3 % роста (5,4 см); под землю — нигде",
                     pull_flight_min_m=round(min(S[P + "Pull"]["frac_low"], min(r["feet"][s]["mesh_low"] for r in IND["clips"][P + "Pull"]["rows"]
                                                                                if 1 <= r["f"] <= 11 for s in ("Left", "Right"))), 4),
                     lowest_mesh_anywhere_m=min(r["feet"][s]["mesh_low"] for c, v in IND["clips"].items() if P in c for r in v["rows"] for s in ("Left", "Right")))
    L["planted_foot_slide_m"] = {c[len(P):]: s["slide"] for c, s in S.items()}
    L["arms_in_thighs"] = "нет ни в одном кадре (кисть/предплечье обеих рук против обоих бёдер, голеней, торса и головы, сетка Pelag_v6)"
    L["unity_transfer"] = dict(max_hip_offset_ratio=V6["max_hip_offset_ratio"], limit=0.5, unity_default=0.4,
                               note="присед ограничен −7,5 см (ab_pose.DZ_MIN): сборка проходит и с порогом по умолчанию",
                               independent=max(r["hipoff"] for c, v in IND["clips"].items() if P in c for r in v["rows"]))
    return L


_all = [r for rs in ROWS.values() for r in rs]
PY = (min(r["pelvis_yaw"] for r in _all), max(r["pelvis_yaw"] for r in _all))
CY = (min(r["chest_yaw"] for r in _all), max(r["chest_yaw"] for r in _all))
T = dict(
    skill="Абордаж v2 (Pelag_AN_Abordage2_*, переделка 02.10: якорь во врага, тяга 20 м/с, кулак правой; правка 02.10 по независимой проверке: пальцы, замах ≤ 35°, путь кости за тик, взвод, левый кулак у бедра)", fps=30,
    frame_is_tick="при самой длинной раскладке (A = 6, P = 12); короче — растяжка по таблицам ниже",
    root_motion="нет: таз по XY постоянен (только присед по Z); корень везёт Sim (тяга 0,66 м/тик), поворот корня к цели делает вид",
    rig="Pelag_v6_MixamoRig, привязка Pelag_AN_Abordage2Bind — покой v6 стоя, объект +90° по X, масштаб .01 (как DashBind/Squall2Bind; кости — как Pelag_AN_Dash)",
    base_stance="стойка серии сабли (кадр 2 Pelag_MX_SaberCombo на v6, как у рывка и Шквала): Throw кадр 0 и Recover кадр 6 — она же",
    hands="сабля за кушаком у левого бедра (вид: PelagEquipmentView.BeginAnchorUse), рукоять цепи в ЛЕВОМ кулаке, якорь бросает и бьёт ПРАВАЯ",
    sim_timeline="C — тик каста; выпуск якоря C+2; зацеп B = C+2+A, A = clamp(ceil((d − 0,5 − r_цели) / 1,5), 1, 6); тяга B+1..B+P; "
                 "удар B+P; удержание 3; выход 6 (спека artifacts/abordage/plan/SPEC.md §2.2)",
    chain="Throw (0..8 по A) → Pull (0..12 по P) → Punch (0..3) → Recover (0..6) → стойка покоя; стыки 0°",
    throw_retime={str(A): throw_retime(A) for A in range(1, 7)},
    pull_retime={str(p): pull_retime(p) for p in range(2, 13)},
    contact=dict(clip=P + "Punch", frame=0, same_as=P + "Pull кадр 12", tick="B+P (тик прибытия)"),
    planted_ankles={c[len(P):]: ankles(c) for c in ROWS},
    exit_to_idle="Recover кончается ровно в стойке рывка и Шквала (0° по всем костям, таз 0). Левая лодыжка там 0,476 м вперёд и 0,163 влево от корня, "
                 "в стойке покоя ~0,21 вперёд и на 8 см шире — смешивание в CombatIdle повезёт её ~24 см, как у Шквала. В клипе это не убрать, "
                 "не меняя конечную стойку; вид должен звать тот же шаг выхода, что у Шквала (CharacterAnimatorView.Squall BeginSquallExitStep → "
                 "PelagFootPlantView.StepSquallExit, PlantedAnkleHeight .17 — лодыжка в кадре 6 на 0,136). Вход в Throw — из той же стойки, "
                 "входной сдвиг корня — как EntryShift Шквала, если каст из покоя",
    view_requirements=["RazlomPelagV5AnimatorBuilder: Build(\"Pelag_AN_Abordage2_*\", false, \"Pelag_AN_Abordage2Bind\") — смещение таза "
                       "< 0,40 (limits.unity_transfer), проходит и с порогом по умолчанию 0,4; явный .5f, как у Шквала, — только запас",
                       "замах (тики C→C+2): корень доворачивать к цели вокруг ЛЕВОЙ лодыжки (как замах Шквала): левая стопа стоит, "
                       "правая в кадре 1 в воздухе и встаёт на носок только на выпуске — проскальзывания нет",
                       "FBX и привязку положить в Assets/Resources/Characters/Pelag_v5/Mixamo/ рядом с Pelag_AN_Dash",
                       "время вида Tick − 2 + Alpha; Throw по throw_retime(A), Pull по pull_retime(P), P уточняется в тик зацепа",
                       "взгляд/корень: вид доворачивает корень к цели за 2 тика замаха (см. выше); собственная крутка клипа — только тело относительно корня "
                       "(таз %.0f…%.0f°, грудь %.0f…%.0f° от направления корня); на контакте лицо %.0f° от линии удара "
                       "(голова следует за грудью, поворот ±30°)" % (PY[0], PY[1], CY[0], CY[1], ROWS[P + "Punch"][0]["face_yaw"])],
    sheet_pose_map={"1": dict(clip=P + "Throw", frame=1, what="замах (≤ 35° за тик): корпус закручен назад, правая с якорем отведена назад, правая стопа оторвана"),
                    "2": dict(clip=P + "Throw", frame=2, what="выпуск: правая выброшена вбок-вперёд, ладонь раскрыта, вес на левой, правая встала назад на носок"),
                    "3": dict(clip=P + "Pull", frame=1, what="срыв: таз поднят, наклон 34°, левый кулак на цепи впереди, правый кулак у груди, ноги в воздухе"),
                    "4": dict(clip=P + "Pull", frame=10, what="взвод: правый кулак у рёбер, локоть вниз-назад, левая выбирает цепь"),
                    "5": dict(clip=P + "Punch", frame=0, what="контакт: левая плашмя, прямой правой, левый кулак с цепью у бедра, правая нога сзади на носке"),
                    "6": dict(clip=P + "Recover", frame=3, what="стойка листа 6: кулаки в защите, правая встала на место стойки")},
    forms=dict(rule="формы пока на базовых клипах (лист B2 нет); Пробоина = базовый Punch по замыслу",
               b2_would_add=["поза 7 Гейзер → Pelag_AN_Abordage2_Uppercut (4 кадра: апперкот из приседа, правый кулак над головой, на носках) "
                             "+ Pelag_AN_Abordage2_Pull_Geyser (кадры 0–9 копия Pull, свои 10–12: низкий присед под апперкот)",
                             "позы 9–10 Обвал → Pelag_AN_Abordage2_Pull_Quake (кадры 0–9 копия Pull, свои 10–12: короткий подскок, правый кулак над головой) "
                             "+ Pelag_AN_Abordage2_Slam (4 кадра: приземление в глубокий присед ≤ 15 % роста, кулак в землю, наклон 35–45°)",
                             "поза 8 Пробоина → только при желании владельца: Pelag_AN_Abordage2_Breach (глубже выпад, корпус вложен в удар); "
                             "по замыслу Пробоина = базовый Punch, клип не обязателен",
                             "Recover для Uppercut и Slam — свои кадры 0–2 (из другой конечной позы), 3–6 общие; стыки 0°"]),
    clips=CLIPS, limits=limits(), rows=ROWS)
p = os.path.join(anim, "timing.json")
open(p, "a").close()
f = open(p, "r+", encoding="utf-8", newline=""); f.seek(0); f.write(json.dumps(T, indent=1, ensure_ascii=False)); f.truncate(); f.close()
print("timing.json written; build violations", T["limits"]["violations_build"], "hip", T["limits"]["unity_transfer"]["max_hip_offset_ratio"])
