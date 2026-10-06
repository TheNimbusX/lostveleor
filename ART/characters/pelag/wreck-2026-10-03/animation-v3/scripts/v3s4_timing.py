"""Крушение v3 (06.10, Swing2 = продолжение вращения): дописать в timing.json Swing2, Wait2, новый Slam (от Swing2@15), Stow на новое
крепление, тики серии в Sim и предложение крепления — чтение-правка-запись, чужие ключи не трогаются.
python v3s4_timing.py <animation-v3 dir>"""
import json, math, os, re, sys

V3 = sys.argv[1]; B = os.path.join(V3, "bake")
R3 = lambda v: [round(x, 4) for x in v]
C2, N2, C3, O3, N3 = 14, 19, 13, 7, 24          # Swing2: контакт, последний кадр; Slam: контакт, над головой, последний кадр
HAND, STOW_D = 6, 6
N4 = HAND + 19 + STOW_D


def table(path, skip=0):
    out = []
    if not os.path.exists(path): return out
    for line in open(path, encoding="utf-8", errors="replace").read().splitlines()[skip:]:
        m = re.match(r"\s+(ok|FAIL|info|n/a)\s+(\S+)\s+(.*?):\s(.*)$", line)
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


def bake_at(name):
    bk = json.load(open(os.path.join(B, name + ".anchorbake.json"), encoding="utf-8"))["samples"]
    return lambda t: min(bk, key=lambda s: abs(s["t"] - t))


def lines(path, a, b):
    return [l.rstrip() for l in open(path, encoding="utf-8").read().splitlines()[a:b]]


# ---------- Swing2
C = "Pelag_AN_Wreck2_Swing2"
gt, ank, rows = track(C, N2)
at = bake_at(C + "_w14")
path = json.load(open(os.path.join(B, C + ".path.json"), encoding="utf-8"))
piv = os.path.join(B, C + ".pivot.json")
swing2 = dict(
    frames=N2 + 1, contact_frame=C2, sim_contact_tick=8 + C2,
    segments={"unwind_from_swing1": [0, 3], "pirouette_right_ball": [3, C2], "left_leg_crosses": [path.get("cross0"), path.get("cross1")],
              "contact": C2, "right_steps_to_hip": [C2, C2 + 3], "follow": [C2 + 1, N2]},
    marks={"seam_in": "кадр 0 = Swing1 кадр 8 (тик 8, копия ключей .blend Swing1)",
           "seam_next": "кадр 15 = Slam кадр 0 (контакт + 1 тик) при нажатии в окне",
           "pause": "кадр 19 = Wait2 кадр 0 (нажатия нет)",
           "motion": "продолжение вращения: голова идёт справа налево, таз и грудь поворачиваются за ней (рысканье таза psi), мах плоский"},
    pelvis_yaw_deg=[round(x, 1) for x in path["psi"]], chest_twist_deg=[round(x, 1) for x in path["twist"]],
    hands="левый кулак — гнездо хвата на пути gripopt_s4 (дуга между ключами), правый на цепи 0,15 м",
    grip_track=gt, feet=dict(ankles=ank, note="правая — пируэт на подушечке 3–14 (центр пятна ≤ 1 мм/тик, верчение 26–44°/тик), "
                                             "левая в воздухе, встаёт в контакте; правая к бедру 14–17"),
    anchor=dict(bake=C + "_w14.anchorbake.json", grip=C + ".grip.json", start="Pelag_AN_Wreck2_Swing1_w7.anchorbake.json@8",
                tool="anchorbake --kind swing --contact 14 --chain 1.6 --start Swing1_w7@8 --tempo",
                contact=dict(frame=C2, point=R3(at(C2)["p"]), speed_ms=round(math.dist([0, 0, 0], at(C2 - .25)["v"]), 1)),
                checks=table(os.path.join(B, C + ".validation.txt"))),
    search=open(os.path.join(B, C + ".feasibility.txt"), encoding="utf-8").read().splitlines(),
    seams=rows.get("seams"),
)

# ---------- Wait2
C = "Pelag_AN_Wreck2_Wait2"
gtw, ankw, rowsw = track(C, 12)
wait2 = dict(
    frames=13, loop=True, loop_frames=12, contact_frame=None,
    mask="только верх тела: таз, грудь, голова, руки; ноги стоят как в Swing2 кадр 19",
    marks={"seam_in": "кадр 0 = Swing2 кадр 19 (копия ключей .blend Swing2)", "loop": "кадр 12 = кадр 0", "next": "нажатие в окне → Slam; окно кончилось → Stow"},
    grip_track=gtw, feet=dict(ankles=ankw[::3]),
    anchor_live=dict(start="Pelag_AN_Wreck2_Swing2_w14.anchorbake.json@19", file=C + ".live.json", window_end=C + ".windowend.json",
                     note="голова не запекается: AnchorRigCore.StepLive от состояния Swing2@19, две петли (24 тика окна)",
                     checks=table(os.path.join(B, C + ".livecheck.txt")),
                     check_note="anchorbake по живому треку: 5/6 (точка и скорость контакта) — n/a, контакта в паузе нет"),
    seams=rowsw.get("seams"),
)

# ---------- Slam
C = "Pelag_AN_Wreck2_Slam"
gts, anks, rowss = track(C, N3)
at = bake_at(C + "_w13")
slam = dict(
    frames=N3 + 1, contact_frame=C3, overhead_frame=O3, impact_point_root=[0.0, 2.2], sim_contact_tick=8 + C2 + 1 + C3,
    spec_note="Slam от настоящего конца Swing2 (Swing2@15). Самый ранний контакт, проходящий все проверки, — кадр 13 (тик 36, через 14 тиков "
              "после контакта маха 2; 11 и 12 физически нет — bake/Pelag_AN_Wreck2_Slam.feasibility.txt). Над головой кадр 7 (тик 30).",
    segments={"rise_over_left_shoulder": [0, O3], "overhead": O3, "down": [O3, C3], "contact": C3, "hold": [C3 + 1, C3 + 3], "exit": [C3 + 4, N3]},
    marks={"seam_in": "кадр 0 = Swing2 кадр 15 (копия ключей .blend Swing2), голова — запечка Swing2_w14@15",
           "rise": "подъём к позе 5: таз +5 см, ключицы вверх до 50°, грудь чуть вправо — кулаки над макушкой (кисти достают хват)",
           "feet": "правая назад 0–4 сразу на подушечку (14°), к удару — на носок 22°; левая стоит; в выходе правая к стойке, левая подтягивается",
           "end": "кадр 24 = стойка серии сабли"},
    hands="левый кулак — гнездо хвата на пути gripopt_s4 --slam (на треке выгрузки, + сдвиги между кадрами); над головой 6–8 — по оси "
          "рукояти; правый на цепи 0,12–0,15 м (в подъёме 3–4 — 0,12, мимо лица), в рывке вниз правый локоть вниз-вперёд",
    grip_track=gts, feet=dict(ankles=anks[::3]),
    anchor=dict(bake=C + "_w13.anchorbake.json", grip=C + ".grip.json", start="Pelag_AN_Wreck2_Swing2_w14.anchorbake.json@15",
                tool="anchorbake --kind slam --contact 13 --overhead 7 --impact 0,2.2 --chain 1.6 --start Swing2_w14@15 --tempo",
                contact=dict(frame=C3, point=R3(at(C3)["p"]), speed_ms=round(math.dist([0, 0, 0], at(C3 - .25)["v"]), 1)),
                checks=table(os.path.join(B, C + ".validation.txt"))),
    search=open(os.path.join(B, C + ".feasibility.txt"), encoding="utf-8").read().splitlines(),
    seams=rowss.get("seams"),
)

# ---------- Stow
C = "Pelag_AN_Wreck2_Stow"
gtt, ankt, rowst = track(C, N4)
l1 = json.load(open(os.path.join(B, C + ".live.json"), encoding="utf-8"))
l2 = json.load(open(os.path.join(B, C + "_from_Wait2.live.json"), encoding="utf-8"))
stow = dict(
    frames=N4 + 1, contact_frame=None, mask="всё тело", after="WindowExpired (окно 24 тика кончилось без нажатия), из Wait1 или Wait2",
    segments={"right_releases_chain": [1, 5], "left_handle_over_top_to_mount": [1, HAND], "handle_on_mount": HAND,
              "left_releases": [HAND + 1, HAND + 19], "body_waits_for_catch": [HAND, HAND + 12 + STOW_D], "to_sabre_stance": [HAND + 12 + STOW_D, N4]},
    marks={"seam_in": "кадр 0 = Wait1 кадр 0 (= Swing1 кадр 12); вход из Wait2 и любой фазы петли — блендом 2 кадра",
           "hand_path": "левая с рукоятью: перед левой грудью → над левым плечом → за затылком → за правую лопатку (кадр 6), как «проба Эпли»",
           "turn": "корпус доворачивает влево ~85°, крепление смотрит на якорь — намотка идёт к спине; возврат в стойку после поимки (кадр 24+)",
           "end": "кадр 31 = стойка серии сабли, кисти свободны"},
    grip_track=gtt, feet=dict(ankles=ankt[::3]),
    anchor_live={
        "from_Wait1": dict(start="bake/Pelag_AN_Wreck2_Wait1.windowend.json@24", file=C + ".live.json", hand_frame=l1["handT"],
                           catch_frame=l1["catchT"], caught=l1["caught"], checks=lines(os.path.join(B, C + ".stow.txt"), 1, 8)),
        "from_Wait2": dict(start="bake/Pelag_AN_Wreck2_Wait2.windowend.json@24", file=C + "_from_Wait2.live.json", hand_frame=l2["handT"],
                           catch_frame=l2["catchT"], caught=l2["caught"], checks=lines(os.path.join(B, C + "_from_Wait2.stow.txt"), 1, 8))},
    seams=rowst.get("seams"),
)

p = os.path.join(V3, "timing.json")
fh = open(p, "r+", encoding="utf-8", newline="")
T = json.loads(fh.read())
T["skill"] = "Крушение v3: Swing1 → Swing2 (продолжение вращения) → Slam; паузы Wait1/Wait2; Stow на новое крепление — 06.10"
T["frame_is_tick"] = "кадр = тик Sim; этап стартует в кадре контакт + 1 предыдущего (при самых быстрых нажатиях)"
clips = T["clips"]
for k in ("Pelag_AN_Wreck2_Slam", "Pelag_AN_Wreck2_Stow"): clips.pop(k, None)
clips["Pelag_AN_Wreck2_Swing2"] = swing2; clips["Pelag_AN_Wreck2_Wait2"] = wait2
clips["Pelag_AN_Wreck2_Slam"] = slam; clips["Pelag_AN_Wreck2_Stow"] = stow
for c, r in (("Pelag_AN_Wreck2_Swing2", rows), ("Pelag_AN_Wreck2_Wait2", rowsw), ("Pelag_AN_Wreck2_Slam", rowss), ("Pelag_AN_Wreck2_Stow", rowst)):
    T["limits"]["summary"][c] = r["summary"].get(c); T["limits"]["violations_by_clip"][c] = r["violations"]
T["limits"]["violations_by_clip"]["Pelag_AN_Wreck2_Swing2_note"] = (
    "«скольжение» правой 3–11 (11–16 мм) — верчение пируэта на подушечке: центр пятна касания ≤ 1 мм/тик (bake/…Swing2.pivot.json)")
T["series_sim_ticks"] = dict(
    note="самые быстрые нажатия: этап стартует в тик контакт + 1; окно нажатия — 24 тика после каждого контакта",
    Swing1=dict(start=0, contact=7, window=[7, 31]),
    Swing2=dict(start=8, contact=8 + C2, clip_contact_frame=C2, window=[8 + C2, 8 + C2 + 24]),
    Slam=dict(start=8 + C2 + 1, overhead=8 + C2 + 1 + O3, contact=8 + C2 + 1 + C3, clip_contact_frame=C3, clip_overhead_frame=O3,
              window=[8 + C2 + 1 + C3, 8 + C2 + 1 + C3 + 24]),
    pause="Swing1 кадр 12 → Wait1; Swing2 кадр 19 → Wait2; окно кончилось → Stow")
T["stow_mount"] = dict(
    bone="mixamorig:Spine2", axes="локальные оси Spine2 в Unity, м", where="ниже, за правой лопаткой (было — за шеей справа)",
    grip_local_m=[0.132, 0.015, -0.184], head_centre_local_m=[-0.085, -0.321, -0.151], rotation="Spine2 · Rz(−25°), как было",
    old=dict(grip_local_m=[0.231, 0.280, -0.169], head_centre_local_m=[0.015, -0.056, -0.137]),
    reach="левая кисть доходит в кадре 6 с зазором 2,4 см (≤ 4 см), голову ловит без прохода сквозь тело (из Wait1 0,5 см, из Wait2 0,0 см)",
    applies="поменять крепление в риге (PelagAnchorRig, сокет на спине) — этот агент razlom/ не трогает")
for k in ("swing2_blocked", "swing2_contract", "wait2_blocked"): T.pop(k, None)
fh.seek(0); fh.write(json.dumps(T, indent=1, ensure_ascii=False)); fh.truncate(); fh.close()
print("timing written", list(T["clips"]))
