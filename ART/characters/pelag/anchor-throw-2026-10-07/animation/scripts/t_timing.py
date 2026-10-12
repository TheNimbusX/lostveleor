"""timing.json клипов Броска якоря 07.10 (Pelag_AN_AnchorThrow2_*): python t_timing.py <out_dir>
Таблицы — то же правило, что PelagAnchorThrowClipRules (вид) и тесты представления AnchorThrowClipRulesTests."""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import t_lib as T

OUT = sys.argv[1]
LAST = {n: b - a for n, a, b in T.CLIPS}
START = {n: a for n, a, b in T.CLIPS}
FLY, YANK, HAUL, CATCH = START["Fly"], START["Yank"], START["Haul"], START["Catch"]


def clip_at(c):
    for n, a, b in reversed(T.CLIPS):
        if c >= a or n == "Throw": return [n, round(min(c - a, b - a), 4)]


def flight(f, u):
    if u <= 0: return 2.0
    if u >= f + 1: return float(YANK)
    if f >= 3: return 2.0 + u if u <= 3 else FLY + (u - 3) * LAST["Fly"] / (f - 2)
    return 3.0 if u <= 1 else 3.0 + (u - 1) * (YANK - 3.0) / f


def haul(r, y):
    if r == 4: tab = [0, 1, 3, 5]
    elif r == 5: tab = [0, 1, 7 / 3, 11 / 3, 5]
    else: tab = [LAST["Haul"] * (k - 1) / (r - 1) for k in range(1, r + 1)]
    return tab[max(0, min(len(tab) - 1, y - 1))]


def key(w, f, r, n):
    release, hold = w, w + f + 1
    taut = w + f + 2; catch = taut + r
    if n <= 0: return 0.0
    if n <= release: return 2.0 * n / w
    if n < hold: return flight(f, n - release)
    if n <= taut: return YANK + (n - hold) / max(1, taut - hold)
    if n < catch: return HAUL + haul(r, n - taut)
    return CATCH + min(LAST["Catch"], n - catch)


timing = {
    "skill": "Бросок якоря 07.10 (Pelag_AN_AnchorThrow2_*): клипы по листу поз pose-sheet-v1 (замах → выпуск → цепь "
             "разматывается → держит → рывок → ловля → готов), тайминг Sim 25 тиков (контракт AGENTS/specs/anchor-throw-view-contract.md §0)",
    "fps": 30,
    "frame_is_tick": "при броске на 7 м: замах W = 2, полёт F = 7, натяг 2 (HoldTick C+10, рывок TautTick C+11), возврат R = 6, ловля C+17, "
                     "удержание 2, выход 6 (конец C+25); короче/длиннее — по таблицам retime",
    "source": "настоящее движение: замах и выпуск — Pelag_AN_Sabre1 (тейк Mixamo SaberCombo, закрутка и хлёст из стойки серии, "
              "тики сабли " + ", ".join("%g→%g" % k for k in T.SRC_WARP) + "), дальше стойка серии + кривые веса/корпуса "
              "(t_lib.py: HIP_S, HIP_DZ, PYAW, CYAW, LEAN); руки — решатель v3_arms (кулак на цепи), стопы стоят (правая — шаг назад на "
              "выпуске и обратно на выходе)",
    "rig": "Pelag_v6_MixamoRig, привязка Pelag_AN_AnchorThrow2Bind (= Pelag_AN_Wreck4Bind, покой v6 стоя), предел таза 0,85 (Blender 0,77)",
    "hands": "рукоять цепи в ЛЕВОМ кулаке весь бросок (из него выходит цепь); якорь бросает и ловит ПРАВАЯ; на цепи (тики 7,5–15,6) "
             "правый кулак на 0,17 м впереди левого по линии броска",
    "chain": "Throw [0..5] → Fly [0..5] → Yank [0..2] → Haul [0..5] → Catch [0..8]; стыки — одна поза",
    "clips": {"Pelag_AN_AnchorThrow2_" + n: {"frames": b - a + 1, "master_ticks": [a, b]} for n, a, b in T.CLIPS},
    "keys": {
        "windup": {"clip": "Throw", "frame": 1, "pose": 1},
        "release": {"clip": "Throw", "frame": 2, "pose": 2},
        "payout": {"clip": "Throw", "frame": 5, "pose": 3},
        "hold": {"clip": "Yank", "frame": 0, "pose": 4, "tick": "HoldTick"},
        "yank": {"clip": "Yank", "frame": 1, "tick": "TautTick (рывок)"},
        "yank_peak": {"clip": "Haul", "frame": 1, "pose": 5},
        "catch": {"clip": "Catch", "frame": 0, "pose": 6, "tick": "CatchTick"},
        "ready": {"clip": "Catch", "frame": 6, "pose": 7},
        "walk_from": {"clip": "Catch", "frame": 4},
        "stow_handoff": {"clip": "Catch", "frame": 7},
    },
    "planted": {"rule": "кадры, где стопа стоит: [левая, правая]", "Throw": [[0, 1, 2, 3, 4, 5], [0, 1, 3, 4, 5]],
                "Fly": [[0, 1, 2, 3, 4, 5]] * 2, "Yank": [[0, 1, 2]] * 2, "Haul": [[0, 1, 2, 3, 4, 5]] * 2,
                "Catch": [[0, 1, 2, 3, 4, 5, 6, 7, 8], [0, 1, 2, 5, 6, 7, 8]]},
    "retime": {
        "windup": {"2": [0.0, 1.0, 2.0], "3": [0.0, round(2 / 3, 4), round(4 / 3, 4), 2.0]},
        "flight": {"rule": "u = тик − выпуск (0…F+1): F ≥ 3 — Throw 2..5 1:1, затем Fly растянут на F − 2 тиков до Yank 0 в HoldTick; "
                           "F ≤ 2 — Throw 2, 3 и по прямой до Yank 0",
                   "table": {str(f): [clip_at(flight(f, u)) for u in range(f + 2)] for f in range(1, 11)}},
        "hold": "HoldTick — Yank 0 (поза «держит»), TautTick — Yank 1 (рывок пошёл)",
        "haul": {"rule": "y = тик − TautTick (1…R): Haul, кадр 1 (пик рывка) всегда через тик после y = 1; y = R — ловля = Haul 5 = Catch 0",
                 "table": {str(r): [round(haul(r, y), 4) for y in range(1, r + 1)] for r in (4, 5, 6)}},
    },
    "layouts": {"examples": {"W%d F%d R%d" % (w, f, r): [[n] + clip_at(key(w, f, r, n)) for n in range(0, w + f + 2 + r + 9)]
                             for w, f, r in ((2, 7, 6), (3, 7, 6), (2, 4, 4), (2, 5, 5), (2, 1, 4), (2, 10, 6))}},
}
os.makedirs(OUT, exist_ok=True)
json.dump(timing, open(os.path.join(OUT, "timing.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("timing.json", os.path.join(OUT, "timing.json"))
