"""Крушение v3: дописать в timing.json Slam и Stow (+ пометки: контракт Swing2@7, Wait2) — чтение-правка-запись, чужие ключи не трогаются.
python v3s3_timing.py <animation-v3 dir>"""
import json, math, os, re, sys

V3 = sys.argv[1]; B = os.path.join(V3, "bake")
R3 = lambda v: [round(x, 4) for x in v]


def table(path):
    out = []
    if not os.path.exists(path): return out
    for line in open(path, encoding="utf-8").read().splitlines():
        m = re.match(r"\s+(ok|FAIL|info)\s+(\S+)\s+(.*?):\s(.*)$", line)
        if m: out.append(dict(status=m.group(1), id=m.group(2), check=m.group(3).strip(), value=m.group(4).strip()))
    return out


def track(c, n):
    g = json.load(open(os.path.join(B, c + ".grip.json"), encoding="utf-8"))
    rows = json.load(open(os.path.join(V3, c + ".rows.json"), encoding="utf-8"))
    nm, sub = g["boneNames"], g["sub"]
    gt = [dict(frame=f, grip=R3(g["samples"][f * sub]["grip"]), support=R3(g["samples"][f * sub]["support"]),
               hand_gap=rows["rows"][f]["hand_gap"], on_grip=rows["rows"][f].get("on_grip", True)) for f in range(n + 1)]
    ank = [dict(frame=f, Left=R3(g["samples"][f * sub]["bones"][nm.index("LeftFoot")]),
                Right=R3(g["samples"][f * sub]["bones"][nm.index("RightFoot")])) for f in range(n + 1)]
    return gt, ank, rows


C = "Pelag_AN_Wreck2_Slam"
gt, ank, rows = track(C, 27)
bk = json.load(open(os.path.join(B, C + "_w16.anchorbake.json"), encoding="utf-8"))["samples"]
at = lambda t: min(bk, key=lambda s: abs(s["t"] - t))
slam = dict(
    frames=28, contact_frame=16, overhead_frame=9, impact_point_root=[0.0, 2.2],
    spec_note="спека: 21 кадр, контакт 9 (тик 24). Честная физика от состояния Swing2@7 даёт удар не раньше кадра 15–16 "
              "(bake/Pelag_AN_Wreck2_Slam.feasibility.txt) — собран вариант с контактом 16: в Sim удар оземь = нажатие + 16 (+7 тиков к спеке), "
              "над головой = нажатие + 9; удержание 17–19, выход 20–27 (8 кадров, как в спеке). Решение по тикам — владельцу, вместе со Swing2.",
    segments={"rise_over_right_shoulder": [0, 9], "overhead": 9, "down": [9, 16], "contact": 16, "hold_bounce": [17, 19], "pull_exit": [20, 27]},
    marks={"seam_in": "кадр 0 = КОНТРАКТ Swing2@7 (Swing2 не собран): поза 4 + 1 тик (таз −40°, грудь −70°, правая впереди R_LAND, левая отведена "
                      "назад), голова — bake/Pelag_AN_Wreck2_Swing2.contract.json@7; Swing2 обязан кончаться этой позой и этим состоянием головы",
           "feet": "смена стойки на месте: правая назад 1–4, левая вперёд 6–11 (поза 5→6), в выходе правая к стойке 21–24, левая 25–27",
           "end": "кадр 27 = стойка серии сабли (ноги, таз, грудь); кисти держат цепь у правого бедра, якорь лежит справа ≈1,9 м от корня"},
    hands="левый кулак — гнездо хвата на пути gripopt_s3 (ось кулака по цепи; над головой 7–11 — по оси рукояти вверх-вправо, цепь гнётся у "
          "кулака), правый на цепи 0,12–0,19 м",
    grip_track=gt, feet=dict(ankles=ank[::3]),
    anchor=dict(bake=C + "_w16.anchorbake.json", grip=C + ".grip.json", start="Pelag_AN_Wreck2_Swing2.contract.json@7",
                tool="anchorbake --kind slam --contact 16 --overhead 9 --impact 0,2.2 --chain 1.6 --start <contract>@7",
                contact=dict(frame=16, point=R3(at(16)["p"]), speed_ms=round(math.dist([0, 0, 0], at(15.75)["v"]), 1)),
                checks=table(os.path.join(B, C + ".validation.txt"))),
    seams=rows.get("seams"),
)
C2 = "Pelag_AN_Wreck2_Stow"
gt2, ank2, rows2 = track(C2, 7)
live = json.load(open(os.path.join(B, C2 + ".live.json"), encoding="utf-8"))
stow = dict(
    frames=8, contact_frame=None, mask="всё тело", after="WindowExpired (окно 24 тика кончилось без нажатия)",
    segments={"toss": [0, 3], "release_right": [1, 3], "release_left": [4, 7]},
    marks={"seam_in": "кадр 0 = Wait1 кадр 0 (= Swing1 кадр 12, копия ключей из рабочего .blend Wait1); вход из любой фазы петли — блендом 2 кадра",
           "end": "кадр 7 = стойка серии сабли, кисти свободны",
           "rig": "якорь не запекается: живая физика + уборка рига (кисть у крепления ≤ 4 см → рукоять на спину, намотка 5 м/с, "
                  "поимка ≤ 10 см / ≤ 2,5 м/с)",
           "mount_unreachable": "крепление рукояти на спине — за шеей справа (0,48 м от левого плеча): левая кисть дотягивается туда только сквозь "
                                "голову и шею; клип доводит кисть до переда правого плеча (0,32 м от крепления), дальше запасной путь рига: "
                                "ждёт 0,35 с и тянет рукоять на спину 0,22 с"},
    hands="кадры 0–3 левая на рукояти (1–2 — смесь поз 0 и 3, без перескока IK), правая отпускает цепь 1–3; 4–7 обе — смесь позы 3 и стойки",
    grip_track=gt2, feet=dict(ankles=ank2),
    anchor_live=dict(start="bake/Pelag_AN_Wreck2_Wait1.windowend.json@24 (Wait1 живой маятник, тик 24 окна)", file=C2 + ".live.json",
                     hand_frame=live["handT"], catch_frame=live["catchT"], caught=live["caught"],
                     checks=open(os.path.join(B, C2 + ".stow.txt"), encoding="utf-8").read().splitlines()[1:6]),
    seams=rows2.get("seams"),
)
p = os.path.join(V3, "timing.json")
fh = open(p, "r+", encoding="utf-8", newline="")
T = json.loads(fh.read())
T["clips"][C] = slam; T["clips"][C2] = stow
for c, r in ((C, rows), (C2, rows2)):
    T["limits"]["summary"][c] = r["summary"].get(c); T["limits"]["violations_by_clip"][c] = r["violations"]
T["swing2_contract"] = dict(file="bake/Pelag_AN_Wreck2_Swing2.contract.json",
                            note="состояние головы Swing2@7 = зеркало Swing1@8 (25,6 м/с вправо, впереди); поза тела — Slam кадр 0 (v3sl_keys KEYS[0])")
T["wait2_blocked"] = "Wait2 начинается с конца Swing2 (кадр 11) — Swing2 не собран, петлю не от чего строить"
fh.seek(0); fh.write(json.dumps(T, indent=1, ensure_ascii=False)); fh.truncate(); fh.close()
print("timing written", list(T["clips"]))
