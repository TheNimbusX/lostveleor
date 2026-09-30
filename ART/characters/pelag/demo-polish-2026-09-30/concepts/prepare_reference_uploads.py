"""Encode approved reference images for transport; leave originals untouched."""
import hashlib
import json
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent.parent
manifest = json.loads((root / 'concepts/requests.json').read_text(encoding='utf-8'))
destination = root / 'concepts/upload-references'
destination.mkdir(exist_ok=True)
records = []
for index, reference in enumerate(manifest['requests'][0]['references'], 1):
    source = (root / 'concepts' / reference['path']).resolve()
    target = destination / ('ref_%02d.jpg' % index)
    with Image.open(source) as original:
        image = original.convert('RGB')
        image.thumbnail((1536, 1536), Image.Resampling.LANCZOS)
        image.save(target, 'JPEG', quality=78, optimize=True)
        records.append({'index': index, 'source': str(source), 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'transport': str(target), 'transport_sha256': hashlib.sha256(target.read_bytes()).hexdigest(), 'dimensions': list(image.size), 'bytes': target.stat().st_size, 'role': reference['role']})
(destination / 'manifest.json').write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps([{'index': record['index'], 'bytes': record['bytes'], 'dimensions': record['dimensions']} for record in records]))
