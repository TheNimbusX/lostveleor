"""Звуки Epidemic для мобов (29.09): забрать из «Загрузок», сопоставить со списком, собрать доску на слух.

python build_board.py → raw/*.wav, downloads.csv, board/*.mp3, board/index.html
"""

import csv
import html
import re
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
RAW = HERE / "raw"
BOARD = HERE / "board"
DOWNLOADS = Path.home() / "Downloads"
SCRATCH_TOOLS = Path(r"C:\Users\DE06B~1.GRA\AppData\Local\Temp\claude\C--Users-d-grab-Desktop-the-game\b9a62d32-b302-4af1-9255-9c30476f872b\scratchpad\pytools")


def ffmpeg():
    sys.path.insert(0, str(SCRATCH_TOOLS))
    import imageio_ffmpeg
    return imageio_ffmpeg.get_ffmpeg_exe()


def norm(title):
    return re.sub(r"[^a-z0-9]+", " ", title.lower()).strip()


def shortlist():
    rows = []
    for line in (HERE / "shortlist.md").read_text(encoding="utf-8").splitlines():
        if not line.startswith("|") or "---" in line or line.startswith("| slot"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) >= 7:
            rows.append({"slot": cells[0], "n": cells[1], "title": cells[2], "dur": cells[3], "url": cells[5], "why": cells[6]})
    return rows


def main():
    RAW.mkdir(exist_ok=True)
    BOARD.mkdir(exist_ok=True)
    for f in DOWNLOADS.glob("ES_*.wav"):
        target = RAW / re.sub(r" \(\d+\)(?=\.wav$)", "", f.name)
        if target.exists():
            f.unlink()
        else:
            shutil.move(str(f), target)
    files = {norm(p.name[3:].replace(" - Epidemic Sound.wav", "")): p for p in RAW.glob("ES_*.wav")}
    rows = shortlist()
    ff = ffmpeg()
    missing = []
    with open(HERE / "downloads.csv", "w", encoding="utf-8", newline="") as out:
        writer = csv.writer(out)
        writer.writerow(["slot", "candidate", "title", "file", "bytes"])
        for r in rows:
            p = files.get(norm(r["title"]))
            if not p:
                # Скачан соседний вариант того же звука (другой хвост названия) — берём его.
                head = " ".join(norm(r["title"]).split()[:5])
                p = next((f for key, f in files.items() if key.startswith(head)), None)
                if p:
                    r["title"] += f" (скачан: {p.stem[3:].replace(' - Epidemic Sound', '')})"
            r["file"] = p
            writer.writerow([r["slot"], r["n"], r["title"], p.name if p else "", p.stat().st_size if p else 0])
            if not p:
                missing.append(r["title"])
                continue
            mp3 = BOARD / (re.sub(r"[^A-Za-z0-9]+", "_", p.stem)[:80] + ".mp3")
            r["mp3"] = mp3.name
            if not mp3.exists():
                subprocess.run([ff, "-y", "-loglevel", "error", "-i", str(p), "-ac", "2", "-b:a", "160k", str(mp3)], check=True)
    groups = {}
    for r in rows:
        mob, _, slot = r["slot"].partition(":")
        groups.setdefault(mob.strip(), {}).setdefault(slot.strip() or mob.strip(), []).append(r)
    parts = ['<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">',
             "<title>Звуки мобов · выбор</title>",
             "<style>:root{color-scheme:dark}body{margin:0;background:#141b19;color:#eae9df;font:15px/1.5 'Segoe UI',sans-serif}"
             "main{max-width:1200px;margin:auto;padding:28px 24px 60px}h1{font-size:32px;margin:6px 0}h2{font-size:24px;margin:34px 0 8px;padding-top:14px;border-top:1px solid #3b473e}"
             "h3{font-size:15px;color:#cad69d;text-transform:uppercase;letter-spacing:.1em;margin:18px 0 6px}"
             ".row{display:grid;grid-template-columns:52px 1fr 320px;gap:12px;align-items:center;padding:6px 10px;border:1px solid #3b473e;border-radius:10px;margin:6px 0;background:#202925}"
             ".row b{color:#cad69d}.row small{color:#b3beb1}audio{width:100%}p{color:#b3beb1;max-width:80ch}</style>",
             "<main><p style='letter-spacing:.14em;text-transform:uppercase;color:#cad69d;font-size:12px'>Лес · звуки мобов · 29 сентября 2026</p>",
             "<h1>Звуки мобов: выбор на слух</h1><p>Кандидаты с Epidemic Sound целиком (многие файлы содержат несколько вариантов — нарезку сделаю после выбора). Ответ в формате «Хранитель взмах: 2, удар: 1+3 …» — можно брать несколько как слои.</p>"]
    for mob, slots in groups.items():
        parts.append(f"<h2>{html.escape(mob)}</h2>")
        for slot, items in slots.items():
            parts.append(f"<h3>{html.escape(slot)}</h3>")
            for r in items:
                player = f'<audio controls preload="none" src="{r["mp3"]}"></audio>' if r.get("mp3") else "<small>нет файла</small>"
                parts.append(f'<div class="row"><b>{html.escape(r["n"])}</b><div>{html.escape(r["title"])} <small>· {html.escape(r["dur"])} · {html.escape(r["why"])}</small></div>{player}</div>')
    parts.append("</main></html>")
    (BOARD / "index.html").write_text("\n".join(parts), encoding="utf-8")
    print(f"rows {len(rows)}, raw files {len(files)}, missing {len(missing)}")
    for m in missing:
        print("MISSING:", m)


if __name__ == "__main__":
    main()
