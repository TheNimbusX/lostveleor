"""Шквал v2: timing.json — тайминг, границы участков, перевремение полёта, позы листа, замеры пределов, требования к виду.
python sq_timing.py <anim_dir> [v6_check.json]   (читает <anim_dir>/_measure.json от sq_author.py)"""
import sys, os, json

anim = sys.argv[1]
M = json.load(open(os.path.join(anim, "_measure.json"), encoding="utf-8"))
V6 = json.load(open(sys.argv[2], encoding="utf-8")) if len(sys.argv) > 2 else None
P = "Pelag_AN_Squall2_"
ROWS = M["rows"]
CHK = M["check"]


def retime_table():
    out = {}
    for F in range(2, 7):
        if F == 2: out[str(F)] = [0, 3, 6]
        else: out[str(F)] = [0] + [round(1 + 5 * (k - 1) / (F - 1), 3) for k in range(1, F + 1)]
    return out


SLASH = lambda side: dict(
    frames=9, ticks=8,
    segments=dict(launch=[0, 1], flight=[0, 6], contact=6, support=[6, 8]),
    retime="только полёт [0..6] → F тиков (2–6) по flight_retime; опора [6..8] — 2 тика как есть",
    plant_foot="Left", push_foot="Right", slash=side,
    feet="кадр 0: левая на месте стойки, правая плашмя под тазом чуть сзади; 1–5: обе в воздухе ≥ 6,5 см по сетке; "
         "6: левая ставится на то же место, правая далеко сзади на носке (выпад); 7: правая в воздухе; 8: правая плашмя",
    frame_marks={"0": "замах", "1": "толчок", "3": "полёт", "5": "к земле", "6": "КОНТАКТ", "7": "проводка", "8": "замах"})
CLIPS = {
    P + "Load": dict(frames=3, ticks=2, segments=dict(anticipation=[0, 2]),
                     note="стойка серии сабли → присед-замах; правая шагом (кадр 1 в воздухе) встаёт под таз; кадр 2 = кадр 0 Forehand",
                     frame_marks={"0": "стойка", "1": "шаг", "2": "замах"}),
    P + "Forehand": dict(SLASH("прямой справа налево: клинок сзади-справа через правый бок к цели"),
                         note="кадр 0 = конец Backhand/Load (F0), кадр 8 = кадр 0 Backhand (B0)"),
    P + "Backhand": dict(SLASH("обратный слева направо: клинок сзади через левый бок к цели"),
                         note="кадр 0 = конец Forehand (B0), кадр 8 = кадр 0 Forehand (F0)"),
    P + "FinishFore": dict(frames=10, ticks=9, segments=dict(contact=0, follow_through=[0, 2], step=[2, 4], sheet6=5, exit=[5, 9]),
                           note="последний прыжок был Forehand: кадр 0 = кадр 6 Forehand (контакт); правая подшагивает в опорную точку стойки, "
                                "кадр 5 — стойка листа 6 (клинок горизонтально перед собой), кадр 9 = стойка серии сабли",
                           frame_marks={"0": "контакт", "2": "проводка", "4": "подшаг", "5": "лист 6", "9": "стойка"}),
    P + "FinishBack": dict(frames=10, ticks=9, segments=dict(contact=0, follow_through=[0, 2], step=[2, 4], sheet6=5, exit=[5, 9]),
                           note="последний прыжок был Backhand: кадр 0 = кадр 6 Backhand; дальше как FinishFore",
                           frame_marks={"0": "контакт", "2": "проводка", "4": "подшаг", "5": "лист 6", "9": "стойка"}),
    P + "ReturnFore": dict(frames=11, ticks=10, segments=dict(launch=[0, 1], flight=[0, 6], land=6, recovery=[6, 10]),
                           retime="полёт [0..6] → F тиков (2–6), как у ударов",
                           note="Неуловимый (= «Возврат»): после Forehand, кадр 0 = B0 (кадр 8 Forehand); лицом по пути — разворот корня делает вид; "
                                "посадка на обе стопы в опорные точки стойки (6), сабля слева переходит к правому боку дугой перед корпусом (6–10)",
                           frame_marks={"0": "B0", "1": "толчок", "3": "полёт", "6": "посадка", "10": "стойка"}),
    P + "ReturnBack": dict(frames=11, ticks=10, segments=dict(launch=[0, 1], flight=[0, 6], land=6, recovery=[6, 10]),
                           retime="полёт [0..6] → F тиков (2–6), как у ударов",
                           note="Неуловимый: после Backhand, кадр 0 = F0 (кадр 8 Backhand); посадка в опорные точки стойки (6), кадр 10 = стойка",
                           frame_marks={"0": "F0", "1": "толчок", "3": "полёт", "6": "посадка", "10": "стойка"}),
}


def limits():
    S = CHK["summary"]
    L = dict(source="sq_check.py при сборке; то же — по выгруженным FBX методом независимой проверки (artifacts/tools/squall-rework/clips-fix/indep)")
    L["violations"] = len(CHK["violations"])
    L["seam_max_bone_deg"] = dict(limit=5.0, value=max(s["max_bone_deg"] for s in M["seams"]), seams=M["seams"])
    L["per_tick"] = dict(rule="кость ≤ 35° за тик во всех раскладках полёта F = 2–6 и в опоре; рука с саблей (RightArm/ForeArm/Hand) "
                              "в тик удара (последний тик полёта и 6→7, у Finish 0→1 и 1→2) ≤ 70°",
                         worst={c[len(P):]: s["worst"] for c, s in S.items()})
    allr = [(c, r) for c, rs in ROWS.items() for r in rs]
    tw = max(allr, key=lambda cr: abs(cr[1]["twist"]))
    L["chest_to_pelvis_twist_deg"] = dict(limit=45.0, value=abs(tw[1]["twist"]), at=[tw[0], tw[1]["frame"]])
    L["forward_lean_deg"] = dict(rule="вперёд всегда", min_any=min(r["lean"] for c, r in allr),
                                 flight=[min(r["lean"] for c, r in allr if c in (P + "Forehand", P + "Backhand") and 1 <= r["frame"] <= 5),
                                         max(r["lean"] for c, r in allr if c in (P + "Forehand", P + "Backhand") and 1 <= r["frame"] <= 5)])
    fa = max(((c, r) for c, r in allr), key=lambda x: max(abs(v) for v in x[1]["forearm_twist"].values()))
    L["forearm_twist_from_rest_deg"] = dict(limit=70.0, value=max(abs(v) for v in fa[1]["forearm_twist"].values()), at=[fa[0], fa[1]["frame"]])
    L["head_follows_chest"] = dict(max_relP_over_stance=max(r["relP_over_stance"] for c, r in allr))
    L["feet"] = dict(rule="кадры 1–5 полёта (и дробные кадры раскладок): обе стопы по сетке ≥ 3 % роста (5,4 см)",
                     flight_min_m={c[len(P):]: round(min(s["frac_low"] or 1, 1), 4) for c, s in S.items() if s["frac_low"] is not None})
    L["planted_foot_slide_m"] = {c[len(P):]: s["slide"] for c, s in S.items()}
    L["contact_blade"] = {c[len(P):]: dict(yaw=ROWS[c][6]["blade_yaw"], dir=ROWS[c][6]["blade_dir"]) for c in (P + "Forehand", P + "Backhand") if c in ROWS}
    L["hips_drop_m"] = {c: round(max(r["hips_z"] for r in rs) - min(r["hips_z"] for r in rs), 3) for c, rs in ROWS.items()}
    if V6:
        L["unity_transfer"] = dict(max_hip_offset_ratio=V6["max_hip_offset_ratio"], dash_limit=0.5, unity_default=0.4)
    return L


T = dict(
    skill="Шквал v2 (Squall rework 02.10, правка по проверке)", fps=30, frame_is_tick=True,
    root_motion="нет: таз по XY постоянен, по Z присед/дуга прыжка; корень везёт Sim, поворот корня делает вид",
    rig="Pelag_v6_MixamoRig (пропорции v6, как Roll/Skewer/Dash), привязка Pelag_AN_Squall2Bind — покой v6 стоя, объект +90° по X",
    base_stance="стойка серии сабли = кадр 2 Pelag_MX_SaberCombo, перенесённый на v6 как в Unity, таз опущен на 4 см, стопы на земле "
                "(в ней же кончается рывок). С кадром 0 Pelag_AN_Sabre1, как его переносит Unity, не совпадает: левое колено 30°, "
                "таз 4 см (там левая стопа висит на 11 см)",
    lead_leg="ведущая нога всегда левая (как в стойке): левая стоит на месте стойки в замахе, в опоре и на контакте; правая толкает, "
             "уходит назад в выпад и подтягивается обратно в опоре",
    chain="Load → Forehand → Backhand → Forehand … → последний: полёт Forehand/Backhand до кадра 6, затем FinishFore/FinishBack с кадра 0; "
          "Неуловимый: последний удар целиком (до кадра 8), затем ReturnFore/ReturnBack",
    flight_retime=retime_table(),
    flight_retime_rule="тик полёта k (0..F) → кадр клипа; F = clamp(round(d / 0,66 м), 2, 6); тик 1 = толчок (кадр 1), дальше равномерно до контакта (кадр 6); "
                       "при F = 2 — кадры 0, 3, 6",
    view_turn="поворот корня к следующей цели — в опоре (кадры 7–8) + 1-й тик полёта, S-кривая; левая стопа в кадрах 6–8 стоит — "
              "крутить корень вокруг лодыжки левой стопы (её смещение от корня — rows[*][кадр].Left.ankle, x = влево, y = −вперёд)",
    view_requirements=["RazlomPelagV5AnimatorBuilder: клипы Pelag_AN_Squall2_* собирать с явным пределом таза 0,5, как рывок: "
                       "RazlomPelagAuthoredClips.Build(name, false, \"Pelag_AN_Squall2Bind\", .5f). По умолчанию 0,4, а клипы дают 0,48 — "
                       "без явного 0,5 сборка упадёт с «hip offset exceeds authored crouch envelope»",
                       "Pelag_AN_Squall2Bind.fbx положить рядом с клипами (Assets/Resources/Characters/Pelag_v5/Mixamo/)"],
    sheet_pose_map={"1": dict(clip=P + "Load", frame=2, what="замах-присед, кисть у правого бедра, клинок назад-вправо (F0)"),
                    "2": dict(clip=P + "Forehand", frame=1, what="толчок правой, левое колено вперёд, корпус 30°"),
                    "3": dict(clip=P + "Forehand", frame=3, what="низкий полёт, обе стопы над землёй"),
                    "4": dict(clip=P + "Forehand", frame=6, what="глубокий выпад, рука вытянута вперёд, клинок на цель"),
                    "5": dict(clip=P + "Backhand", frame=6, what="глубокий выпад, рука вперёд поперёк корпуса, клинок на цель"),
                    "6": dict(clip=P + "FinishFore", frame=5, what="широкая стойка, клинок горизонтально перед собой")},
    clips=CLIPS, limits=limits(), rows=ROWS)
p = os.path.join(anim, "timing.json")
open(p, "a").close()
f = open(p, "r+", encoding="utf-8", newline=""); f.seek(0); f.write(json.dumps(T, indent=1, ensure_ascii=False)); f.truncate(); f.close()
print("timing.json written; violations", T["limits"]["violations"], "hip", T["limits"].get("unity_transfer"))
