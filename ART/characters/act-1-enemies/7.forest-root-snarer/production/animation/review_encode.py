"""Annotate the review renders and encode per-take mp4s (system python: PIL + imageio_ffmpeg).
python review_encode.py [take ...]   (takes given: only those are re-encoded, media.json is merged)
In : work/review_frames/<take>/<view>/NNNN.png (640x440)
Out: ../review/animation_r01/<short>_<view>.mp4 (640x480, 30 fps) and keys_<short>.jpg.
Every frame gets a counter and a timeline strip with the take's events; contact frames get an
amber border and a big label so the sim tick can be checked frame by frame."""
import json, subprocess, sys, tempfile, shutil
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import imageio_ffmpeg

HERE = Path(__file__).resolve().parent
SRC = HERE / "work" / "review_frames"
OUT = HERE.parent / "review" / "animation_r01"
OUT.mkdir(parents=True, exist_ok=True)
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
AMBER, GREEN, INK = (255, 214, 120), (190, 222, 150), (235, 233, 223)
STRIP = 40


def font(size, bold=False):
    for name in (("segoeuib.ttf" if bold else "segoeui.ttf"), "arial.ttf"):
        try:
            return ImageFont.truetype(f"C:/Windows/Fonts/{name}", size)
        except OSError:
            pass
    return ImageFont.load_default()


F_S, F_M, F_B = font(14), font(16, True), font(30, True)
# events: (frame, label, is_contact)
TAKES = {
    "ForestRootSnarer_Idle": {"short": "idle", "loop": True, "events": [(0, "шов петли", False)],
                              "keys": [0, 15, 30, 45]},
    "ForestRootSnarer_Walk": {"short": "walk", "loop": True,
                              "events": [(0, "Л плита", True), (1.5, "П задн.", False), (5.5, "Л задн.", False),
                                         (8, "П плита", True), (9.5, "П задн.", False), (13.5, "Л задн.", False)],
                              "keys": [0, 4, 8, 12]},
    "ForestRootSnarer_Slam": {"short": "slam", "loop": False,
                              "events": [(12, "плиты над головой", False), (15, "КОНТАКТ", True),
                                         (36, "корни", True), (60, "рывок", False)],
                              "keys": [0, 12, 15, 36, 60, 72]},
    "ForestRootSnarer_Hit": {"short": "hit", "loop": False, "events": [(3, "пик", False)], "keys": [0, 3, 6, 12]},
    "ForestRootSnarer_Death": {"short": "death", "loop": False,
                               "events": [(8, "вздыбился", False), (25, "брюхо на земле", True), (32, "лежит", False)],
                               "keys": [0, 8, 19, 25, 45]},
    # 27.09 — новые способности
    "ForestRootSnarer_Mend": {"short": "mend", "loop": False,
                              "events": [(5, "замах", False), (8, "КОНТАКТ", True), (20, "толчок", False),
                                         (28, "сбор", False), (30, "ВОЛНА", True), (38, "выдёргивает", False)],
                              "keys": [0, 5, 8, 20, 28, 30, 42, 50]},
}
HOLD = 10  # extra frames on the last pose of one-shot takes (marked in the strip)


def annotate(img, take, cfg, f, n, view, held=False):
    W, H = img.size
    can = Image.new("RGB", (W, H + STRIP), (24, 30, 28))
    can.paste(img, (0, 0))
    d = ImageDraw.Draw(can)
    contact = next((lab for ef, lab, c in cfg["events"] if c and abs(ef - f) < 0.01 and not held), None)
    x0, x1, y = 16, W - 16, H + 24
    d.line((x0, y, x1, y), fill=(90, 104, 92), width=3)
    for ef, lab, c in cfg["events"]:
        x = x0 + (x1 - x0) * ef / n
        d.line((x, y - 9, x, y + 7), fill=AMBER if c else GREEN, width=3 if c else 2)
    px = x0 + (x1 - x0) * f / n
    d.ellipse((px - 6, y - 6, px + 6, y + 6), fill=INK)
    d.text((x0, H + 2), f"кадр {f} / {n}" + ("  (стоп-кадр)" if held else ""), font=F_S, fill=INK)
    evs = [lab for ef, lab, c in cfg["events"] if abs(ef - f) < 0.51 and not held]
    if evs:
        d.text((x1, H + 2), " · ".join(evs), font=F_S, fill=AMBER if contact else GREEN, anchor="ra")
    d.text((10, 8), f"{take.split('_')[1]} · {'игровая камера' if view == 'game' else 'сбоку'}", font=F_M, fill=INK,
           stroke_width=2, stroke_fill=(20, 24, 22))
    if contact:
        d.rectangle((0, 0, W - 1, H - 1), outline=AMBER, width=6)
        d.text((W - 14, 10), f"{contact} · кадр {f}", font=F_B, fill=AMBER, anchor="ra",
               stroke_width=3, stroke_fill=(30, 26, 12))
    return can


def encode(frames_dir, mp4):
    cmd = [FFMPEG, "-y", "-loglevel", "error", "-framerate", "30", "-i", str(frames_dir / "%04d.jpg"),
           "-vf", "scale=in_range=pc:out_range=tv,format=yuv420p", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-color_range", "tv", "-crf", "20", "-preset", "slow", "-movflags", "+faststart", str(mp4)]
    subprocess.run(cmd, check=True)


def key_sheet(take, cfg):
    ks = cfg["keys"]
    cols = len(ks)
    cw = min(320, 1280 // cols)
    ch = int(cw * 480 / 640)
    sheet = Image.new("RGB", (cw * cols, ch * 2), (24, 30, 28))
    n = TAKES_N[take]
    for r, view in enumerate(("game", "side")):
        for c, f in enumerate(ks):
            im = annotate(Image.open(SRC / take / view / f"{f:04d}.png").convert("RGB"), take, cfg, f, n, view)
            sheet.paste(im.resize((cw, ch), Image.LANCZOS), (c * cw, r * ch))
    sheet.save(OUT / f"keys_{cfg['short']}.jpg", quality=86)


TAKES_N = {}
only = set(sys.argv[1:])
manifest = json.loads((OUT / "media.json").read_text(encoding="utf8")) if only and (OUT / "media.json").exists() else {}
for take, cfg in TAKES.items():
    if only and take not in only:
        continue
    n = len(list((SRC / take / "game").glob("*.png"))) - 1
    TAKES_N[take] = n
    manifest[take] = {"frames": n, "loop": cfg["loop"], "events": cfg["events"], "videos": {}}
    for view in ("game", "side"):
        tmp = Path(tempfile.mkdtemp(prefix="rs_enc_"))
        seq = list(range(n)) if cfg["loop"] else list(range(n + 1)) + [n] * HOLD
        for i, f in enumerate(seq):
            img = Image.open(SRC / take / view / f"{f:04d}.png").convert("RGB")
            annotate(img, take, cfg, f, n, view, held=(i > n)).save(tmp / f"{i:04d}.jpg", quality=92)
        mp4 = OUT / f"{cfg['short']}_{view}.mp4"
        encode(tmp, mp4)
        shutil.rmtree(tmp, ignore_errors=True)
        manifest[take]["videos"][view] = {"file": mp4.name, "video_frames": len(seq), "kb": mp4.stat().st_size // 1024}
        print("MP4", mp4.name, len(seq), mp4.stat().st_size // 1024, "KB", flush=True)
    key_sheet(take, cfg)
(OUT / "media.json").write_text(json.dumps(manifest, indent=1, ensure_ascii=False), encoding="utf8")
print("ENCODE_DONE", OUT)
