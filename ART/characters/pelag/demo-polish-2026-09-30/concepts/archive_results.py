import hashlib, json, pathlib, urllib.request
root = pathlib.Path(__file__).resolve().parent
manifest = json.loads((root / 'results.json').read_text(encoding='utf-8'))
for result in manifest['results']:
    request = urllib.request.Request(result['result_url'], headers={'User-Agent': 'Mozilla/5.0'})
    with urllib.request.urlopen(request, timeout=90) as response:
        data = response.read()
    if not data.startswith(bytes.fromhex('89504e470d0a1a0a')):
        raise ValueError('Result is not a PNG: ' + result['id'])
    (root / result['file']).write_bytes(data)
    result['sha256'] = hashlib.sha256(data).hexdigest()
    result['bytes'] = len(data)
    print(f"{result['id']}: archived {len(data)} bytes")
(root / 'results.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
