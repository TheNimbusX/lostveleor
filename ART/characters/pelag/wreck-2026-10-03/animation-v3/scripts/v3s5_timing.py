"""Крушение v3 (06.10, формы): дописать в timing.json Swing1/Swing2/Slam_Braced («Якорная броня»), Slam_Drag («Волнорез»),
правку стопы Wait1/Wait2 и таблицы пределов — чтение-правка-запись, чужие ключи не трогаются.
python v3s5_timing.py <animation-v3 dir>"""
import json, math, os, re, sys

V3 = sys.argv[1]; B = os.path.join(V3, "bake"); P = "Pelag_AN_Wreck2_"
R3 = lambda v: [round(x, 4) for x in v]
T = json.load(open(os.path.join(V3, "timing.json"), encoding="utf-8"))


def table(path):
    out = []
    if not os.path.exists(path): return out
    for line in open(path, encoding="utf-8", errors="replace").read().splitlines():
        m = re.match(r"\s+(ok|FAIL|info|n/a)\s+(\S+)\s+(.*?):\s(.*)$", line)
        if m: out.append(dict(status=m.group(1), id=m.group(2), check=m.group(3).strip(), value=m.group(4).strip()))
    return out


def first(path):
    return open(path, encoding="utf-8").read().splitlines() if os.path.exists(path) else []


def track(c, n):
    g = json.load(open(os.path.join(B, c + ".grip.json"), encoding="utf-8"))
    rows = json.load(open(os.path.join(V3, c + ".rows.json"), encoding="utf-8"))
    nm, sub = g["boneNames"], g["sub"]
    gt = [dict(frame=f, grip=R3(g["samples"][f * sub]["grip"]), support=R3(g["samples"][f * sub]["support"]),
               hand_gap=rows["rows"][f]["hand_gap"], on_grip=rows["rows"][f].get("on_grip", True)) for f in range(n + 1)]
    ank = [dict(frame=f, Left=R3(g["samples"][f * sub]["bones"][nm.index("LeftFoot")]),
                Right=R3(g["samples"][f * sub]["bones"][nm.index("RightFoot")])) for f in range(n + 1)]
    fl = [r["feet_low"] for r in rows["rows"]]
    planted = [[f for f in range(n + 1) if fl[f][s] <= 0.008] for s in ("Left", "Right")]
    return gt, ank, rows, planted


def anchor(c, w, start, tool, extra=None):
    bk = json.load(open(os.path.join(B, "%s_%s.anchorbake.json" % (c, w)), encoding="utf-8"))
    d = dict(bake="%s_%s.anchorbake.json" % (c, w), grip=c + ".grip.json", start=start, tool=tool, contact=bk.get("contacts"),
             liveFrom=bk.get("liveFrom"), validation=bk.get("validation"), checks=table(os.path.join(B, c + ".validation.txt")))
    if extra: d.update(extra)
    return d


LAYER = ("слой: БАЗОВЫЙ, всё тело (состояние вместо базы). Кисти = база (мировые матрицы), таз ниже — значит на нижнем слое "
         "(маска таз+ноги поверх верха базы) кисти уехали бы вниз на опускание таза (до 6,3 см) — так не ставить")
BRACE = dict(
    note="«Якорная броня» (06.10, бывш. Водяной панцирь): поза 10 листа B2 поверх ПРИНЯТОЙ базы — стойка шире и ниже, колени "
         "наружу 18°, подбородок вниз 4,5° (предел «голова за грудью» ±5°); кисти, хват и якорь — база. " + LAYER,
    pelvis="таз опущен до потолка Build (смещение таза 0,495 длины корпуса при пределе 0,5) и не ниже, чем дают руки до кистей базы: "
           "Swing1/Swing2 — таз всё время на 0,712 (база 0,720–0,775), Slam — в подъёме над голову как база (руки прямые), "
           "к удару и в удержании — 0,712, выход 17–24 — к базе",
    feet="правая шире: в упоре сзади 6 см назад и 16 см наружу (Swing1 0–8 = Swing2 0), у бедра после шага 16 см наружу "
         "(Swing1 12, Swing2 17–19), в упоре выпада Slam 5–17 — 2 см назад и 12 см наружу; сдвиг меняется только в воздухе. "
         "Пятка правой в упоре Swing1 1–4 вдвое ниже базы. Левая — на месте выпада базы (Swing1 0 — опора поворота корня вида): "
         "где у базы она висела (4–9 см), здесь стоит — пятка на земле, носок вверх ≤ 15–31° (прямая передняя нога). "
         "Над головой Slam (1–8) левая отрывается, как в базе (притоп к удару с пятки).",
    seams="Swing1_Braced 8 = Swing2_Braced 0, Swing2_Braced 15 = Slam_Braced 0 (копии снимков), Slam_Braced 24 = Slam 24 (стойка); "
          "12 / 19 — вход в Wait1/Wait2 (верх), как у базы; кадры контактов, окна и стыки — те же номера",
)
clips = T["clips"]
spec = [("Swing1_Braced", 12, "w7", "Pelag_AN_Wreck2_Swing1.start.anchorbake.json@0",
         "anchorbake --kind swing --contact 7 --chain 1.6 --start Swing1.start@0 --tempo", dict(contact_frame=7)),
        ("Swing2_Braced", 19, "w14", "Pelag_AN_Wreck2_Swing1_Braced_w7.anchorbake.json@8",
         "anchorbake --kind swing --contact 14 --chain 1.6 --start Swing1_Braced_w7@8 --tempo", dict(contact_frame=14, sim_contact_tick=22)),
        ("Slam_Braced", 24, "w13", "Pelag_AN_Wreck2_Swing2_Braced_w14.anchorbake.json@15",
         "anchorbake --kind slam --contact 13 --overhead 7 --impact 0,2.2 --chain 1.6 --start Swing2_Braced_w14@15 --tempo",
         dict(contact_frame=13, overhead_frame=7, sim_contact_tick=36))]
for name, n, w, start, tool, extra in spec:
    c = P + name; base = clips[c.replace("_Braced", "")]
    gt, ank, rows, planted = track(c, n)
    e = dict(frames=n + 1, **extra, form="WreckShell («Якорная броня»)", layer="base", segments=base.get("segments"),
             marks=dict(BRACE, base=c.replace("_Braced", "") + " (сегменты, контакт, стыки — её)"),
             pelvis_drop_m=rows["drop"], hips_z=[r["hips_z"] for r in rows["rows"]], hip_offset_body_lengths=max(r["hip"] for r in rows["rows"]),
             grip_vs_base=first(os.path.join(B, c + ".gripdiff.txt")), grip_track=gt, planted_frames=planted, feet=dict(ankles=ank[::3]),
             anchor=anchor(c, w, start, tool, dict(vs_base=first(os.path.join(B, c + ".bakediff.txt")))), seams=rows.get("seams"),
             notes={str(r["frame"]): r["note"] for r in rows["rows"] if r.get("note")})
    if name == "Slam_Braced": e["anchor"]["after_contact"] = "после касания земли (liveFrom 13,25) риг живой — расхождение с базой там — физика отскока, не клип"
    clips[c] = e

# ---------- Slam_Drag
c = P + "Slam_Drag"
gt, ank, rows, planted = track(c, 25)
drag = json.load(open(os.path.join(B, c + ".drag.json"), encoding="utf-8"))
clips[c] = dict(
    frames=26, contact_frame=13, overhead_frame=7, sim_contact_tick=36, form="WreckBreakwater («Волнорез»)", layer="base",
    sim="Sim: удержание 3 + выход 9 (WreckBreakwaterExitTicks, Locked 6) — героя держит по удар + 9 (кадр 22), ходьба с кадра 23, конец выхода 25",
    segments={"copy_of_slam": [0, 16], "contact": 13, "right_steps_forward": [16, 19], "pose11_drag": [17, 21], "pose11": [19, 20],
              "exit_to_stance": [21, 25], "sim_release_walk": 23},
    marks={"seam_in": "кадры 0–16 = Slam 0–16 (копия ключей .blend Slam; хват 0 мм) — вход из Swing2@15 как у Slam",
           "pose11": "шаг правой из упора вперёд под таз и наружу (смена опоры на месте: корень стоит, таз над серединой), корпус до 41°, "
                     "голова поднята (кивок вверх 4,5°), кисти низко впереди тянут цепь назад ~12 см и вверх; голова якоря тащится по "
                     "земле к герою и влево, цепь натянута",
           "exit": "с 21 корпус встаёт, правая шагает назад в стойку (20→23), кисти к рукояти стойки смесью поворотов (22–24)",
           "end": "кадр 25 — стойка навыка: корпус и кисти как Slam 24, правая — стойка сабли, левая на месте выпада (как Wait1/Stow 0 по ногам)",
           "view": "PelagWreckClipRules: DragFromFrame 16 (= удар + 3), DragLast 25 (= удар + 12) — совпадают"},
    grip_track=gt, planted_frames=planted, feet=dict(ankles=ank[::3]),
    anchor=anchor(c, "w13", "Pelag_AN_Wreck2_Swing2_w14.anchorbake.json@15",
                  "anchorbake --kind slam --contact 13 --overhead 7 --impact 0,2.2 --chain 1.6 --start Swing2_w14@15 --tempo",
                  dict(drag_check=first(os.path.join(B, c + ".drag.txt")), drag_16_22=dict(max_low_m=drag["max_low"], min_span_minus_L_m=drag["min_span_minus_L"],
                       head_moved_m=drag["moved"]),
                       note_16b="16b — замер времени AnchorSlackChain на этой машине (шум 0,046–0,061 мс; база Slam тем же прогоном 0,055)")),
    seams=rows.get("seams"))

# ---------- Wait1/Wait2: стопа
for w, src in (("Wait1", "Swing1@12"), ("Wait2", "Swing2@19")):
    c = P + w; rows = json.load(open(os.path.join(V3, c + ".rows.json"), encoding="utf-8"))
    ff = rows["foot_fix"]
    clips[c]["foot_fix_0610"] = dict(
        before="левая стопа висела %.1f см над землёй (нога %s прямая, до земли не достаёт)" % (100 * ff["hang_before_m"], src),
        fix="только кость LeftFoot: носок вниз на %.1f° вокруг лодыжки — опора на подушечку, пятка чуть поднята; бедро, голень, таз, "
            "корпус и руки — как были (хват 0 мм, живой якорь тот же); поза ног в петле одна — стопа не едет" % ff["toe_down_deg"],
        game_note="в игре Wait — верхний слой, ноги в паузе — базовый клип (%s): там левая висит как раньше" % src,
        footfix_check=first(os.path.join(B, c + ".footfix.txt")))
    lf = [r["feet_low"] for r in rows["rows"]]
    clips[c]["planted_frames"] = [[f for f in range(13) if lf[f]["Left"] <= 0.008], [f for f in range(13) if lf[f]["Right"] <= 0.008]]

# ---------- пределы
L = T["limits"]
for c in [P + x for x in ("Swing1_Braced", "Swing2_Braced", "Slam_Braced", "Slam_Drag", "Wait1", "Wait2")]:
    r = json.load(open(os.path.join(V3, c + ".rows.json"), encoding="utf-8"))
    L["summary"][c] = r["summary"][c]
    L["violations_by_clip"][c] = r["violations"]
L["violations_by_clip"][P + "Swing2_Braced_note"] = "«скольжение» правой 3–11 — верчение пируэта на подушечке, как у базы (стопа = стопа базы)"
T["forms_v3"] = dict(
    date="06.10", built=["Swing1_Braced", "Swing2_Braced", "Slam_Braced", "Slam_Drag"],
    shell="«Якорная броня»: вся серия — *_Braced на базовом слое; якорь — своя запечка (= база: хват 0 мм в кадрах, голова ≤ 2 мм в махах, "
          "≤ 1,3 см к удару оземь) или запечка базы",
    breakwater="«Волнорез»: Slam_Drag вместо Slam (0–16 одинаковы — переключение формы до удара + 3 без стыка)",
    ninth_wave="«Девятый вал» (заряд) этим проходом не собирался — Charge/ChargeRelease v2 по-прежнему",
    rules="предел таза 0,5 (Build), кадр = тик, без корня, привязка Pelag_AN_Wreck2Bind, кисти 0,10–0,25 м, голова за грудью ±5°")
json.dump(T, open(os.path.join(V3, "timing.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("timing.json: +", ", ".join(T["forms_v3"]["built"]), "+ Wait1/Wait2 foot_fix_0610")
