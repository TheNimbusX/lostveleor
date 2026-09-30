"""Build a linear material control mask; never rewrite the painted albedo or mesh."""
from pathlib import Path
import json
import uuid
import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parents[5]
FOLDER = ROOT / 'razlom/Assets/Resources/Characters/Pelag_v6'
source = FOLDER / 'Pelag_v6_BaseColor.jpg'
image = Image.open(source).convert('RGB')
hsv = np.asarray(image.convert('HSV')).astype(np.float32)
hue, saturation, value = hsv[:, :, 0] * (360 / 255), hsv[:, :, 1] / 255, hsv[:, :, 2] / 255
candidate = ((hue >= 16) & (hue <= 31) & (saturation >= .48) &
             (saturation <= .88) & (value >= .42) & (value <= .97))
labels, count = ndimage.label(candidate)
areas = np.bincount(labels.ravel())
keep = areas > 1500
keep[0] = False
mask = keep[labels].astype(np.uint8) * 255
# Explicitly protect the three gold fittings, even if compression changes their hue.
for x0, y0, x1, y1 in ((1190, 1527, 1251, 1584), (2861, 3081, 2931, 3147), (900, 965, 972, 1057)):
    mask[y0:y1, x0:x1] = 0
output = FOLDER / 'Pelag_SkinMask.png'
Image.fromarray(mask).resize((1024, 1024), Image.Resampling.LANCZOS).save(output)
meta = Path(str(output) + '.meta')
if not meta.exists():
    template = Path(str(source) + '.meta').read_text(encoding='utf-8')
    old_guid = next(line[6:] for line in template.splitlines() if line.startswith('guid: '))
    template = template.replace(old_guid, uuid.uuid4().hex)
    template = template.replace('sRGBTexture: 1', 'sRGBTexture: 0')
    template = template.replace('maxTextureSize: 2048', 'maxTextureSize: 1024')
    meta.write_text(template, encoding='utf-8')
report = {'source': str(source), 'dimensions': list(image.size),
          'mask_dimensions': [1024, 1024], 'skin_coverage': float(np.mean(mask > 0)),
          'accepted_components': int(np.sum(keep)), 'mask_guid':
          next(line[6:] for line in meta.read_text(encoding='utf-8').splitlines() if line.startswith('guid: '))}
Path(__file__).with_name('skin_mask_report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
