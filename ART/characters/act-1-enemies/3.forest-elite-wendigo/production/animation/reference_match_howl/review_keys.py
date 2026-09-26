"""Blocking review: every key pose next to its reference station (fit / game / side cameras).

python review_keys.py [--keys 0,4,6] [--size 300]
Writes breakdown/keys_sheet_*.jpg. Needs key_poses.json from keys_report.py.
"""
import json, subprocess, argparse
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from pose_keys import REF_OF_KEY

HERE = Path(__file__).resolve().parent
BLENDER = 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe'
SCENE = HERE.parent / 'unity_package' / 'ForestWendigo_Production.blend'
ap = argparse.ArgumentParser(); ap.add_argument('--keys', default=''); ap.add_argument('--size', type=int, default=300)
ap.add_argument('--poses', default=str(HERE / 'key_poses.json')); ap.add_argument('--tag', default='keys')
args = ap.parse_args()
poses = json.loads(Path(args.poses).read_text())['frames']
keys = args.keys.split(',') if args.keys else sorted(poses, key=float)
cam = ','.join(map(str, json.loads((HERE / 'landmarks.json').read_text())['camera']))
tmp = HERE / 'breakdown' / ('render_' + args.tag)
for view, res in [('fit', '960x960'), ('game', '960x720'), ('side', '960x720')]:
    cmd = [BLENDER, '-b', str(SCENE), '--python', str(HERE / 'render_poses.py'), '--', '--poses', args.poses,
           '--out', str(tmp / view), '--camera', view, '--res', res, '--samples', '6', '--frames', ','.join(keys),
           '--names', ','.join(keys)]
    if view == 'fit': cmd += ['--fit', cam]
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 18)
S = args.size
per = 5
for c in range(0, len(keys), per):
    chunk = keys[c:c + per]
    sheet = Image.new('RGB', (S * 4, S * len(chunk)), (18, 20, 20))
    for i, k in enumerate(chunk):
        ref_f = REF_OF_KEY.get(int(float(k)), None)
        tiles = []
        if ref_f is not None:
            tiles.append(Image.open(HERE / 'reference' / f'ref_{ref_f + 1:03d}.png').convert('RGB').resize((S, S)))
        else:
            tiles.append(Image.new('RGB', (S, S), (40, 40, 40)))
        tiles.append(Image.open(tmp / 'fit' / f'pose_{k}.png').convert('RGB').resize((S, S)))
        for v in ('game', 'side'):
            im = Image.open(tmp / v / f'pose_{k}.png').convert('RGB')
            im = im.crop((120, 0, 840, 720)).resize((S, S))
            tiles.append(im)
        for j, t in enumerate(tiles):
            sheet.paste(t, (j * S, i * S))
        d = ImageDraw.Draw(sheet)
        d.text((6, i * S + 4), f'key {k}  ref {ref_f}', font=font, fill=(255, 255, 0))
    sheet.save(HERE / 'breakdown' / f'{args.tag}_sheet_{c // per}.jpg', quality=88)
print('sheets', (len(keys) + per - 1) // per)
