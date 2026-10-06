"""Крушение v3: дописать в timing.json клип Wait1 (и пометку о Swing2) — чтение-правка-запись, чужие ключи не трогаются.
python v3s2_timing.py <animation-v3 dir>"""
import json, math, os, re, sys

V3 = sys.argv[1]
B = os.path.join(V3, "bake")
C = "Pelag_AN_Wreck2_Wait1"
R3 = lambda v: [round(x, 4) for x in v]
rows_doc = json.load(open(os.path.join(V3, C + ".rows.json"), encoding="utf-8"))
rows = rows_doc["rows"]
grip = json.load(open(os.path.join(B, C + ".grip.json"), encoding="utf-8"))
live = json.load(open(os.path.join(B, C + ".live.json"), encoding="utf-8"))["samples"]
names, sub = grip["boneNames"], grip["sub"]


def table(path):
    out = []
    if not os.path.exists(path): return out
    for line in open(path, encoding="utf-8").read().splitlines():
        m = re.match(r"\s+(ok|FAIL|info)\s+(\S+)\s+(.*?):\s(.*)$", line)
        if m: out.append(dict(status=m.group(1), id=m.group(2), check=m.group(3).strip(), value=m.group(4).strip()))
    return out


livetxt = open(os.path.join(B, C + ".live.txt"), encoding="utf-8").read() if os.path.exists(os.path.join(B, C + ".live.txt")) else ""
m = re.search(r"nearTaut clr (-?[\d.]+) (\S+ ?\S* @t[\d.]+)", livetxt)
near = dict(cm=float(m.group(1)), where=m.group(2)) if m else None
ab = [r for r in table(os.path.join(B, C + ".livecheck.txt")) if r["id"] not in ("5", "6", "7", "1a", "1b")]
ankles = [dict(frame=f, Left=R3(grip["samples"][f * sub]["bones"][names.index("LeftFoot")]),
               Right=R3(grip["samples"][f * sub]["bones"][names.index("RightFoot")])) for f in (0, 6, 12)]
head = [dict(tick=s["t"], p=R3(s["p"]), speed=round(math.dist([0, 0, 0], s["v"]), 1), taut=s["taut"]) for s in live if abs(s["t"] - round(s["t"])) < 1e-6 and round(s["t"]) % 2 == 0]
entry = dict(
    frames=13, loop=True, loop_frames=12, contact_frame=None,
    mask="только верх тела: руки, грудь, голова (ноги и таз — бег/стойка); ноги клипа = Swing1 кадр 12",
    segments={"brake": [1, 8], "return": [8, 12]},
    marks={"seam_in": "кадр 0 = Swing1 кадр 12 (ключи копией из рабочего .blend Swing1)",
           "loop": "кадр 12 = кадр 0", "next_press": "следующий клип входит из Wait1 блендом 2 кадра в свой кадр 0 (SPEC 4 «Стыки»)",
           "pose": "кисти уходят вниз за левое бедро (грудь +20° влево, наклон +21° вперёд и +12° влево позвоночником), "
                   "якорь проходит за спиной низко и садится на землю справа (тормозит), кисти возвращаются в позу 3"},
    hands="левый кулак — гнездо хвата на пути gripopt_s2; правый на рукояти 0,12–0,24 м (ось рукояти к правому плечу)",
    grip_track=[dict(frame=f, grip=R3(grip["samples"][f * sub]["grip"]), support=R3(grip["samples"][f * sub]["support"]),
                     hand_gap=rows[f]["hand_gap"]) for f in range(13)],
    feet=dict(note="ноги не двигаются (маска верха; стопы кадра 12 Swing1)", ankles=ankles),
    anchor_live=dict(note="голова не запекается: живой маятник AnchorRigCore.StepLive от состояния запечки Swing1@12 "
                          "(p, v); проверено на 2 петли = 24 тика окна, разброс начальной скорости ±6 %",
                     start=dict(p=R3(live[0]["p"]), v=R3(live[0]["v"])), near_taut_clearance=near, head_every_2_ticks=head,
                     checks=ab, file=C + ".live.json"),
    seams=rows_doc.get("seams"),
)
p = os.path.join(V3, "timing.json")
fh = open(p, "r+", encoding="utf-8", newline="")
T = json.loads(fh.read())
T.setdefault("clips", {})[C] = entry
T.setdefault("limits", {}).setdefault("summary", {})[C] = rows_doc["summary"].get(C)
T["limits"].setdefault("violations_by_clip", {})[C] = rows_doc["violations"]
T["swing2_blocked"] = dict(
    status="не собран: мах 2 с контактом в кадре 6 (тик 14) физически невозможен от состояния Swing1@8",
    why="голова в Swing1 кадр 8 летит влево 25,6 м/с на радиусе ≈2 м от хвата; за 6 тиков (0,2 с) она проходит ≈5 м — "
        "≈110° дуги вокруг хвата, к тику 14 она слева-сзади (−111°). Вернуть её вперёд (±10°) — нужен полный оборот (≈13 м) "
        "или смена направления вращения (восьмёрка); кисти рядом с плечом (≤0,56 м, ≤8 м/с) не дают ни того, ни другого.",
    probe="bake/Pelag_AN_Wreck2_Swing2.feasibility.txt, scripts/v3s2_swing2_probe.sh (gripopt_s2, та же физика)",
)
fh.seek(0); fh.write(json.dumps(T, indent=1, ensure_ascii=False)); fh.truncate(); fh.close()
print("timing written", p, "clips", list(T["clips"]))
