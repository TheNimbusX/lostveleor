"""Check the review bundle before owner approval, without modifying it."""
import hashlib
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent
manifest = json.loads((root / 'concepts/requests.json').read_text(encoding='utf-8'))
costs = json.loads((root / 'concepts/cost-preflight.json').read_text(encoding='utf-8'))
page = (root / 'review/index.html').read_text(encoding='utf-8')
links = re.findall(r'(?:src|href|data-image)="([^"]+)"', page)
missing = [link for link in links if not link.startswith(('http', '#')) and not (root / 'review' / link).is_file()]
assert not missing, missing
assert manifest['generation_jobs_submitted'] == 2
results = json.loads((root / 'concepts/results.json').read_text(encoding='utf-8'))
receipt = json.loads((root / 'concepts/completion-receipt.json').read_text(encoding='utf-8'))
assert results['owner_selection'] is None
assert len(results['results']) == len(receipt['items']) == 2
assert receipt['cost']['observed_balance_delta'] == receipt['cost']['approved_credits'] == 4
for request, result in zip(manifest['requests'], results['results']):
    item = next(item for item in receipt['items'] if item['id'] == result['job_id'])
    assert item['status'] == result['status'] == 'completed'
    assert item['params']['prompt'] == request['params']['prompt']
    assert len(item['params']['input_images']) == 5
    assert [image['id'] for image in item['params']['input_images']] == result['reference_media_ids']
    assert hashlib.sha256((root / 'concepts' / result['file']).read_bytes()).hexdigest() == result['sha256']
assert sum(request['cost']['credits'] for request in costs['requests']) == manifest['total_estimated_credits'] == 4
assert len(manifest['requests']) == 2
for request, cost in zip(manifest['requests'], costs['requests']):
    prompt = (root / 'concepts' / (request['id'] + '.prompt.txt')).read_text(encoding='utf-8').rstrip()
    assert prompt == request['params']['prompt']
    assert cost['params'] == request['params']
    assert all((root / 'concepts' / reference['path']).is_file() for reference in request['references'])
assert len({request['params']['prompt'] for request in manifest['requests']}) == 2
unity = json.loads((root / 'baseline/unity-live-audit.json').read_text(encoding='utf-8'))
blender = json.loads((root / 'baseline/blender-source-audit.json').read_text(encoding='utf-8'))
source = root.parents[3] / 'razlom/Assets/Resources/Characters/Pelag_v6/Runtime/Pelag_v6_MixamoRig.fbx'
assert unity['source_body_sha256'] == blender['source_sha256'] == hashlib.sha256(source.read_bytes()).hexdigest()
print('PASS: links, two completed jobs, five references each, exact prompts, 4 credits, image hashes, unchanged body source')
