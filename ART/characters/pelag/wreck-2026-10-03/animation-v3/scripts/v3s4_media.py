"""Крушение v3 (06.10, Swing2 = продолжение вращения): последовательности кадров для видео без стоп-кадров (шаг полкадра = 60 к/с).

python v3s4_media.py series <anim_dir> <out seq.json>   — серия при самых быстрых нажатиях: Swing1 0→8 → Swing2 0→C2+1 → Slam 0→N3
                                                         (голова — запечки Swing1_w7, Swing2_w14, Slam_w13; клип меняется в кадре контакт+1)
python v3s4_media.py pause1 <anim_dir> <out seq.json>   — Swing1 0→12 → Wait1 ×2 (живой маятник) → Stow → намотка рига → на спине
python v3s4_media.py pause2 <anim_dir> <stow_w2.live.json> <out seq.json> — Swing1 0→8 → Swing2 0→N2 → Wait2 ×2 → Stow → на спине
python v3s4_media.py make|sheet ... — как v3s2_media (MP4 1× и 0,5×, GIF, лист поз)
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import v3s2_media as M

C2, N2 = 14, 19          # Swing2: контакт, последний кадр
C3, O3 = 13, 7           # Slam: контакт, над головой


def near(samples, t, key="t"):
    return min(samples, key=lambda s: abs(s[key] - t))


def bake(anim, name):
    return json.load(open(os.path.join(anim, "bake", name + ".anchorbake.json")))["samples"]


def series(anim, out):
    s1, s2, sl = bake(anim, "Pelag_AN_Wreck2_Swing1_w7"), bake(anim, "Pelag_AN_Wreck2_Swing2_w14"), bake(anim, "Pelag_AN_Wreck2_Slam_w%d" % C3)
    n3 = int(round(max(s["t"] for s in sl)))
    frames = []
    for k in range(0, 2 * 8):                       # Swing1 0 → 7,5 (кадр 8 = Swing2 кадр 0)
        t = k / 2; s = near(s1, t)
        frames.append(dict(clip="Pelag_AN_Wreck2_Swing1", frame=t, p=s["p"], q=s["q"],
                           label="Swing1 к%04.1f · тик %04.1f" % (t, t) + ("  КОНТАКТ 1" if abs(t - 7) < .26 else "")))
    for k in range(0, 2 * (C2 + 1)):                # Swing2 0 → C2 + 0,5 (кадр C2 + 1 = Slam кадр 0)
        t = k / 2; s = near(s2, t)
        frames.append(dict(clip="Pelag_AN_Wreck2_Swing2", frame=t, p=s["p"], q=s["q"],
                           label="Swing2 к%04.1f · тик %04.1f" % (t, 8 + t) + ("  КОНТАКТ 2" if abs(t - C2) < .26 else "")))
    for k in range(0, 2 * n3 + 1):
        t = k / 2; s = near(sl, t)
        lab = "Slam к%04.1f · тик %04.1f" % (t, 8 + C2 + 1 + t)
        lab += "  УДАР ОЗЕМЬ" if abs(t - C3) < .26 else "  над головой" if abs(t - O3) < .26 else ""
        frames.append(dict(clip="Pelag_AN_Wreck2_Slam", frame=t, p=s["p"], q=s["q"], label=lab))
    json.dump(dict(frames=frames, seamchecks=[["Pelag_AN_Wreck2_Swing1", 8, "Pelag_AN_Wreck2_Swing2", 0],
                                               ["Pelag_AN_Wreck2_Swing2", C2 + 1, "Pelag_AN_Wreck2_Slam", 0]]),
              open(out, "w", encoding="utf-8"), ensure_ascii=False)
    print("seq", len(frames))


def stow_tail(frames, st, t0, stow_n):
    ss, back, catch = st["samples"], st["back"], st["catchT"]
    end = catch + 4
    for k in range(1, int(2 * end) + 1):
        t = k / 2; f = min(t, float(stow_n))
        if t <= catch:
            s = near(ss, t); d = dict(clip="Pelag_AN_Wreck2_Stow", frame=f, p=s["p"], q=s["q"])
            if s["step"] >= 1: d["grip"] = s["grip"]
            what = "рукоять за правую лопатку" if t <= st["handT"] else "намотка рига 5 м/с"
        else:
            bb = back[min(len(back) - 1, int(f))]; d = dict(clip="Pelag_AN_Wreck2_Stow", frame=f, p=bb["centre"], q=bb["q"], grip=bb["grip"])
            what = "якорь на спине"
        d["label"] = "Stow к%04.1f — %s" % (t, what)
        frames.append(d)
    return catch


def pause1(anim, out):
    bk = os.path.join(anim, "bake")
    s1 = bake(anim, "Pelag_AN_Wreck2_Swing1_w7")
    live = json.load(open(os.path.join(bk, "Pelag_AN_Wreck2_Wait1.live.json")))["samples"]
    st = json.load(open(os.path.join(bk, "Pelag_AN_Wreck2_Stow.live.json")))
    frames = []
    for k in range(0, 2 * 12 + 1):
        t = k / 2; s = near(s1, t)
        frames.append(dict(clip="Pelag_AN_Wreck2_Swing1", frame=t, p=s["p"], q=s["q"], label="Swing1 к%04.1f" % t))
    for k in range(1, 2 * 24 + 1):
        w = k / 2; s = near(live, w); f = w % 12 if w % 12 else 12
        frames.append(dict(clip="Pelag_AN_Wreck2_Wait1", frame=f, p=s["p"], q=s["q"], label="Wait1 к%04.1f (окно, петля %d), якорь живой" % (f, int((w - 1e-6) // 12) + 1)))
    n = json.load(open(os.path.join(bk, "Pelag_AN_Wreck2_Stow.grip.json")))["frames"]
    catch = stow_tail(frames, st, 0, n)
    json.dump(dict(frames=frames, seamchecks=[["Pelag_AN_Wreck2_Swing1", 12, "Pelag_AN_Wreck2_Wait1", 0], ["Pelag_AN_Wreck2_Wait1", 0, "Pelag_AN_Wreck2_Stow", 0]]),
              open(out, "w", encoding="utf-8"), ensure_ascii=False)
    print("seq", len(frames), "catch", catch)


def pause2(anim, stow_live, out):
    bk = os.path.join(anim, "bake")
    s1, s2 = bake(anim, "Pelag_AN_Wreck2_Swing1_w7"), bake(anim, "Pelag_AN_Wreck2_Swing2_w14")
    live = json.load(open(os.path.join(bk, "Pelag_AN_Wreck2_Wait2.live.json")))["samples"]
    st = json.load(open(stow_live))
    frames = []
    for k in range(0, 2 * 8):
        t = k / 2; s = near(s1, t)
        frames.append(dict(clip="Pelag_AN_Wreck2_Swing1", frame=t, p=s["p"], q=s["q"], label="Swing1 к%04.1f" % t))
    for k in range(0, 2 * N2 + 1):
        t = k / 2; s = near(s2, t)
        frames.append(dict(clip="Pelag_AN_Wreck2_Swing2", frame=t, p=s["p"], q=s["q"], label="Swing2 к%04.1f" % t + ("  КОНТАКТ 2" if abs(t - C2) < .26 else "")))
    for k in range(1, 2 * 24 + 1):
        w = k / 2; s = near(live, w); f = w % 12 if w % 12 else 12
        frames.append(dict(clip="Pelag_AN_Wreck2_Wait2", frame=f, p=s["p"], q=s["q"], label="Wait2 к%04.1f (окно, петля %d), якорь живой" % (f, int((w - 1e-6) // 12) + 1)))
    n = json.load(open(os.path.join(bk, "Pelag_AN_Wreck2_Stow.grip.json")))["frames"]
    catch = stow_tail(frames, st, 0, n)
    json.dump(dict(frames=frames, seamchecks=[["Pelag_AN_Wreck2_Swing2", N2, "Pelag_AN_Wreck2_Wait2", 0], ["Pelag_AN_Wreck2_Wait2", 0, "Pelag_AN_Wreck2_Stow", 0]]),
              open(out, "w", encoding="utf-8"), ensure_ascii=False)
    print("seq", len(frames), "catch", catch)


if __name__ == "__main__":
    mode = sys.argv[1]
    if mode == "series": series(sys.argv[2], sys.argv[3])
    elif mode == "pause1": pause1(sys.argv[2], sys.argv[3])
    elif mode == "pause2": pause2(sys.argv[2], sys.argv[3], sys.argv[4])
    elif mode == "make": M.make(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5].split(","), sys.argv[6])
    elif mode == "sheet":
        M.ImageDraw_title = sys.argv[8] if len(sys.argv) > 8 else None
        M.sheet(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5].split(","), [int(x) for x in sys.argv[6].split(",")], sys.argv[7].split("|"))
