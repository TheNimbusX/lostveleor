"""Крушение v3, Slam / Stow / серия: последовательности кадров для видео (без стоп-кадров; MP4/GIF — v3s2_media.make).

python v3s3_media.py slam  <anim_dir> <out seq.json>  — Slam 0→27 (шаг полкадра), голова — запечка Slam_w16 от контракта Swing2@7
python v3s3_media.py pause <anim_dir> <out seq.json>  — Swing1 0→12 → Wait1 ×2 (окно, живой маятник) → Stow 0→7 → намотка рига
                                                         (тело в стойке, голова живая до поимки креплением) → 4 кадра на спине
python v3s3_media.py make  ... — как v3s2_media.make
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import v3s2_media as M


def near(samples, t, key="t"):
    return min(samples, key=lambda s: abs(s[key] - t))


def slam(anim, out):
    b = json.load(open(os.path.join(anim, "bake", "Pelag_AN_Wreck2_Slam_w16.anchorbake.json")))["samples"]
    frames = []
    for k in range(0, 2 * 27 + 1):
        t = k / 2; s = near(b, t)
        lab = "Slam к%04.1f" % t + ("  КОНТАКТ" if abs(t - 16) < .26 else "  над головой" if abs(t - 9) < .26 else "")
        frames.append(dict(clip="Pelag_AN_Wreck2_Slam", frame=t, p=s["p"], q=s["q"], label=lab + " · вход: контракт Swing2@7"))
    json.dump(dict(frames=frames, seamchecks=[]), open(out, "w", encoding="utf-8"), ensure_ascii=False)
    print("seq", len(frames))


def pause(anim, out):
    bk = os.path.join(anim, "bake")
    s1 = json.load(open(os.path.join(bk, "Pelag_AN_Wreck2_Swing1_w7.anchorbake.json")))["samples"]
    live = json.load(open(os.path.join(bk, "Pelag_AN_Wreck2_Wait1.live.json")))["samples"]
    st = json.load(open(os.path.join(bk, "Pelag_AN_Wreck2_Stow.live.json")))
    ss, back, catch = st["samples"], st["back"], st["catchT"]
    frames = []
    for k in range(0, 2 * 12 + 1):
        t = k / 2; s = near(s1, t)
        frames.append(dict(clip="Pelag_AN_Wreck2_Swing1", frame=t, p=s["p"], q=s["q"], label="Swing1 к%04.1f" % t))
    for k in range(1, 2 * 24 + 1):
        w = k / 2; s = near(live, w)
        f = w % 12 if w % 12 else 12
        frames.append(dict(clip="Pelag_AN_Wreck2_Wait1", frame=f, p=s["p"], q=s["q"], label="Wait1 к%04.1f (окно, петля %d), якорь живой" % (f, int((w - 1e-6) // 12) + 1)))
    end = catch + 4
    for k in range(1, int(2 * end) + 1):
        t = k / 2; f = min(t, 7.0)
        if t <= catch:
            s = near(ss, t); d = dict(clip="Pelag_AN_Wreck2_Stow", frame=f, p=s["p"], q=s["q"])
            if s["step"] >= 1: d["grip"] = s["grip"]
            hand = st["handT"]
            what = "рывок, правая отпускает" if t < 3 else "кисти в стойку" if t <= 7 else "риг ждёт 0,35 с"
            if s["step"] >= 1: what = "рукоять на спину (риг)" if t < hand + 6.6 else "намотка рига 5 м/с"
        else:
            bb = back[7]; d = dict(clip="Pelag_AN_Wreck2_Stow", frame=7.0, p=bb["centre"], q=bb["q"], grip=bb["grip"]); what = "якорь на спине"
        d["label"] = "Stow к%04.1f — %s" % (t, what)
        frames.append(d)
    json.dump(dict(frames=frames, seamchecks=[["Pelag_AN_Wreck2_Swing1", 12, "Pelag_AN_Wreck2_Wait1", 0], ["Pelag_AN_Wreck2_Wait1", 0, "Pelag_AN_Wreck2_Stow", 0]]),
              open(out, "w", encoding="utf-8"), ensure_ascii=False)
    print("seq", len(frames), "catch", catch)


if __name__ == "__main__":
    mode = sys.argv[1]
    if mode == "slam": slam(sys.argv[2], sys.argv[3])
    elif mode == "pause": pause(sys.argv[2], sys.argv[3])
    elif mode == "make": M.make(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5].split(","), sys.argv[6])
    elif mode == "sheet":
        M.ImageDraw_title = sys.argv[8] if len(sys.argv) > 8 else None
        M.sheet(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5].split(","), [int(x) for x in sys.argv[6].split(",")], sys.argv[7].split("|"))
