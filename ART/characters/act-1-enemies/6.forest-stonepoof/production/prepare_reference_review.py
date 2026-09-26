"""Archive generated motion references and build diagnostic contact sheets."""
from pathlib import Path
import json, subprocess, urllib.request, hashlib
import imageio_ffmpeg
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
REFS = ROOT / 'references'
REVIEW = ROOT / 'review' / 'motion_references'
REVIEW.mkdir(parents=True, exist_ok=True)
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
jobs = json.loads((REFS / 'jobs.json').read_text(encoding='utf8'))
for job in jobs:
    if job['status'] != 'completed':
        continue
    slug = job['slug']
    source = REFS / (slug + '.mp4')
    if not source.exists():
        urllib.request.urlretrieve(job['result_url'], source)
    job['sha256'] = hashlib.sha256(source.read_bytes()).hexdigest()
    frames = REVIEW / (slug + '_frames')
    frames.mkdir(exist_ok=True)
    subprocess.run([ffmpeg, '-v', 'error', '-y', '-i', str(source), '-vf', 'fps=12,scale=960:-2', '-q:v', '3', str(frames / '%04d.jpg')], check=True)
    pics = sorted(frames.glob('*.jpg'))
    job['preview_frames'] = len(pics)
    job['preview_fps'] = 12
    # Every third frame provides a readable quarter-second contact sheet.
    sampled = pics[::3]
    cols, w, h = 4, 384, 240
    sheet = Image.new('RGB', (cols*w, ((len(sampled)+cols-1)//cols)*h), '#202327')
    draw = ImageDraw.Draw(sheet)
    for i, path in enumerate(sampled):
        frame = Image.open(path).convert('RGB')
        frame.thumbnail((w, 216))
        x, y = (i%cols)*w, (i//cols)*h
        sheet.paste(frame, (x, y))
        draw.text((x+8,y+220), f'{slug}  {(int(path.stem)-1)/12:.2f}s', fill='white')
    sheet.save(REVIEW / (slug + '_sheet.jpg'), quality=92)
    subprocess.run([ffmpeg, '-v', 'error', '-y', '-i', str(source), '-an', '-c:v', 'libvpx-vp9', '-crf', '25', '-b:v', '0', '-row-mt', '1', '-cpu-used', '4', '-pix_fmt', 'yuv420p', str(REVIEW / (slug+'.webm'))], check=True)
    print(slug, len(pics), 'frames', source.stat().st_size, 'bytes')
(REFS / 'jobs.json').write_text(json.dumps(jobs, ensure_ascii=False, indent=2), encoding='utf8')
