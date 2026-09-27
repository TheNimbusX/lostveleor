"""Review media from the rendered frames (system Python: PIL + imageio_ffmpeg).

python review_media.py [take-substrings...] [--roll-seq]
  -> ../review/animation_r01/{<Take>_<view>.mp4, <Take>_keys.jpg, Sequence_game.mp4, refs/*}
  r02: RollSeq_uncurl.mp4 / RollSeq_dizzy.mp4 (640x360) and refs/roll_target_*.jpg; with take substrings only
  those takes are (re)encoded and the r01 media are left alone.
Every frame gets the take, view and frame number burned in; marked frames (contact / landing / release)
get a red border and a label, so the owner can step to them in the player.
"""
import shutil
import subprocess
import sys
from pathlib import Path

import imageio_ffmpeg
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from takes import ORDER, TAKES  # noqa: E402

PROD = HERE.parent
OUT = PROD / "review" / "animation_r01"
FR = OUT / "frames"
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
FONT = "C:/Windows/Fonts/segoeui.ttf"
FONT_B = "C:/Windows/Fonts/segoeuib.ttf"
RED = (226, 64, 52)
LOOP_REPEAT = {"ForestSplitter_Idle": 2, "ForestSplitter_Walk": 9, "ForestSplitter_RollLoop": 5}
HOLD = 12  # frames to hold the last pose of one-shot takes


def marks(take):
    """{frame: label} to highlight."""
    sp = TAKES[take]
    m = {}
    if take.endswith("Bite"):
        m[sp["contact"]] = "КОНТАКТ · тик 18"
    if take.endswith("Pop"):
        m[sp["takeoff"]] = "отрыв"
        m[sp["contact"]] = "ПРИЗЕМЛЕНИЕ · тик 8"
    if take.endswith("Death"):
        m[sp["release"]] = "СКРЫТЬ · ДЕТИ"
    if take.endswith("RollCurl"):
        m[sp["lock"]] = "ФИКСАЦИЯ · тик 12"
        m[sp["shake"][0]] = "ТРЯСКА · тик 24"
        m[sp["launch"]] = "СТАРТ · тик 30"
    if take.endswith("RollDizzy"):
        m[0] = "УДАР О СТЕНУ"
    return m


def font(size, bold=False):
    return ImageFont.truetype(FONT_B if bold else FONT, size)


def overlay(path, title, frame, total, mark):
    im = Image.open(path).convert("RGB")
    d = ImageDraw.Draw(im)
    d.rectangle((0, 0, im.width, 26), fill=(20, 27, 25))
    d.text((8, 3), title, font=font(15), fill=(234, 233, 223))
    txt = "кадр %d / %d" % (frame, total)
    w = d.textlength(txt, font=font(15, True))
    d.text((im.width - w - 8, 3), txt, font=font(15, True), fill=(202, 214, 157))
    if mark:
        d.rectangle((0, 0, im.width - 1, im.height - 1), outline=RED, width=6)
        f = font(20, True)
        w = d.textlength(mark, font=f)
        d.rectangle(((im.width - w) / 2 - 10, im.height - 44, (im.width + w) / 2 + 10, im.height - 12), fill=RED)
        d.text(((im.width - w) / 2, im.height - 42), mark, font=f, fill="white")
    return im


def encode(images, dest):
    args = [FFMPEG, "-v", "error", "-y", "-f", "image2pipe", "-framerate", "30", "-vcodec", "png", "-i", "pipe:0",
            "-an", "-c:v", "libx264", "-profile:v", "baseline", "-crf", "20", "-movflags", "+faststart",
            "-pix_fmt", "yuv420p", str(dest)]
    proc = subprocess.Popen(args, stdin=subprocess.PIPE)
    try:
        for im in images:
            from io import BytesIO
            buf = BytesIO()
            im.save(buf, "PNG")
            proc.stdin.write(buf.getvalue())
    finally:
        proc.stdin.close()
    assert proc.wait() == 0, dest


def take_video(take, view):
    sp = TAKES[take]
    n = sp["frames"]
    count = n if sp["loop"] else n + 1
    mk = marks(take)
    label = "%s · %s" % (take.replace("ForestSplitter_", ""), "игровая камера" if view == "game" else "сбоку")
    frames = [overlay(FR / f"{take}_{view}" / f"{f:04d}.png", label, f, n, mk.get(f)) for f in range(count)]
    seq = frames * LOOP_REPEAT.get(take, 1) if sp["loop"] else frames + [frames[-1]] * HOLD
    encode(seq, OUT / f"{take}_{view}.mp4")


def key_strip(take):
    sp = TAKES[take]
    n = sp["frames"]
    mk = marks(take)
    if sp["loop"]:
        picks = [round(i * n / 6) for i in range(6)]
    else:
        picks = sorted(set([0, n] + list(mk) + [round(i * n / 5) for i in range(1, 5)]))
        if take.endswith("Bite"):
            picks = [0, 6, 12, 15, 17, 18, 20, 24, 30]
        if take.endswith("RollCurl"):
            picks = [0, 2, 4, 6, 8, 10, 12, 24, 26, 30]
        if take.endswith("RollUncurl"):
            picks = [0, 4, 8, 10, 12, 14, 17, 20, 23, 30]
        if take.endswith("RollDizzy"):
            picks = [0, 2, 5, 8, 12, 18, 24, 30, 37, 45]
    tile = 200
    sheet = Image.new("RGB", (tile * len(picks), tile * 2 + 28), (20, 27, 25))
    d = ImageDraw.Draw(sheet)
    for r, view in enumerate(("game", "side")):
        for i, f in enumerate(picks):
            im = Image.open(FR / f"{take}_{view}" / f"{f % (n if sp['loop'] else n + 1):04d}.png").convert("RGB")
            im = im.resize((tile, tile))
            if f in mk:
                ImageDraw.Draw(im).rectangle((0, 0, tile - 1, tile - 1), outline=RED, width=5)
            sheet.paste(im, (i * tile, 28 + r * tile))
    for i, f in enumerate(picks):
        lab = "f%d  %.2fs" % (f, f / 30) + ("  " + mk[f].split(" ")[0].lower() if f in mk else "")
        d.text((i * tile + 6, 5), lab, font=font(14, f in mk), fill=RED if f in mk else (202, 214, 157))
    sheet.save(OUT / f"{take}_keys.jpg", quality=88)


def sequence_video():
    files = sorted((FR / "Sequence_game").glob("*.png"))
    ims = []
    for i, p in enumerate(files):
        if i < 12:
            lab, f, n, mk = "Родитель: Death", i, 12, None
        elif i < 23:
            lab, f, n = "Дети ×0.6: Pop, сим уносит на 1 м за 8 тиков", i - 12, 10
            mk = {0: "ДЕТИ ПОЯВИЛИСЬ", 8: "ПРИЗЕМЛЕНИЕ"}.get(f)
        else:
            lab, f, n, mk = "Дети: Idle", i - 23, 60, None
        ims.append(overlay(p, lab, f, n, mk))
    encode(ims + [ims[-1]] * HOLD, OUT / "Sequence_game.mp4")


ROLL_SEQ = {  # (segment label, take frames, marks) per rendered segment; must match render_review.py --roll-seq
    "uncurl": [("RollCurl: сжимается, трясётся", 31, {12: "ФИКСАЦИЯ", 24: "ТРЯСКА", 30: "СТАРТ"}),
               ("Катится: RollLoop + вращение вида, 0,40 м/тик", 16, {}),
               ("RollUncurl: окно наказания 30 тиков", 31, {0: "СТОП"}),
               ("Idle", 12, {})],
    "dizzy": [("RollCurl: сжимается, трясётся", 31, {12: "ФИКСАЦИЯ", 24: "ТРЯСКА", 30: "СТАРТ"}),
              ("Катится: RollLoop + вращение вида, 0,40 м/тик", 9, {}),
              ("RollDizzy: удар о стену, 45 тиков", 46, {0: "УДАР О СТЕНУ"}),
              ("Idle", 12, {})],
}


def roll_sequence_video(name):
    files = sorted((FR / f"RollSeq_{name}").glob("*.png"))
    segs = ROLL_SEQ[name]
    assert len(files) == sum(n for _, n, _ in segs), (name, len(files))
    ims, i = [], 0
    for lab, n, mk in segs:
        first = 1 if lab == "Idle" or lab.startswith("Катится") else 0   # roll ticks and idle count from 1
        for k in range(n):
            ims.append(overlay(files[i], lab, k + first, n - 1 + first, mk.get(k)))
            i += 1
    encode(ims + [ims[-1]] * HOLD, OUT / f"RollSeq_{name}.mp4")


def roll_refs():
    src = PROD / "vfx_target_frames_2026-09-27"
    for stem, dst in (("2-roll-windup", "roll_target_windup.jpg"), ("1-roll", "roll_target_roll.jpg")):
        im = Image.open(src / f"{stem}.png").convert("RGB")
        im.thumbnail((960, 960))
        im.save(OUT / "refs" / dst, quality=86)


if __name__ == "__main__":
    args = sys.argv[1:]
    only = [a for a in args if not a.startswith("--")]
    (OUT / "refs").mkdir(parents=True, exist_ok=True)
    if not only:
        for ref in ("bite", "idle_walk", "death"):
            shutil.copy2(PROD / "references" / f"{ref}_sheet.jpg", OUT / "refs" / f"{ref}_sheet.jpg")
    for take in ORDER:
        if only and not any(o in take for o in only):
            continue
        for view in ("game", "side"):
            take_video(take, view)
        key_strip(take)
        print("MEDIA", take, flush=True)
    if not only:
        sequence_video()
    if "--roll-seq" in args:
        roll_refs()
        for name in ROLL_SEQ:
            roll_sequence_video(name)
            print("MEDIA RollSeq", name, flush=True)
    print("MEDIA_DONE", flush=True)
