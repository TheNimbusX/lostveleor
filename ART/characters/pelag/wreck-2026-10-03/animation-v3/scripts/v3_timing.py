"""Крушение v3, Swing1: timing.json — кадры контакта и стыков, лодыжки (опора для поворота корня), трек хвата,
запечка якоря и её проверки, показ рига при первом нажатии. python v3_timing.py <animation-v3 dir>"""
import json, math, os, re, sys

V3 = sys.argv[1]
B = os.path.join(V3, "bake")
C = "Pelag_AN_Wreck2_Swing1"
K = 0.98881
rows_doc = json.load(open(os.path.join(V3, C + ".rows.json"), encoding="utf-8"))
rows = rows_doc["rows"]
grip = json.load(open(os.path.join(B, C + ".grip.json"), encoding="utf-8"))
bake = json.load(open(os.path.join(B, C + "_w7.anchorbake.json"), encoding="utf-8"))
seam = json.load(open(os.path.join(B, "draw_seam_ingame.json"), encoding="utf-8"))["samples"]
path = json.load(open(os.path.join(B, "grip_path.json"), encoding="utf-8"))
val = open(os.path.join(B, "validation.txt"), encoding="utf-8").read()
names = grip["boneNames"]; sub = grip["sub"]
R3 = lambda v: [round(x, 4) for x in v]


def bone(n, f): return grip["samples"][f * sub]["bones"][names.index(n)]


checks = []
for line in val.splitlines():
    m = re.match(r"\s+(ok|FAIL|info)\s+(\S+)\s+(.*?):\s(.*)$", line)
    if m: checks.append(dict(status=m.group(1), id=m.group(2), check=m.group(3).strip(), value=m.group(4).strip()))
ankles, planted = [], {"Left": [], "Right": []}
for f, r in enumerate(rows):
    e = dict(frame=f)
    for s in ("Left", "Right"):
        low = r.get("feet_low", {}).get(s, 1.0)
        a = bone(s + "Foot", f)
        kind = "air" if low > 0.012 else ("flat" if a[1] < 0.15 else "toe")
        if kind != "air": planted[s].append(f)
        e[s] = dict(unity=R3(a), contact=kind)
    ankles.append(e)
la = [bone("LeftFoot", f) for f in range(13)]
mean_l = [sum(a[i] for a in la) / 13 for i in range(3)]
contact = next(c for c in bake["contacts"] if c["kind"] == "swing")
s7 = min(bake["samples"], key=lambda s: abs(s["t"] - 7))
T = dict(
    skill="Крушение v3, Swing1 (мах 1 справа налево) — переделка после отказа 03.10; 06.10",
    fps=30, frame_is_tick="кадр = тик Sim; контакт — кадр 7 (удар Sim C+7)",
    root_motion="нет: корень стоит, таз по XY постоянен; поворот корня — вид (Wreck2Root, вокруг левой лодыжки)",
    rig="Pelag_v6_MixamoRig; привязка Pelag_AN_Wreck2Bind — покой v6 стоя (объект +90° X), масштаб .01",
    build='RazlomPelagAuthoredClips.Build("Pelag_AN_Wreck2_*", false, "Pelag_AN_Wreck2Bind", .5f)',
    hip_offset_body_lengths=grip["build"]["hipPeakBodyLengths"],
    hands="левый кулак на рукояти (гнездо хвата grip_socket.json — начало цепи), правый на цепи 0,15 м дальше по цепи; сабля за кушаком",
    head_not_animated="голова якоря в клипе не анимирована: путь запечён anchorbake от трека хвата этого FBX (bake/)",
    clips={C: dict(
        frames=13, contact_frame=7,
        segments={"draw": [0, 1], "pose1": [1, 2], "swing": [2, 7], "contact": 7, "follow": [8, 12], "pose3": 10},
        marks={"seam_next": "кадр 8 = Swing2 кадр 0 (Swing2 v3 — следующий клип; голова в кадре 8 уходит влево-вверх, 25 м/с)",
               "seam_wait": "кадр 12 = Wait1 кадр 0",
               "entry": "вход из стойки/бега — смешиванием вида (Wreck2EnterBlend 0,035 с): кадр 0 — уже снятие, не стойка "
                        "(разница со стойкой до %.0f° у %s)" % (rows_doc["seam_stance_f0"]["max_bone_deg"], rows_doc["seam_stance_f0"]["bone"]),
               "grip_handoff": "кадр 0 — кисти у правого бока; рукоять со спины в руку — правилом рига (скольжение 2 кадра)",
               "right_foot": "носок правой с кадра 1 (пятка вверх, поворот к 22°), короткий шаг вперёд 9→12",
               "sheathFrame": None},
        retime={"7": list(range(8))},
        planted_frames=[planted["Left"], planted["Right"]],
        planted_ankles=ankles,
        left_ankle_pivot=dict(unity_root_m=R3(mean_l), timing_rig_m=dict(left=round(-mean_l[0] * 1.8 / 1.82, 3), forward=round(mean_l[2] * 1.8 / 1.82, 3)),
                              note="опора поворота корня (PelagSquallClipRules.LeftAnkleLeft/Forward = 0,141/0,438 — точка Шквала); "
                                   "у Swing1 v3 стойка шире — без новой точки левая стопа уедет на развороте корня"),
        grip_track=[dict(frame=f, grip=R3(grip["samples"][f * sub]["grip"]), support=R3(grip["samples"][f * sub]["support"]),
                         hand_gap=rows[f]["hand_gap"]) for f in range(13)],
    )},
    anchor=dict(
        bake=C + "_w7.anchorbake.json", grip=C + ".grip.json", start=C + ".start.anchorbake.json",
        tool="artifacts/anchor-core/bake/anchorbake: --kind swing --contact 7 --chain 1.6 --start <start>@0 (AnchorRigCore.StepLive)",
        start_note="кадр 0 запечки: голова уже сорвана со спины — сзади на уровне плеча (%s м), %.1f м/с по кругу вокруг хвата. "
                   "Со спины в покое за 7 тиков кистень на цепи 1,6 м не разогнать (оптимизация хвата: ≤ 12–15 м/с); энергию "
                   "даёт снятие — в игре это шов рига OnBack → Baked." % (R3(path["start"]["p"]), math.dist([0, 0, 0], bake["samples"][0]["v"])),
        contact=dict(frame=7, point=contact["point"], radius_m=contact["radius"], speed_ms=round(math.dist([0, 0, 0], s7["v"]), 2),
                     taut=s7["taut"]),
        checks=checks,
        draw_seam_ingame=dict(
            note="первое нажатие: голова на спине → Drive(Baked) с AnchorBlend (≤ 400 м/с², срок контакт−1); кадры 0–4 показ = шов",
            offset_m={("f%.0f" % s["t"]): round(s["offset"], 3) for s in seam if abs(s["t"] - round(s["t"])) < 1e-6 and s["t"] <= 7},
            max_correction_accel=max(s["accel"] for s in seam)),
    ),
    limits=dict(summary=rows_doc["summary"], violations=rows_doc["violations"],
                rule="тело ≤ 35°/тик, руки ≤ 70° в 0→10; скрутка ≤ 45°; предплечье ≤ 70°; наклон назад — никогда; голова за грудью ±5°; "
                     "таз ≤ 0,5 корпуса; кисти 0,10–0,25 м; кисти/предплечья не в бёдрах, голенях, торсе; стопы не ниже −4 мм, опора не едет"),
)
p = os.path.join(V3, "timing.json")
open(p, "a").close()
fh = open(p, "r+", encoding="utf-8", newline=""); fh.seek(0); fh.write(json.dumps(T, indent=1, ensure_ascii=False)); fh.truncate(); fh.close()
print("timing written", p, "left ankle pivot", T["clips"][C]["left_ankle_pivot"]["timing_rig_m"])
