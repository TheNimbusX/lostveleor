"""Скачивает реф движения Higgsfield и режет лист кадров для просмотра.

python fetch_motion_ref.py <mob_folder> <slug> <job_id> <url>
Кладёт <mob>/production/references/<slug>.mp4, <slug>_sheet.jpg (12 кадров/с, сетка) и дописывает jobs.json.
"""

import hashlib
import json
import subprocess
import sys
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]


def ffmpeg_exe():
    sys.path.insert(0, str(ROOT / "artifacts" / "tools" / "python"))
    import imageio_ffmpeg  # noqa: E402
    return imageio_ffmpeg.get_ffmpeg_exe()


def main():
    folder, slug, job_id, url = sys.argv[1:5]
    refs = HERE / folder / "production" / "references"
    refs.mkdir(parents=True, exist_ok=True)
    video = refs / f"{slug}.mp4"
    urllib.request.urlretrieve(url, video)
    digest = hashlib.sha256(video.read_bytes()).hexdigest()
    frames = refs / f"{slug}_frames"
    frames.mkdir(exist_ok=True)
    for old in frames.glob("*.jpg"):
        old.unlink()
    ffmpeg = ffmpeg_exe()
    subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-i", str(video), "-vf", "fps=6,scale=240:-2",
                    str(frames / "%03d.jpg")], check=True)
    from PIL import Image, ImageDraw
    images = sorted(frames.glob("*.jpg"))
    columns = 6
    width, height = Image.open(images[0]).size
    rows = (len(images) + columns - 1) // columns
    sheet = Image.new("RGB", (columns * width, rows * height), "black")
    draw = ImageDraw.Draw(sheet)
    for index, path in enumerate(images):
        x, y = (index % columns) * width, (index // columns) * height
        sheet.paste(Image.open(path), (x, y))
        draw.text((x + 4, y + 4), f"{index / 6:.2f}s", fill=(255, 255, 0))
    sheet.save(refs / f"{slug}_sheet.jpg", quality=85)
    jobs_path = refs / "jobs.json"
    jobs = json.loads(jobs_path.read_text(encoding="utf-8")) if jobs_path.exists() else []
    jobs = [job for job in jobs if job.get("slug") != slug]
    jobs.append({"slug": slug, "job_id": job_id, "model": "seedance_2_0 fast 720p 1:1 4s", "result_url": url,
                 "sha256": digest, "file": video.name, "sheet": f"{slug}_sheet.jpg"})
    jobs_path.write_text(json.dumps(jobs, ensure_ascii=False, indent=1), encoding="utf-8")
    print(refs / f"{slug}_sheet.jpg")


if __name__ == "__main__":
    main()
