"""Build review/animation_r01: labelled mp4s (game + side), key stills, reference sheets, manifest.json.

python package_review.py   (system python: PIL, imageio_ffmpeg). Run after render_review.py.
The page itself is written by review_page.py.
"""
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

import imageio_ffmpeg
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
PROD = HERE.parent
OUT = PROD / "review" / "animation_r01"
FR = OUT / "frames"
FF = imageio_ffmpeg.get_ffmpeg_exe()
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 17)
big = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 20)
build = json.loads((HERE / "build.json").read_text())
val = json.loads((HERE / "validation.json").read_text())

PHASES = {
    "Idle": [(0, "Ожидание: дыхание, покачивание, шипы плавают")],
    "Walk": [(0, "Ходьба на ходулях: 2,2 м/с, 1,76 м за цикл")],
    "LineCast": [(0, "Подъём обоих шипов над головой"), (18, "Удар вниз"), (24, "КОНТАКТ: шипы вошли в землю"),
                 (26, "Шипы в земле: волна по линии до тика 45"), (45, "Окно наказания: стоит согнутым"),
                 (60, "Выдёргивает шипы, выпрямляется")],
    "Burst": [(0, "Сжимается, руки к груди"), (18, "Рывок"), (21, "ВЗРЫВ: поза звезды"), (27, "Возврат в стойку")],
    "Shot": [(0, "Замах правым шипом, как копьём"), (15, "Прицел: линия полёта (оверлей ревью)"),
             (21, "ВЫСТРЕЛ: шип срывается с кончика"), (22, "Доводка вниз-поперёк"), (25, "Возврат в стойку")],
    "Hit": [(0, "Отдёргивается назад"), (3, "Возврат")],
    "Death": [(0, "Шатается назад"), (11, "Колени подламываются"), (22, "На коленях"), (29, "Падает лицом вниз"),
              (39, "Лежит неподвижно")],
}
REF = {"Idle": "idle_walk", "Walk": "idle_walk", "LineCast": "line_cast", "Burst": "burst", "Death": "death"}
KEYS = {"Idle": [0, 15, 30, 45], "Walk": [0, 6, 12, 18], "LineCast": [0, 18, 22, 24, 40, 68], "Burst": [0, 12, 18, 21, 27, 36],
        "Shot": [0, 15, 19, 21, 25, 33], "Hit": [0, 3, 6, 12], "Death": [0, 11, 22, 29, 39, 48]}


def phase(take, f):
    return [p for s, p in PHASES[take] if f >= s][-1]


def label(take, view, f, n, contact):
    im = Image.open(FR / take / view / f"{f:04d}.png").convert("RGB")
    w, h = im.size
    d = ImageDraw.Draw(im)
    d.rectangle((0, 0, w, 50), fill=(18, 24, 22))
    d.text((10, 5), f"ForestThorncaster_{take} · {'игровая камера' if view == 'game' else 'сбоку'}", font=font, fill=(234, 233, 223))
    hot = contact is not None and f == contact
    d.text((10, 26), f"кадр {f}/{n} · {phase(take, f)}", font=font, fill=(255, 196, 90) if hot else (190, 200, 188))
    # timeline with the contact marker
    y = h - 12
    d.rectangle((10, y, w - 10, y + 5), fill=(40, 48, 44))
    if contact is not None:
        cx = 10 + (w - 20) * contact / n
        d.rectangle((cx - 2, y - 5, cx + 2, y + 10), fill=(255, 140, 40))
    px = 10 + (w - 20) * f / n
    d.rectangle((px - 3, y - 2, px + 3, y + 7), fill=(234, 233, 223))
    if hot:
        d.rectangle((0, 0, w - 1, h - 1), outline=(255, 140, 40), width=6)
        d.text((w - 118, 60), "КОНТАКТ", font=big, fill=(255, 140, 40))
    return im


def encode(folder, target, frames):
    subprocess.run([FF, "-v", "error", "-framerate", "30", "-i", str(folder / "%04d.jpg"), "-frames:v", str(frames),
                    "-c:v", "libx264", "-crf", "23", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-y", str(target)], check=True)


(OUT / "refs").mkdir(parents=True, exist_ok=True)
(OUT / "stills").mkdir(exist_ok=True)
for slug in set(REF.values()):
    shutil.copy2(PROD / "references" / f"{slug}_sheet.jpg", OUT / "refs" / f"{slug}_sheet.jpg")
manifest = {"stage": "animation_r01_owner_review", "fps": 30, "takes": {}}
for take, info in build["takes"].items():
    n = info["frames"][1]
    contact = info.get("contact")
    loop = info["loop"]
    for view in ("game", "side"):
        tmp = Path(tempfile.mkdtemp(prefix=f"tc_{take}_{view}_"))
        seq = list(range(n)) * (4 if take == "Walk" else 2) if loop else list(range(n + 1)) + [n] * 12
        for i, f in enumerate(seq):
            label(take, view, f, n, contact).save(tmp / f"{i:04d}.jpg", quality=90)
        encode(tmp, OUT / f"{take}_{view}.mp4", len(seq))
        shutil.rmtree(tmp)
        for f in KEYS[take] + ([contact] if contact is not None else []):
            label(take, view, f, n, contact).resize((360, 360), Image.LANCZOS).save(OUT / "stills" / f"{take}_{view}_{f:04d}.jpg", quality=86)
    manifest["takes"][take] = {"fbx_take": "ForestThorncaster_" + take, "frames": info["frames"], "loop": loop, "contact": contact,
                               "reference_sheet": f"refs/{REF[take]}_sheet.jpg" if take in REF else None,
                               "videos": {"game": f"{take}_game.mp4", "side": f"{take}_side.mp4"},
                               "stills": sorted(set(KEYS[take] + ([contact] if contact is not None else []))),
                               "phases": [{"from": s, "label": p} for s, p in PHASES[take]]}
    print("PACKED", take, flush=True)
shutil.rmtree(FR)   # full PNG frames are in the mp4s now
manifest["validation"] = val
(OUT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=1), encoding="utf-8")
print("PACKAGE_REVIEW_OK", OUT)
