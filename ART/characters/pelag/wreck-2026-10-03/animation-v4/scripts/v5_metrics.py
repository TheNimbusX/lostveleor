"""Крушение v4 — замер вращения головы якоря по выходу wreck4sim (оси Unity, кадры 60/с).
python v5_metrics.py <sim.json> [json_out]
По каждому клипу: пиковая угловая скорость (рад/с, между соседними кадрами), кадров > 20 рад/с (кроме кадров удара),
полный поворот (сумма углов между кадрами), крен — вращение вокруг оси веретена (локальная +Y, к кольцу), сумма |крена|,
и рассогласование веретена с цепью (угол между +Y головы и направлением кольцо→хват при натянутой цепи).
"""
import json, math, sys


def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by, aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw, aw * bw - ax * bx - ay * by - az * bz)


def qconj(q): return (-q[0], -q[1], -q[2], q[3])


def qrot(q, v):
    x, y, z, w = q
    t = (2 * (y * v[2] - z * v[1]), 2 * (z * v[0] - x * v[2]), 2 * (x * v[1] - y * v[0]))
    return (v[0] + w * t[0] + y * t[2] - z * t[1], v[1] + w * t[1] + z * t[0] - x * t[2], v[2] + w * t[2] + x * t[1] - y * t[0])


def ang(q0, q1):
    d = abs(sum(a * b for a, b in zip(q0, q1)))
    return 2 * math.acos(min(1.0, d))


def twist_y(q0, q1):
    d = qmul(qconj(q0), q1)                      # поворот в осях головы
    if d[3] < 0: d = tuple(-c for c in d)
    return 2 * math.atan2(d[1], d[3])           # крен вокруг локальной +Y


def sub(a, b): return [x - y for x, y in zip(a, b)]


def norm(v):
    n = math.sqrt(sum(c * c for c in v)); return [c / n for c in v] if n > 1e-9 else [0, 0, 0]


def measure(d, impact_ts=()):
    fr = d["frames"]; dt = fr[1]["t"] - fr[0]["t"]
    per = {}; order = []
    for i in range(1, len(fr)):
        a, b = fr[i - 1], fr[i]; c = b["clip"].replace("Pelag_AN_Wreck4_", "")
        if c not in per: per[c] = dict(peak=0, peak_t=0, over20=[], total=0, roll=0, misalign=0, mis_t=0); order.append(c)
        s = per[c]; w = ang(a["q"], b["q"]) / dt
        if w > s["peak"]: s["peak"], s["peak_t"] = w, b["t"]
        if w > 20 and not any(abs(b["t"] - t) <= 1.5 * dt for t in impact_ts): s["over20"].append(round(b["t"], 3))
        s["total"] += ang(a["q"], b["q"]); s["roll"] += abs(twist_y(a["q"], b["q"]))
        if b["span"] >= b["cable"] - 0.03 and b["mode"] not in ("OnBack",):
            sh = qrot(b["q"], (0, 1, 0)); ch = norm(sub(b["grip"], b["ring"]))
            m = math.degrees(math.acos(max(-1, min(1, sum(x * y for x, y in zip(sh, ch))))))
            if m > s["misalign"]: s["misalign"], s["mis_t"] = m, b["t"]
    out = {}
    for c in order:
        s = per[c]
        out[c] = dict(peak_rad_s=round(s["peak"], 1), peak_t=round(s["peak_t"], 3), frames_over_20=len(s["over20"]),
                      total_deg=round(math.degrees(s["total"])), roll_deg=round(math.degrees(s["roll"])),
                      misalign_deg=round(s["misalign"]), mis_t=round(s["mis_t"], 3))
    return out


if __name__ == "__main__":
    d = json.load(open(sys.argv[1], encoding="utf-8"))
    ev = d.get("events", {}); imp = [c["t"] for c in d.get("contacts", [])]
    if isinstance(ev.get("impact"), dict): imp.append(ev["impact"]["t"])
    elif "impact" in ev: imp.append(ev["impact"])
    m = measure(d, imp)
    for c, s in m.items(): print("%-7s %s" % (c, s))
    tot = sum(s["total_deg"] for s in m.values()); pk = max(s["peak_rad_s"] for s in m.values())
    print("ALL total %d deg, peak %.1f rad/s, over20 %d" % (tot, pk, sum(s["frames_over_20"] for s in m.values())))
    if len(sys.argv) > 2: json.dump(m, open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False, indent=1)
